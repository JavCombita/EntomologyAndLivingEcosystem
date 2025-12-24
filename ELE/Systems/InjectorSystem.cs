using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;

namespace ELE.Core.Systems
{
    public class InjectorSystem
    {
        private readonly ModEntry Mod;
        
        // IDs (IMPORTANTE: Prefijo (O) agregado para coincidir con el inventario)
        private const string InjectorItemId = "(O)JavCombita.ELE_AlchemicalInjector";
        private const string MutagenBaseId = "JavCombita.ELE_Mutagen"; 
        
        // Keys para ModData
        private const string AmmoCountKey = "ele_injector_ammo_count";
        private const string AmmoTypeKey = "ele_injector_ammo_type";

        public InjectorSystem(ModEntry mod)
        {
            this.Mod = mod;
            mod.Helper.Events.Input.ButtonPressed += OnButtonPressed;
            mod.Helper.Events.Display.RenderedWorld += OnRenderedWorld; 
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            // 1. LÓGICA DE INVENTARIO (Drag & Drop)
            if (Game1.activeClickableMenu != null)
            {
                HandleMenuInteraction(e);
                return; 
            }

            // 2. LÓGICA DE MUNDO
            if (!Context.IsWorldReady || !Context.IsPlayerFree) return;

            // Verificar item en mano (usando Contains para ser flexible con prefijos si hiciera falta)
            if (Game1.player.CurrentItem == null || Game1.player.CurrentItem.ItemId != InjectorItemId) return;

            // Unificamos Left Click (PC) y Tap (Android)
            if (e.Button.IsUseToolButton() || e.Button == SButton.MouseLeft)
            {
                if (TryGetTargetCrop(e.Cursor.Tile, out HoeDirt dirt, out Vector2 tile))
                {
                    // >>> FIX RANGO: Si está lejos, NO hacemos nada. 
                    // El juego procesará el clic como "Moverse" por defecto.
                    if (!IsInRange(tile)) return;

                    // Si está cerca, disparamos
                    HandleInjection(dirt, tile);
                    Mod.Helper.Input.Suppress(e.Button);
                }
            }
        }

        private bool IsInRange(Vector2 targetTile)
        {
            Vector2 playerTile = Game1.player.Tile;
            return Math.Abs(targetTile.X - playerTile.X) <= 1 && Math.Abs(targetTile.Y - playerTile.Y) <= 1;
        }

        // Helper robusto para encontrar el item bajo el mouse en cualquier menú
        private Item GetHoveredItem()
        {
            if (Game1.activeClickableMenu == null) return null;

            // Caso Especial: Menú Principal (Inventario con pestañas, tecla E)
            if (Game1.activeClickableMenu is GameMenu gameMenu)
            {
                var pages = Mod.Helper.Reflection.GetField<List<IClickableMenu>>(gameMenu, "pages").GetValue();
                if (pages != null && gameMenu.currentTab >= 0 && gameMenu.currentTab < pages.Count)
                {
                    var currentPage = pages[gameMenu.currentTab];
                    return Mod.Helper.Reflection.GetField<Item>(currentPage, "hoveredItem", false)?.GetValue();
                }
            }
            
            // Caso Estándar: Cofres, Máquinas, Tiendas
            return Mod.Helper.Reflection.GetField<Item>(Game1.activeClickableMenu, "hoveredItem", false)?.GetValue();
        }

        private void HandleMenuInteraction(ButtonPressedEventArgs e)
        {
            if (e.Button != SButton.MouseLeft && e.Button != SButton.MouseRight) return;

            Item heldItem = Game1.player.CursorSlotItem;
            Item hoveredItem = GetHoveredItem(); // Usamos el nuevo helper

            // Validamos que ambos existan antes de chequear IDs
            if (heldItem == null || hoveredItem == null) return;

            // CASO: Arrastrar Mutágeno (held) sobre Inyector (hovered)
            // Usamos .Contains para el Mutágeno para soportar Growth/Chaos
            if (heldItem.ItemId.Contains(MutagenBaseId) && hoveredItem.ItemId == InjectorItemId)
            {
                PerformReloadLogic(hoveredItem, heldItem);
                Mod.Helper.Input.Suppress(e.Button); // ESTO EVITA EL SWAP
            }
        }

