using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace ELE.Core.Systems
{
    public class InjectorSystem
    {
        private readonly ModEntry Mod;
        
        // IDs: (O) es vital para coincidir con el inventario
        private const string InjectorItemId = "(O)JavCombita.ELE_AlchemicalInjector";
        // String parcial para detectar los mutágenos
        private const string MutagenPartId = "JavCombita.ELE_Mutagen"; 
        
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
            // Solo lógica de mundo
            if (!Context.IsWorldReady || !Context.IsPlayerFree) return;
            
            // Unificamos botón de uso (Clic / Tap)
            if (!e.Button.IsUseToolButton() && !e.Button.IsActionButton()) return;

            Item heldItem = Game1.player.CurrentItem;
            if (heldItem == null) return;

            // --- CASO A: SOSTENIENDO EL INYECTOR (Disparar) ---
            if (heldItem.ItemId.Contains("JavCombita.ELE_AlchemicalInjector"))
            {
                if (TryGetTargetCrop(e.Cursor.Tile, out HoeDirt dirt, out Vector2 tile))
                {
                    // Si está en rango, disparamos.
                    if (IsInRange(tile))
                    {
                        HandleInjection(heldItem, dirt, tile);
                        Mod.Helper.Input.Suppress(e.Button);
                    }
                    // Si NO está en rango, no hacemos nada (ni suprimimos), así el jugador camina.
                }
            }
            // --- CASO B: SOSTENIENDO UN MUTÁGENO (Recargar) ---
            else if (heldItem.ItemId.Contains(MutagenPartId))
            {
                // >>> FIX ANDROID: Solo recargar si tocamos al JUGADOR <<<
                if (IsClickingOnPlayer(e.Cursor.Tile))
                {
                    HandleReloadFromHand(heldItem);
                    Mod.Helper.Input.Suppress(e.Button);
                }
                // Si tocamos cualquier otro lado (suelo, aire, UI), NO hacemos nada.
                // Esto permite caminar, abrir inventario, etc.
            }
        }

        // >>> NUEVO: Detecta si el clic/tap fue sobre el granjero <<<
        private bool IsClickingOnPlayer(Vector2 cursorTile)
        {
            Vector2 playerTile = Game1.player.Tile;
            // Verificamos si tocamos el tile de los pies (Tile) o el tile de la cabeza (Tile.Y - 1)
            // Esto hace que sea fácil atinarle en pantallas táctiles
            return cursorTile == playerTile || cursorTile == new Vector2(playerTile.X, playerTile.Y - 1);
        }

        // ============================================================================================
        // ?? LÓGICA DE RECARGA SIN LÍMITES
        // ============================================================================================
        private void HandleReloadFromHand(Item mutagenInHand)
        {
            // 1. Buscar el Inyector en el inventario (búsqueda flexible)
            Item injector = Game1.player.Items.FirstOrDefault(i => i != null && i.ItemId.Contains("JavCombita.ELE_AlchemicalInjector"));

            if (injector == null)
            {
                Game1.showRedMessage("No Injector found in inventory!"); 
                return;
            }

            // 2. Leer estado actual
            int currentLoad = 0;
            string currentType = null;
            if (injector.modData.TryGetValue(AmmoCountKey, out string c)) int.TryParse(c, out currentLoad);
            if (injector.modData.TryGetValue(AmmoTypeKey, out string t)) currentType = t;

            // 3. LÓGICA DE SWAP (Si hay munición y es DIFERENTE, la expulsamos)
            if (currentLoad > 0 && !string.IsNullOrEmpty(currentType) && currentType != mutagenInHand.ItemId)
            {
                Item oldAmmo = ItemRegistry.Create(currentType, currentLoad);
                
                // Intentar devolver al inventario
                if (Game1.player.addItemToInventoryBool(oldAmmo))
                {
                    Game1.playSound("coin");
                    currentLoad = 0; 
                    injector.modData[AmmoCountKey] = "0";
                }
                else
                {
                    // Si inventario lleno, tiramos al suelo
                    Game1.createItemDebris(oldAmmo, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
                    currentLoad = 0;
                    injector.modData[AmmoCountKey] = "0";
                }
            }

            // 4. CALCULAR RECARGA (SIN LÍMITE)
            int toLoad = mutagenInHand.Stack;

            // 5. APLICAR CAMBIOS
            injector.modData[AmmoCountKey] = (currentLoad + toLoad).ToString();
            injector.modData[AmmoTypeKey] = mutagenInHand.ItemId; 

            // 6. CONSUMIR TODO EL STACK DE LA MANO
            Game1.player.Items[Game1.player.CurrentToolIndex] = null;

            // 7. FEEDBACK
            Game1.playSound("load_gun");
            Game1.showGlobalMessage(Mod.Helper.Translation.Get("injector.reloaded", new { count = toLoad }));
            
            // Animación
            Game1.player.FarmerSprite.animateOnce(new FarmerSprite.AnimationFrame[] {
                new FarmerSprite.AnimationFrame(57, 300),
                new FarmerSprite.AnimationFrame(0, 100)
            });
        }

        // ============================================================================================
        // ?? LÓGICA DE DISPARO
        // ============================================================================================
        private void HandleInjection(Item tool, HoeDirt dirt, Vector2 tile)
        {
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
            
            // Animación
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

        private bool IsInRange(Vector2 targetTile)
        {
            Vector2 playerTile = Game1.player.Tile;
            return Math.Abs(targetTile.X - playerTile.X) <= 1 && Math.Abs(targetTile.Y - playerTile.Y) <= 1;
        }

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (Game1.player.CurrentItem == null || !Game1.player.CurrentItem.ItemId.Contains("JavCombita.ELE_AlchemicalInjector")) return;
            
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