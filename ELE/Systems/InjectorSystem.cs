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
        private const string InjectorItemId = "JavCombita.ELE_Alchemical_Injector";
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
            // Usamos una semilla basada en posición y tiempo para pseudo-aleatoriedad
            Random methodRng = new Random((int)tile.X * 1000 + (int)tile.Y + (int)Game1.uniqueIDForThisGame + (int)Game1.stats.DaysPlayed);
            
            if (mutagenId.Contains("Growth"))
            {
                // --- GROWTH MUTAGEN (Seguro) ---
                if (dirt.crop.currentPhase.Value < dirt.crop.phaseDays.Count - 1)
                {
                    dirt.crop.currentPhase.Value++;
                    Game1.playSound("wand");
                    Mod.Ecosystem.RestoreNutrients(loc, tile, "(O)JavCombita.ELE_Fertilizer_Omni"); 
                }
            }
            else if (mutagenId.Contains("Chaos")) 
            {
                // --- CHAOS MUTAGEN (Riesgo/Recompensa) ---
                double roll = methodRng.NextDouble();
                
                if (roll < 0.30) 
                {
                    // 30% FALLA: Monstruo (Melon Crab)
                    dirt.crop = null; 
                    Game1.playSound("shadowDie");
                    
                    var monster = new StardewValley.Monsters.RockCrab(tile * 64f, "JavCombita.ELE_MelonCrab");
                    monster.wildernessFarmMonster = true; 
                    loc.addCharacter(monster); 
                    
                    Game1.createRadialDebris(loc, 12, (int)tile.X, (int)tile.Y, 6, false);
                }
                else if (roll < 0.60) 
                {
                    // 30% CRÍTICO: Gigante Instantáneo
                    string cropId = dirt.crop.indexOfHarvest.Value;
                    
                    // IDs válidos para gigantes (Cauliflower, Melon, Pumpkin, Powdermelon)
                    // Nota: Comparamos strings. Powdermelon ID suele ser "(O)Powdermelon" o IDs numéricos nuevos en 1.6
                    bool isGiantCapable = (cropId == "190" || cropId == "254" || cropId == "276");

                    if (isGiantCapable)
                    {
                        TryForceGiantCrop(loc, tile, cropId);
                    }
                    else
                    {
                        // Si no tiene versión gigante, simplemente crece al máximo instantáneamente
                        dirt.crop.growCompletely();
                        Game1.playSound("reward");
                    }
                }
                else 
                {
                    // 40% NORMAL: Crece 1 etapa
                    dirt.crop.currentPhase.Value++;
                    Game1.playSound("bubbles");
                }
            }
        }

        private void TryForceGiantCrop(GameLocation loc, Vector2 centerTile, string cropId)
        {
            // 1. Calcular el área de 3x3 centrada en el impacto
            // El "TopLeft" del gigante será (X-1, Y-1) respecto al centro
            Vector2 topLeft = centerTile - new Vector2(1, 1);

            // 2. Verificar límites del mapa
            if (!loc.isValidTile(topLeft) || !loc.isValidTile(topLeft + new Vector2(2, 2))) return;

            // 3. LIMPIEZA: Eliminar cultivos pequeños en el área de 3x3
            // Esto es necesario para que el gigante no aparezca "encima" de plantas existentes
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
                            hd.crop = null; // Matamos el cultivo pequeño
                            // Opcional: Si quieres quitar la tierra arada también, usa loc.terrainFeatures.Remove(current);
                        }
                        else
                        {
                            // Si hay un árbol o algo que no es tierra arada en el medio del 3x3, abortamos para no romper nada
                            areaClear = false;
                        }
                    }
                }
            }

            if (areaClear)
            {
                // 4. INVOCAR AL GIGANTE
                // En 1.6, GiantCrop toma el ID del producto (ej: "190") y la posición TopLeft
                loc.resourceClumps.Add(new GiantCrop(cropId, topLeft));
                
                Game1.playSound("stumpCrack");
                Game1.createRadialDebris(loc, 12, (int)centerTile.X, (int)centerTile.Y, 12, false);
            }
            else
            {
                // Si el área estaba obstruida, hacemos fallback a crecimiento normal
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