        private void PerformReloadLogic(Item injector, Item ammoSource)
        {
            int currentLoad = 0;
            string currentAmmoType = null;

            if (injector.modData.TryGetValue(AmmoCountKey, out string countStr)) int.TryParse(countStr, out currentLoad);
            if (injector.modData.TryGetValue(AmmoTypeKey, out string typeStr)) currentAmmoType = typeStr;

            Item ejectedAmmo = null;

            // 1. SWAP (Si cambiamos de tipo)
            if (currentLoad > 0 && !string.IsNullOrEmpty(currentAmmoType) && currentAmmoType != ammoSource.ItemId)
            {
                ejectedAmmo = ItemRegistry.Create(currentAmmoType, currentLoad);
                currentLoad = 0;
                injector.modData[AmmoCountKey] = "0";
            }

            // 2. CHECK ESPACIO
            int maxCapacity = 20;
            int spaceFree = maxCapacity - currentLoad;

            if (spaceFree <= 0)
            {
                Game1.playSound("cancel");
                return;
            }

            // 3. TRANSFERIR
            int toLoad = Math.Min(spaceFree, ammoSource.Stack);
            
            injector.modData[AmmoCountKey] = (currentLoad + toLoad).ToString();
            injector.modData[AmmoTypeKey] = ammoSource.ItemId; 

            // 4. CONSUMIR FUENTE
            ammoSource.Stack -= toLoad;

            // 5. MANEJAR SOBRANTES (Devolver munición vieja)
            if (ejectedAmmo != null)
            {
                Game1.playSound("coin");

                if (ammoSource.Stack <= 0)
                {
                     // Si la mano quedó vacía, ponemos la munición vieja en la mano (Swap perfecto)
                     Game1.player.CursorSlotItem = ejectedAmmo;
                }
                else
                {
                    // Si sobró en la mano, intentamos guardar lo viejo en el inventario
                    if (!Game1.player.addItemToInventoryBool(ejectedAmmo))
                    {
                        Game1.createItemDebris(ejectedAmmo, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
                    }
                }
            }
            
            // Limpiar cursor si se acabó el stack y no hubo swap
            if (ammoSource.Stack <= 0 && ejectedAmmo == null)
            {
                Game1.player.CursorSlotItem = null;
            }

            Game1.playSound("load_gun"); 
        }

        private bool TryGetTargetCrop(Vector2 cursorTile, out HoeDirt dirt, out Vector2 tileLocation)
        {
            dirt = null;
            tileLocation = cursorTile;
            GameLocation loc = Game1.currentLocation;

            if (loc.terrainFeatures.TryGetValue(cursorTile, out TerrainFeature tf) && tf is HoeDirt hd && hd.crop != null)
            {
                dirt = hd;
                return true;
            }

            Vector2 grabTile = new Vector2((int)(Game1.player.GetToolLocation().X / 64f), (int)(Game1.player.GetToolLocation().Y / 64f));
            if (grabTile != cursorTile && loc.terrainFeatures.TryGetValue(grabTile, out TerrainFeature tf2) && tf2 is HoeDirt hd2 && hd2.crop != null)
            {
                dirt = hd2;
                tileLocation = grabTile;
                return true;
            }

            return false;
        }

        private void HandleInjection(HoeDirt dirt, Vector2 tile)
        {
            Item tool = Game1.player.CurrentItem;

            if (!tool.modData.TryGetValue(AmmoCountKey, out string cStr) || !int.TryParse(cStr, out int currentAmmo) || currentAmmo <= 0)
            {
                Game1.playSound("click"); 
                Game1.showRedMessage(Mod.Helper.Translation.Get("injector.no_ammo"));
                return;
            }

            GameLocation loc = Game1.currentLocation;
            ApplyMutagenEffect(loc, tile, dirt, tool.modData[AmmoTypeKey]);
            
            int newCount = currentAmmo - 1;
            tool.modData[AmmoCountKey] = newCount.ToString();
            
            // FIX ANIMACIÓN: Usando FarmerSprite.AnimationFrame
            Game1.player.FarmerSprite.animateOnce(new FarmerSprite.AnimationFrame[] {
                new FarmerSprite.AnimationFrame(57, 100), 
                new FarmerSprite.AnimationFrame(58, 100), 
                new FarmerSprite.AnimationFrame(0, 100)
            });
        }

        private void ApplyMutagenEffect(GameLocation loc, Vector2 tile, HoeDirt dirt, string mutagenId)
        {
            Random methodRng = new Random(Guid.NewGuid().GetHashCode());
            
            if (mutagenId.Contains("Growth"))
            {
                if (dirt.crop.currentPhase.Value < dirt.crop.phaseDays.Count - 1)
                {
                    dirt.crop.currentPhase.Value++;
                    Game1.playSound("wand");
                    Mod.Ecosystem.RestoreNutrients(loc, tile, "(O)JavCombita.ELE_Fertilizer_Omni"); 
                }
            }
            else if (mutagenId.Contains("Chaos")) 
            {
                double roll = methodRng.NextDouble();
                if (roll < 0.30) {
                    loc.playSound("shadowDie");
                    Game1.createRadialDebris(loc, 12, (int)tile.X, (int)tile.Y, 6, false);
                    dirt.crop = null; 
                    var monster = new StardewValley.Monsters.RockCrab(tile * 64f, "JavCombita.ELE_MelonCrab");
                    monster.wildernessFarmMonster = true; 
                    loc.addCharacter(monster); 
                } else if (roll < 0.60) {
                    string cropId = dirt.crop.indexOfHarvest.Value;
                    bool isGiantCapable = (cropId == "190" || cropId == "254" || cropId == "276");
                    if (isGiantCapable) TryForceGiantCrop(loc, tile, cropId);
                    else {
                        dirt.crop.growCompletely();
                        Game1.playSound("reward");
                    }
                } else {
                    dirt.crop.currentPhase.Value++;
                    Game1.playSound("bubbles");
                }
            }
        }

        private void TryForceGiantCrop(GameLocation loc, Vector2 centerTile, string cropId)
        {
            Vector2 topLeft = centerTile - new Vector2(1, 1);
            if (!loc.isTileOnMap(topLeft) || !loc.isTileOnMap(topLeft + new Vector2(2, 2))) return;

            bool areaClear = true;
            for (int x = 0; x < 3; x++) {
                for (int y = 0; y < 3; y++) {
                    Vector2 current = topLeft + new Vector2(x, y);
                    if (loc.terrainFeatures.TryGetValue(current, out TerrainFeature tf)) {
                        if (!(tf is HoeDirt)) { areaClear = false; break; }
                    }
                }
            }

            if (areaClear) {
                for (int x = 0; x < 3; x++) {
                    for (int y = 0; y < 3; y++) {
                        Vector2 current = topLeft + new Vector2(x, y);
                        if (loc.terrainFeatures.TryGetValue(current, out TerrainFeature tf) && tf is HoeDirt hd) hd.crop = null;
                    }
                }
                loc.resourceClumps.Add(new GiantCrop(cropId, topLeft));
                Game1.playSound("stumpCrack");
                Game1.createRadialDebris(loc, 12, (int)centerTile.X, (int)centerTile.Y, 12, false);
            } else {
                if (loc.terrainFeatures.TryGetValue(centerTile, out TerrainFeature tf) && tf is HoeDirt hd && hd.crop != null) hd.crop.growCompletely();
            }
        }

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (Game1.player.CurrentItem == null || Game1.player.CurrentItem.ItemId != InjectorItemId) return;
            Item tool = Game1.player.CurrentItem;
            if (tool.modData.TryGetValue(AmmoCountKey, out string count) && int.TryParse(count, out int ammoVal))
            {
                Vector2 playerPos = Game1.player.getLocalPosition(Game1.viewport);
                Vector2 textPos = playerPos + new Vector2(10, -110);
                e.SpriteBatch.DrawString(Game1.smallFont, ammoVal.ToString(), textPos + new Vector2(2, 2), Color.Black);
                e.SpriteBatch.DrawString(Game1.smallFont, ammoVal.ToString(), textPos, Color.Yellow);
            }
        }
    }
}