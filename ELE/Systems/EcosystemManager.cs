using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using ELE.Core.Models;

namespace ELE.Core.Systems
{
    public class EcosystemManager
    {
        private readonly ModEntry Mod;
        private readonly IMonitor Monitor;
        private const string SoilDataKey = "JavCombita.ELE/SoilData";
        private const string LadybugShelterId = "JavCombita.ELE_LadybugShelter";

        public EcosystemManager(ModEntry mod)
        {
            this.Mod = mod;
            this.Monitor = mod.Monitor;
        }

        public void CalculateDailyNutrients()
        {
            foreach (GameLocation location in Game1.locations)
            {
                if (!location.IsFarm && !location.Name.Contains("Greenhouse")) continue;
                
                // Iterar sobre una copia para poder modificar colecciones si fuera necesario
                var terrainFeatures = location.terrainFeatures.Pairs.ToList();
                foreach (var pair in terrainFeatures)
                {
                    if (pair.Value is HoeDirt dirt && dirt.crop != null)
                        ProcessCropNutrients(location, pair.Key, dirt);
                }
            }
            this.Monitor.Log("Daily soil analysis completed.", LogLevel.Trace);
        }

        // Método de Debug
        public void ForcePestAttack()
        {
            Monitor.Log("Forcing Pest Attack on nearby crops...", LogLevel.Alert);
            GameLocation loc = Game1.currentLocation;
            Vector2 playerTile = Game1.player.Tile;
            
            // Buscar cultivos cercanos al jugador
            int attempts = 0;
            foreach (var pair in loc.terrainFeatures.Pairs)
            {
                if (pair.Value is HoeDirt dirt && dirt.crop != null && Vector2.Distance(pair.Key, playerTile) < 5)
                {
                    SpawnPest(loc, pair.Key, dirt, true); // True = Force
                    attempts++;
                    if (attempts >= 3) break;
                }
            }
        }

        private void ProcessCropNutrients(GameLocation location, Vector2 tile, HoeDirt dirt)
        {
            if (!this.Mod.Config.EnableNutrientCycle) return;

            SoilData data = GetSoilDataAt(location, tile);
            
            // Lógica de Monocultivo: Verificar vecinos
            int similarNeighbors = CountSimilarNeighbors(location, tile, dirt.crop.indexOfHarvest.Value);
            float monoculturePenalty = 1.0f + (similarNeighbors * 0.1f); // 10% extra drain per neighbor

            float consumption = 2.0f * this.Mod.Config.NutrientDepletionMultiplier * monoculturePenalty;
            
            // Drenaje según fase
            if (dirt.crop.currentPhase.Value < dirt.crop.phaseDays.Count - 1)
            {
                data.Nitrogen -= consumption * 1.5f;     
                data.Phosphorus -= consumption * 0.5f;
            }
            else
            {
                data.Phosphorus -= consumption * 1.5f;   
                data.Potassium -= consumption * 1.2f;
            }

            // Normalizar límites
            data.Nitrogen = Math.Max(0, data.Nitrogen);
            data.Phosphorus = Math.Max(0, data.Phosphorus);
            data.Potassium = Math.Max(0, data.Potassium);

            SaveSoilDataAt(location, tile, data);

            // Trigger de Plagas: Si Potasio (K) < 50
            if (data.Potassium < 50)
            {
                TrySpawnPests(location, tile, dirt);
            }
        }

        private int CountSimilarNeighbors(GameLocation location, Vector2 tile, string cropIndex)
        {
            int count = 0;
            Vector2[] offsets = { new(1,0), new(-1,0), new(0,1), new(0,-1) };
            foreach(var off in offsets)
            {
                if(location.terrainFeatures.TryGetValue(tile + off, out TerrainFeature tf) && tf is HoeDirt neighbor && neighbor.crop != null)
                {
                    if (neighbor.crop.indexOfHarvest.Value == cropIndex) count++;
                }
            }
            return count;
        }

        private void TrySpawnPests(GameLocation location, Vector2 tile, HoeDirt dirt, bool forced = false)
        {
            if (!this.Mod.Config.EnablePestInvasions && !forced) return;
            
            // Chequeo de Shelter
            if (IsProtectedByShelter(location, tile)) 
            {
                // Efecto visual de "Bloqueo" (Cian explosion)
                Game1.multiplayer.broadcastSprites(location, new TemporaryAnimatedSprite(362, 30f, 1, 1, tile * 64f, false, false){ color = Color.Cyan, scale = 4f });
                return;
            }

            // Probabilidad de aparición (5%)
            if (forced || Game1.random.NextDouble() < 0.05)
            {
                SpawnPest(location, tile, dirt, forced);
            }
        }

