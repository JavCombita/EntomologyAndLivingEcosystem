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
        private const string SoilDataKey = "JavCombita.ELE/SoilData";
        
        // ID que definiremos en Content Patcher
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
                    {
                        ProcessCropNutrients(location, pair.Key, dirt);
                    }
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

            // --- Lógica de Monocultivo ---
            // Penalización: +10% de consumo por cada cultivo idéntico vecino
            int neighbors = GetMonocultureScore(location, tile, crop);
            float competitionFactor = 1.0f + (neighbors * 0.1f);

            float nDrain = 1.5f * multiplier * stageFactor * competitionFactor;
            float pDrain = 1.0f * multiplier * stageFactor * competitionFactor;
            float kDrain = 0.8f * multiplier * stageFactor * competitionFactor;

            soil.Nitrogen -= nDrain;
            soil.Phosphorus -= pDrain;
            soil.Potassium -= kDrain;

            soil.Nitrogen = Math.Clamp(soil.Nitrogen, 0f, 100f);
            soil.Phosphorus = Math.Clamp(soil.Phosphorus, 0f, 100f);
            soil.Potassium = Math.Clamp(soil.Potassium, 0f, 100f);

            SaveSoilDataAt(location, tile, soil);
        }

        public void UpdatePests()
        {
            if (Game1.currentLocation == null || (!Game1.currentLocation.IsFarm && !Game1.currentLocation.Name.Contains("Greenhouse"))) 
                return;

            if (Game1.random.NextDouble() < 0.01) // 1% chance per second check
            {
                SpawnPestNearPlayer(Game1.currentLocation);
            }
        }

        private void SpawnPestNearPlayer(GameLocation location)
        {
            // Fix para Stardew 1.6 (.Tile en vez de getTileLocation)
            Vector2 playerPos = Game1.player.Tile;
            
            for (int x = -5; x <= 5; x++)
            {
                for (int y = -5; y <= 5; y++)
                {
                    Vector2 targetTile = new Vector2(playerPos.X + x, playerPos.Y + y);
                    
                    if (location.terrainFeatures.TryGetValue(targetTile, out TerrainFeature tf) && tf is HoeDirt dirt && dirt.crop != null)
                    {
                        // --- DEFENSA BIOLÓGICA ---
                        if (IsProtectedByLadybugs(location, targetTile)) continue;

                        SoilData soil = GetSoilDataAt(location, targetTile);

                        // --- RIESGO DE MONOCULTIVO ---
                        // Si hay muchos vecinos, el cultivo se enferma más fácil (Umbral de K sube)
                        int neighbors = GetMonocultureScore(location, targetTile, dirt.crop);
                        float dangerThreshold = 30f + (neighbors * 5f); 
                        // Ej: Sin vecinos, atacan si K < 30. Con 8 vecinos, atacan si K < 70.

                        if (soil.Potassium < dangerThreshold)
                        {
                            Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.invasion"), 3));
                            
                            location.temporarySprites.Add(new TemporaryAnimatedSprite(
                                textureName: "LooseSprites\\Cursors",
                                sourceRect: new Rectangle(381, 1342, 10, 10),
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
                            return; 
                        }
                    }
                }
            }
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
                        // Comparamos el índice de cosecha para saber si son la misma planta
                        if (dirt.crop.indexOfHarvest.Value == centerCrop.indexOfHarvest.Value)
                        {
                            score++;
                        }
                    }
                }
            }
            return score;
        }

        private bool IsProtectedByLadybugs(GameLocation location, Vector2 targetTile)
        {
            int radius = 6;
            // Optimización: Escanear área pequeña en busca del objeto protector
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    Vector2 checkTile = new Vector2(targetTile.X + x, targetTile.Y + y);
                    if (location.Objects.TryGetValue(checkTile, out StardewValley.Object obj))
                    {
                        if (obj.ItemId == LadybugShelterId) return true;
                    }
                }
            }
            return false;
        }

        public SoilData GetSoilDataAt(GameLocation location, Vector2 tile)
        {
            string key = $"{SoilDataKey}_{tile.X}_{tile.Y}";
            if (location.modData.TryGetValue(key, out string rawData))
                return SoilData.FromString(rawData);
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
            float heavyBoost = 50f;
            float lightBoost = 10f;

            switch (fertilizerId)
            {
                case "465": case "466": case "HyperSpeedGro":
                    data.Nitrogen += heavyBoost; data.Phosphorus += lightBoost; data.Potassium += lightBoost; break;
                case "368":
                    data.Nitrogen += lightBoost; data.Phosphorus += heavyBoost; data.Potassium += lightBoost; break;
                case "369": case "919":
                    data.Nitrogen += lightBoost; data.Phosphorus += lightBoost; data.Potassium += heavyBoost; break;
                default:
                    data.Nitrogen += 20f; data.Phosphorus += 20f; data.Potassium += 20f; break;
            }
            data.Nitrogen = Math.Min(data.Nitrogen, 100f);
            data.Phosphorus = Math.Min(data.Phosphorus, 100f);
            data.Potassium = Math.Min(data.Potassium, 100f);
            SaveSoilDataAt(location, tile, data);
            
            Game1.createRadialDebris(location, 12, (int)tile.X, (int)tile.Y, 6, false);
        }
    }
}