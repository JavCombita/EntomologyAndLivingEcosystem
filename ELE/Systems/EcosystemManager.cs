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
        private const string PestCountKey = "JavCombita.ELE/PestCount";
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
                var terrainFeatures = location.terrainFeatures.Pairs.ToList();
                foreach (var pair in terrainFeatures)
                {
                    if (pair.Value is HoeDirt dirt && dirt.crop != null)
                        ProcessCropNutrients(location, pair.Key, dirt);
                }
            }
            this.Monitor.Log("Daily soil analysis completed.", LogLevel.Trace);
        }

        private void ProcessCropNutrients(GameLocation location, Vector2 tile, HoeDirt dirt)
        {
            Crop crop = dirt.crop;
            SoilData soil = GetSoilDataAt(location, tile);
            float multiplier = this.Mod.Config.NutrientDepletionMultiplier;
            float stageFactor = (crop.currentPhase.Value + 1) * 0.5f;
            int neighbors = GetMonocultureScore(location, tile, crop);
            float competitionFactor = 1.0f + (neighbors * 0.1f);
            float nDrain = 1.5f * multiplier * stageFactor * competitionFactor;
            float pDrain = 1.0f * multiplier * stageFactor * competitionFactor;
            float kDrain = 0.8f * multiplier * stageFactor * competitionFactor;
            soil.Nitrogen -= nDrain; soil.Phosphorus -= pDrain; soil.Potassium -= kDrain;
            soil.Nitrogen = Math.Clamp(soil.Nitrogen, 0f, 100f);
            soil.Phosphorus = Math.Clamp(soil.Phosphorus, 0f, 100f);
            soil.Potassium = Math.Clamp(soil.Potassium, 0f, 100f);
            SaveSoilDataAt(location, tile, soil);
        }

        public void UpdatePests()
        {
            if (Game1.currentLocation == null || (!Game1.currentLocation.IsFarm && !Game1.currentLocation.Name.Contains("Greenhouse"))) return;
            if (Game1.eventUp || Game1.isFestival()) return;
            
            if (Game1.random.NextDouble() < 0.05) SpawnPestNearPlayer(Game1.currentLocation, isForced: false);
        }

        public void ForcePestInvasion()
        {
            if (Game1.currentLocation == null) return;
            this.Monitor.Log("🐜 DEBUG: Forcing pest invasion (Ignoring nutrients)...", LogLevel.Warn);
            SpawnPestNearPlayer(Game1.currentLocation, isForced: true);
        }

        private void SpawnPestNearPlayer(GameLocation location, bool isForced = false)
        {
            Vector2 playerPos = Game1.player.Tile;
            bool cropFound = false;
            
            for (int x = -5; x <= 5; x++)
            {
                for (int y = -5; y <= 5; y++)
                {
                    Vector2 targetTile = new Vector2(playerPos.X + x, playerPos.Y + y);
                    
                    if (location.terrainFeatures.TryGetValue(targetTile, out TerrainFeature tf) && tf is HoeDirt dirt && dirt.crop != null)
                    {
                        cropFound = true;

                        // 1. CHEQUEO DE ACTIVIDAD
                        if (IsPestActiveAt(location, targetTile)) 
                        {
                            if (isForced) this.Monitor.Log($"🐜 Debug: Pest already active at {targetTile}", LogLevel.Trace);
                            continue;
                        }

                        // 2. SHELTER CHECK
                        StardewValley.Object protector = GetProtectorShelter(location, targetTile);
                        if (protector != null)
                        {
                            // --- RESTAURADO: LÓGICA DE CONTEO ---
                            int currentCount = 0;
                            if (protector.modData.TryGetValue(PestCountKey, out string countStr))
                            {
                                int.TryParse(countStr, out currentCount);
                            }
                            currentCount++;
                            protector.modData[PestCountKey] = currentCount.ToString();
                            // ------------------------------------

                            // Efecto visual de bloqueo
                            location.temporarySprites.Add(new TemporaryAnimatedSprite(5, targetTile * 64f, Color.Cyan) { scale = 0.5f });

                            if (isForced)
                            {
                                this.Monitor.Log($"🛡️ Forced Invasion BLOCKED by Shelter at {targetTile}. Count: {currentCount}", LogLevel.Warn);
                            }
                            return; // Bloqueado
                        }

                        // 3. ATAQUE
                        SoilData soil = GetSoilDataAt(location, targetTile);
                        float dangerThreshold = 50f; 

                        if (soil.Potassium < dangerThreshold || isForced)
                        {
                            if (ModEntry.PestTexture != null)
                            {
                                location.temporarySprites.Add(new VerticalPestSprite(ModEntry.PestTexture, targetTile * 64f));
                            }
                            else
                            {
                                location.temporarySprites.Add(new TemporaryAnimatedSprite(
                                    textureName: "LooseSprites\\Cursors",
                                    sourceRect: new Rectangle(381, 1342, 10, 10),
                                    position: targetTile * 64f,
                                    flipped: false, alphaFade: 0f, color: Color.White)
                                {
                                    motion = new Vector2((float)Game1.random.NextDouble() - 0.5f, -1f),
                                    scale = 4f, 
                                    interval = 100f, 
                                    totalNumberOfLoops = 20,
                                    animationLength = 4
                                });
                            }
                            
                            if (isForced)
                                this.Monitor.Log($"✅ DEBUG SUCCESS: Pest spawned at {targetTile} (Forced)", LogLevel.Alert);
                            else
                                this.Monitor.Log($"🐜 PEST SPAWNED at {targetTile} (Low K: {soil.Potassium})", LogLevel.Trace);
                            
                            return; 
                        }
                    }
                }
            }
            
            if (!cropFound && isForced)
                this.Monitor.Log("❌ DEBUG FAILED: No crops found near player (5 tile radius). Stand closer to a crop!", LogLevel.Error);
        }

        private bool IsPestActiveAt(GameLocation location, Vector2 tile)
        {
            Vector2 positionToCheck = tile * 64f;
            foreach (var sprite in location.temporarySprites)
            {
                if (sprite is VerticalPestSprite && Vector2.Distance(sprite.position, positionToCheck) < 10f) return true;
                if (sprite.textureName == "LooseSprites\\Cursors" && sprite.sourceRect.X == 381 && Vector2.Distance(sprite.position, positionToCheck) < 10f) return true;
            }
            return false;
        }

        private int GetMonocultureScore(GameLocation location, Vector2 centerTile, Crop centerCrop)
        {
            int score = 0;
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue; 
                    Vector2 neighborTile = new Vector2(centerTile.X + x, centerTile.Y + y);
                    if (location.terrainFeatures.TryGetValue(neighborTile, out TerrainFeature tf) && tf is HoeDirt dirt && dirt.crop != null)
                    {
                        if (dirt.crop.indexOfHarvest.Value == centerCrop.indexOfHarvest.Value) score++;
                    }
                }
            }
            return score;
        }

        private StardewValley.Object GetProtectorShelter(GameLocation location, Vector2 targetTile)
        {
            int radius = 6;
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    Vector2 checkTile = new Vector2(targetTile.X + x, targetTile.Y + y);
                    if (location.Objects.TryGetValue(checkTile, out StardewValley.Object obj))
                    {
                        if (obj.ItemId == LadybugShelterId) return obj;
                    }
                }
            }
            return null;
        }

        public SoilData GetSoilDataAt(GameLocation location, Vector2 tile)
        {
            string key = $"{SoilDataKey}_{tile.X}_{tile.Y}";
            if (location.modData.TryGetValue(key, out string rawData)) return SoilData.FromString(rawData);
            return new SoilData(); 
        }

        public void SaveSoilDataAt(GameLocation location, Vector2 tile, SoilData data)
        {
            string key = $"{SoilDataKey}_{tile.X}_{tile.Y}";
            location.modData[key] = data.ToString();
        }

        public void RestoreNutrients(GameLocation location, Vector2 tile, string fertilizerId)
        {
            SoilData data = GetSoilDataAt(location, tile);
            float heavyBoost = 50f; float lightBoost = 10f;
            switch (fertilizerId)
            {
                case "465": case "466": case "HyperSpeedGro": data.Nitrogen += heavyBoost; data.Phosphorus += lightBoost; data.Potassium += lightBoost; break;
                case "368": data.Nitrogen += lightBoost; data.Phosphorus += heavyBoost; data.Potassium += lightBoost; break;
                case "369": case "919": data.Nitrogen += lightBoost; data.Phosphorus += lightBoost; data.Potassium += heavyBoost; break;
                default: data.Nitrogen += 20f; data.Phosphorus += 20f; data.Potassium += 20f; break;
            }
            data.Nitrogen = Math.Min(data.Nitrogen, 100f); data.Phosphorus = Math.Min(data.Phosphorus, 100f); data.Potassium = Math.Min(data.Potassium, 100f);
            SaveSoilDataAt(location, tile, data);
            Game1.createRadialDebris(location, 12, (int)tile.X, (int)tile.Y, 6, false);
        }
    }

    public class VerticalPestSprite : TemporaryAnimatedSprite
    {
        public VerticalPestSprite(Texture2D texture, Vector2 position) : base()
        {
            this.texture = texture;
            this.position = position;
            
            this.sourceRect = new Rectangle(0, 0, 16, 16);
            this.sourceRectStartingPos = new Vector2(0f, 0f); 
            
            this.interval = 100f;          
            this.animationLength = 4;      
            this.totalNumberOfLoops = 15;  
            
            this.scale = 4f;
            this.layerDepth = 1f;
            this.motion = new Vector2((float)Game1.random.NextDouble() - 0.5f, -0.5f);
        }

        public override bool update(GameTime time)
        {
            bool result = base.update(time);

            // Forzamos la lectura vertical
            this.sourceRect.X = 0; 
            this.sourceRect.Y = this.currentParentTileIndex * 16;

            return result;
        }
    }
}