        private void SpawnPest(GameLocation location, Vector2 tile, HoeDirt dirt, bool forced)
        {
            // Visual FX (Nube de insectos persistente como animación temporal)
            if (ModEntry.PestTexture != null)
            {
                 location.temporarySprites.Add(new VerticalPestSprite(ModEntry.PestTexture, tile * 64f));
            }

            // MECÁNICA: Muerte súbita vs Drenaje
            // 30% chance de muerte (o 100% si es forzado para probar)
            if (forced || Game1.random.NextDouble() < 0.30) 
            {
                dirt.crop = null; // Muerte
                Game1.playSound("cut");
                Game1.multiplayer.broadcastSprites(location, new TemporaryAnimatedSprite(362, 30f, 1, 1, tile * 64f, false, false){ color = Color.DarkGreen }); // Hojas volando
                
                // Mensaje solo una vez por día para no spammear
                if (!forced) Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.pest_damage"), 3));
            }
            else
            {
                // Drenaje masivo de nutrientes (La plaga come)
                SoilData data = GetSoilDataAt(location, tile);
                data.Nitrogen = Math.Max(0, data.Nitrogen - 20);
                data.Phosphorus = Math.Max(0, data.Phosphorus - 20);
                SaveSoilDataAt(location, tile, data);
            }
        }

        private bool IsProtectedByShelter(GameLocation location, Vector2 tile)
        {
            foreach (var pair in location.objects.Pairs)
            {
                if (pair.Value.ItemId == LadybugShelterId)
                {
                    if (Vector2.Distance(tile, pair.Key) <= 6) return true;
                }
            }
            return false;
        }

        // --- Data Persistence ---
        public SoilData GetSoilDataAt(GameLocation location, Vector2 tile)
        {
            if (location.modData.TryGetValue($"{SoilDataKey}/{tile.X},{tile.Y}", out string dataStr))
            {
                return SoilData.FromString(dataStr);
            }
            return new SoilData(); 
        }

        public void SaveSoilDataAt(GameLocation location, Vector2 tile, SoilData data)
        {
            location.modData[$"{SoilDataKey}/{tile.X},{tile.Y}"] = data.ToString();
        }

        public void RestoreNutrients(GameLocation location, Vector2 tile, string fertilizerId)
        {
            SoilData data = GetSoilDataAt(location, tile);
            
            // Soporte para IDs nuevos (Strings en 1.6)
            switch (fertilizerId)
            {
                case "(O)368": case "368": // Basic
                    data.Nitrogen += 30f; break;
                case "(O)369": case "369": // Quality
                    data.Nitrogen += 60f; data.Phosphorus += 30f; break;
                // --- NUEVOS FERTILIZANTES DE ELE ---
                case "JavCombita.ELE_Fertilizer_N": 
                    data.Nitrogen += 80f; break;
                case "JavCombita.ELE_Fertilizer_P": 
                    data.Phosphorus += 80f; break;
                case "JavCombita.ELE_Fertilizer_K": 
                    data.Potassium += 80f; break;
                case "JavCombita.ELE_Fertilizer_Omni": 
                    data.Nitrogen = 100f; data.Phosphorus = 100f; data.Potassium = 100f; break;
                    
                default: data.Nitrogen += 15f; data.Phosphorus += 15f; data.Potassium += 15f; break;
            }
            
            // Clamp 100
            data.Nitrogen = Math.Min(data.Nitrogen, 100f); 
            data.Phosphorus = Math.Min(data.Phosphorus, 100f); 
            data.Potassium = Math.Min(data.Potassium, 100f);
            
            SaveSoilDataAt(location, tile, data);
            Game1.createRadialDebris(location, 12, (int)tile.X, (int)tile.Y, 6, false);
        }
    }
    
    // Clase auxiliar para la animación (igual que tenías, pero la incluyo para que compile)
    public class VerticalPestSprite : TemporaryAnimatedSprite
    {
        public VerticalPestSprite(Texture2D texture, Vector2 position) : base()
        {
            this.texture = texture;
            this.position = position;
            this.sourceRect = new Rectangle(0, 0, 16, 16);
            this.interval = 100f;          
            this.animationLength = 4;      
            this.totalNumberOfLoops = 10;  
            this.scale = 4f;
            this.layerDepth = 1f;
        }
    }
}