using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
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
        
        // Key for storing data inside the save file securely
        private const string SoilDataKey = "JavCombita.ELE/SoilData";

        public EcosystemManager(ModEntry mod)
        {
            this.Mod = mod;
            this.Monitor = mod.Monitor;
        }

        /// <summary>
        /// Analyzes the farm, drains nutrients based on crops, and updates persistence.
        /// Runs once per in-game day (DayStarted).
        /// </summary>
        public void CalculateDailyNutrients()
        {
            // Iterate over all locations to support Farm, Greenhouse, and Island
            foreach (GameLocation location in Game1.locations)
            {
                if (!location.IsFarm && !location.Name.Contains("Greenhouse")) continue;

                // Use ToList() to avoid "Collection was modified" errors if we modify the collection
                var terrainFeatures = location.terrainFeatures.Pairs.ToList();

                foreach (var pair in terrainFeatures)
                {
                    if (pair.Value is HoeDirt dirt && dirt.crop != null)
                    {
                        ProcessCropNutrients(location, pair.Key, dirt);
                    }
                }
            }
            
            this.Monitor.Log("Daily soil analysis completed.", LogLevel.Trace);
        }

        /// <summary>
        /// Drains nutrients for a specific tile based on the crop growing there.
        /// </summary>
        private void ProcessCropNutrients(GameLocation location, Vector2 tile, HoeDirt dirt)
        {
            Crop crop = dirt.crop;
            
            // 1. Fetch current data
            SoilData soil = GetSoilDataAt(location, tile);

            // 2. Calculate Drain Factors (Configurable Multiplier)
            float multiplier = this.Mod.Config.NutrientDepletionMultiplier;
            
            // Logic: Crops in later stages consume more nutrients
            float stageFactor = (crop.currentPhase.Value + 1) * 0.5f;

            // Simple drain logic (can be expanded for specific crop types later)
            float nDrain = 1.5f * multiplier * stageFactor; // Nitrogen (Leaf growth)
            float pDrain = 1.0f * multiplier * stageFactor; // Phosphorus (Root/Fruit)
            float kDrain = 0.8f * multiplier * stageFactor; // Potassium (General health)

            // 3. Apply Drain
            soil.Nitrogen -= nDrain;
            soil.Phosphorus -= pDrain;
            soil.Potassium -= kDrain;

            // 4. Clamp values (Cannot go below 0 or above 100)
            soil.Nitrogen = Math.Clamp(soil.Nitrogen, 0f, 100f);
            soil.Phosphorus = Math.Clamp(soil.Phosphorus, 0f, 100f);
            soil.Potassium = Math.Clamp(soil.Potassium, 0f, 100f);

            // 5. Consequence: Poor soil affects crop?
            // If Nitrogen is critical (< 10%), chance to stop growing or attract pests
            if (soil.Nitrogen < 10f && Game1.random.NextDouble() < 0.3)
            {
                 // Visual indicator of poor soil (brown particles)
                 // We don't kill the crop to be player-friendly, but we mark it visually
                 // location.temporarySprites.Add(...) could go here
            }

            // 6. Save Data back to the Tile
            SaveSoilDataAt(location, tile, soil);
        }

        /// <summary>
        /// Runs periodically (every second) to simulate pest invasions.
        /// </summary>
        public void UpdatePests()
        {
            // Only simulate if player is on a farm-like map
            if (Game1.currentLocation == null || (!Game1.currentLocation.IsFarm && !Game1.currentLocation.Name.Contains("Greenhouse"))) 
                return;

            // Very low chance per second to trigger a "Mini Invasion" nearby
            if (Game1.random.NextDouble() < 0.01) // 1% chance per second
            {
                SpawnPestNearPlayer(Game1.currentLocation);
            }
        }

        private void SpawnPestNearPlayer(GameLocation location)
        {
            Vector2 playerPos = Game1.player.getTileLocation();
            
            // Find a valid crop tile near the player
            for (int x = -5; x <= 5; x++)
            {
                for (int y = -5; y <= 5; y++)
                {
                    Vector2 targetTile = new Vector2(playerPos.X + x, playerPos.Y + y);
                    
                    if (location.terrainFeatures.TryGetValue(targetTile, out TerrainFeature tf) && tf is HoeDirt dirt && dirt.crop != null)
                    {
                        // Found a crop! Check soil health
                        SoilData soil = GetSoilDataAt(location, targetTile);

                        // Pests are attracted to WEAK soil (Low Potassium/K)
                        if (soil.Potassium < 30f)
                        {
                            // Trigger visual effect (Emote or Particle)
                            Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.invasion"), 3));
                            
                            // Visual: Fly animation (using standard game sprites for now, ID 12 is a fly in some sheets, or use a generic one)
                            // Using a temporary sprite to simulate a bug eating the crop
                            location.temporarySprites.Add(new TemporaryAnimatedSprite(
                                textureName: "LooseSprites\\Cursors",
                                sourceRect: new Rectangle(381, 1342, 10, 10), // Example coordinate for a bug-like speck
                                position: targetTile * 64f,
                                flipped: false,
                                alphaFade: 0f,
                                color: Color.White)
                            {
                                motion = new Vector2((float)Game1.random.NextDouble() - 0.5f, -1f),
                                scale = 4f,
                                interval = 50f,
                                totalNumberOfLoops = 5,
                                animationLength = 4
                            });
                            
                            // Break loop to spawn only one pest group at a time
                            return;
                        }
                    }
                }
            }
        }

        // --- PUBLIC API FOR RENDERER & DATA ---

        /// <summary>
        /// Retrieves the NPK data for a specific tile.
        /// Reads from modData or returns default (100,100,100).
        /// </summary>
        public SoilData GetSoilDataAt(GameLocation location, Vector2 tile)
        {
            string key = $"{SoilDataKey}_{tile.X}_{tile.Y}";
            
            if (location.modData.TryGetValue(key, out string rawData))
            {
                return SoilData.FromString(rawData);
            }

            // Default: Perfect soil
            return new SoilData(); 
        }

        /// <summary>
        /// Saves the NPK data to the tile's modData.
        /// </summary>
        public void SaveSoilDataAt(GameLocation location, Vector2 tile, SoilData data)
        {
            string key = $"{SoilDataKey}_{tile.X}_{tile.Y}";
            location.modData[key] = data.ToString();
        }
        
        /// <summary>
        /// Use this when applying fertilizer to restore nutrients manually.
        /// </summary>
        public void RestoreNutrients(GameLocation location, Vector2 tile, float amount)
        {
             SoilData data = GetSoilDataAt(location, tile);
             data.Nitrogen += amount;
             data.Phosphorus += amount;
             data.Potassium += amount;
             
             // Clamp max
             data.Nitrogen = Math.Min(data.Nitrogen, 100f);
             data.Phosphorus = Math.Min(data.Phosphorus, 100f);
             data.Potassium = Math.Min(data.Potassium, 100f);
             
             SaveSoilDataAt(location, tile, data);
        }
    }
}