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
        
        // IDs
        private const string InjectorItemId = "JavCombita.ELE_AlchemicalInjector";
        private const string MutagenBaseId = "JavCombita.ELE_Mutagen"; 
        
        // Keys para ModData
        private const string AmmoCountKey = "ele_injector_ammo_count";
        private const string AmmoTypeKey = "ele_injector_ammo_type";

        public InjectorSystem(ModEntry mod)
        {
            this.Mod = mod;
            mod.Helper.Events.Input.ButtonPressed += OnButtonPressed;
            mod.Helper.Events.Display.RenderedHud += OnRenderedHud;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            if (Game1.player.CurrentItem == null || Game1.player.CurrentItem.ItemId != InjectorItemId) return;

            if (e.Button.IsActionButton())
            {
                HandleReload();
                Mod.Helper.Input.Suppress(e.Button); 
            }
            else if (e.Button.IsUseToolButton())
            {
                HandleInjection(e);
                Mod.Helper.Input.Suppress(e.Button); 
            }
        }

        private void HandleReload()
        {
            Item tool = Game1.player.CurrentItem;
            Item ammo = Game1.player.Items.FirstOrDefault(i => i != null && i.ItemId.Contains(MutagenBaseId));

            if (ammo == null)
            {
                Game1.drawObjectDialogue(Mod.Helper.Translation.Get("injector.no_ammo"));
                return;
            }

            int currentLoad = 0;
            if (tool.modData.TryGetValue(AmmoCountKey, out string countStr)) int.TryParse(countStr, out currentLoad);

            int spaceFree = 20 - currentLoad;
            if (spaceFree <= 0)
            {
                Game1.showRedMessage(Mod.Helper.Translation.Get("injector.full"));
                return;
            }

            int toLoad = Math.Min(spaceFree, ammo.Stack);
            
            tool.modData[AmmoCountKey] = (currentLoad + toLoad).ToString();
            tool.modData[AmmoTypeKey] = ammo.ItemId; 

            ammo.Stack -= toLoad;
            if (ammo.Stack <= 0) Game1.player.Items.Remove(ammo);

            Game1.playSound("load_gun"); 
            Game1.showGlobalMessage(Mod.Helper.Translation.Get("injector.reloaded", new { count = toLoad }));
        }

        private void HandleInjection(ButtonPressedEventArgs e)
        {
            Item tool = Game1.player.CurrentItem;

            if (!tool.modData.TryGetValue(AmmoCountKey, out string cStr) || int.Parse(cStr) <= 0)
            {
                Game1.playSound("click"); 
                return;
            }

            Vector2 tile = e.Cursor.Tile;
            GameLocation loc = Game1.currentLocation;
            
            if (loc.terrainFeatures.TryGetValue(tile, out TerrainFeature tf) && tf is HoeDirt dirt && dirt.crop != null)
            {
                ApplyMutagenEffect(loc, tile, dirt, tool.modData[AmmoTypeKey]);
                
                int newCount = int.Parse(cStr) - 1;
                tool.modData[AmmoCountKey] = newCount.ToString();
                
                Game1.player.animateOnce(284); 
            }
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
                
                // Ajusté ligeramente la lógica para limpiar el cultivo ANTES de spawnear el monstruo
				if (roll < 0.30) 
				{
					// Efectos visuales antes de eliminar
					loc.playSound("shadowDie");
					Game1.createRadialDebris(loc, 12, (int)tile.X, (int)tile.Y, 6, false);
				
					dirt.crop = null; // Destruir cultivo
            
					// Spawnear Melon Crab
					// Asegúrate de que "JavCombita.ELE_MelonCrab" coincida con Data/Monsters
					var monster = new StardewValley.Monsters.RockCrab(tile * 64f, "JavCombita.ELE_MelonCrab");
            
					// Configuración crítica para que no desaparezca
					monster.wildernessFarmMonster = true; 
            
					// FIX: Forzar recarga de stats por si acaso el constructor de RockCrab es perezoso
					// En 1.6 suele ser automático, pero esto asegura que tenga la vida correcta del diccionario
					// monster.Health = ... (Si ves que sale con vida base de RockCrab, asigna aquí manualmente)
            
					loc.addCharacter(monster); 
				}
                else if (roll < 0.60) 
                {
                    string cropId = dirt.crop.indexOfHarvest.Value;
                    bool isGiantCapable = (cropId == "190" || cropId == "254" || cropId == "276");

                    if (isGiantCapable)
                    {
                        TryForceGiantCrop(loc, tile, cropId);
                    }
                    else
                    {
                        dirt.crop.growCompletely();
                        Game1.playSound("reward");
                    }
                }
                else 
                {
                    dirt.crop.currentPhase.Value++;
                    Game1.playSound("bubbles");
                }
            }
        }

        private void TryForceGiantCrop(GameLocation loc, Vector2 centerTile, string cropId)
        {
            Vector2 topLeft = centerTile - new Vector2(1, 1);

            // [FIX] Usamos isTileOnMap en lugar de isValidTile
            if (!loc.isTileOnMap(topLeft) || !loc.isTileOnMap(topLeft + new Vector2(2, 2))) return;
			
			if (dirt.crop == null || dirt.crop.dead.Value) return;

            bool areaClear = true;
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    Vector2 current = topLeft + new Vector2(x, y);
                    if (loc.terrainFeatures.TryGetValue(current, out TerrainFeature tf))
                    {
                        if (tf is HoeDirt hd)
                        {
                            hd.crop = null; 
                        }
                        else
                        {
                            areaClear = false;
                        }
                    }
                }
            }

            if (areaClear)
            {
                loc.resourceClumps.Add(new GiantCrop(cropId, topLeft));
                
                Game1.playSound("stumpCrack");
                Game1.createRadialDebris(loc, 12, (int)centerTile.X, (int)centerTile.Y, 12, false);
            }
            else
            {
                if (loc.terrainFeatures.TryGetValue(centerTile, out TerrainFeature tf) && tf is HoeDirt hd && hd.crop != null)
                {
                    hd.crop.growCompletely();
                }
            }
        }

        private void OnRenderedHud(object sender, RenderedHudEventArgs e)
        {
            if (Game1.player.CurrentItem == null || Game1.player.CurrentItem.ItemId != InjectorItemId) return;
            
            Item tool = Game1.player.CurrentItem;
            if (tool.modData.TryGetValue(AmmoCountKey, out string count))
            {
                Vector2 slotPos = new Vector2(
                    (Game1.uiViewport.Width / 2) - (6 * 64) + (Game1.player.CurrentToolIndex * 64),
                    Game1.uiViewport.Height - 64 - 24
                );
                
                Utility.drawTinyDigits(int.Parse(count), e.SpriteBatch, slotPos + new Vector2(40, 40), 3f, 1f, Color.Yellow);
            }
        }
    }
}