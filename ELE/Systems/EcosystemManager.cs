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
        private const string PestCountKey = "JavCombita.ELE/PestCount";
        
        // ID del Shelter
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

            // Lógica de Monocultivo
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

        // --- SISTEMA DE PLAGAS ---

        public void UpdatePests()
        {
            if (Game1.currentLocation == null || (!Game1.currentLocation.IsFarm && !Game1.currentLocation.Name.Contains("Greenhouse"))) 
                return;

            // --- NUEVO: CHEQUEO DE EVENTOS ---
            // Si hay cinemática (evento) o es día de Festival, las plagas no atacan.
            if (Game1.eventUp || Game1.isFestival()) return;
            // ---------------------------------

            if (Game1.random.NextDouble() < 0.01) // 1% chance per second
            {
                SpawnPestNearPlayer(Game1.currentLocation);
            }
        }

        /// <summary>
        /// NUEVO: Comando público para forzar plagas (Debug).
        /// Llamado desde 'ModEntry.cs' por el comando 'ele_pest'.
        /// </summary>
        public void ForcePestInvasion()
        {
            if (Game1.currentLocation == null) return;
            
            this.Monitor.Log("🐜 DEBUG: Forcing pest invasion now...", LogLevel.Warn);
            
            // Llamamos a la lógica interna de spawneo.
            SpawnPestNearPlayer(Game1.currentLocation);
        }

        private void SpawnPestNearPlayer(GameLocation location)
        {
            Vector2 playerPos = Game1.player.Tile;
            bool pestAttempted = false; // Debug flag

            // Intentamos encontrar un cultivo válido cerca del jugador
            for (int x = -5; x <= 5; x++)
            {
                for (int y = -5; y <= 5; y++)
                {
                    Vector2 targetTile = new Vector2(playerPos.X + x, playerPos.Y + y);
                    
                    if (location.terrainFeatures.TryGetValue(targetTile, out TerrainFeature tf) && tf is HoeDirt dirt && dirt.crop != null)
                    {
                        pestAttempted = true;

                        // 1. EFECTO VISUAL "ESCANEO" (Mosca pequeña aleatoria)
                        // Para que veas que el sistema está activo aunque no ataque
                        if (Game1.random.NextDouble() < 0.3) 
                        {
                            location.temporarySprites.Add(new TemporaryAnimatedSprite(
                                "LooseSprites\\Cursors", new Rectangle(346, 400, 8, 8), 
                                targetTile * 64f + new Vector2(Game1.random.Next(10, 50), Game1.random.Next(-50, 0)), 
                                false, 0f, Color.Gray)
                            {
                                scale = 2f, animationLength = 4, interval = 100f,
                                motion = new Vector2((float)Game1.random.NextDouble() - 0.5f, -1f)
                            });
                        }

                        // 2. DEFENSA BIOLÓGICA (SHELTER)
                        StardewValley.Object protector = GetProtectorShelter(location, targetTile);

                        if (protector != null)
                        {
                            // Aumentar contador en el Shelter
                            int currentCount = 0;
                            if (protector.modData.TryGetValue(PestCountKey, out string countStr))
                            {
                                int.TryParse(countStr, out currentCount);
                            }
                            currentCount++;
                            protector.modData[PestCountKey] = currentCount.ToString();

                            // Efecto de BLOQUEO (Explosión cian)
                            location.temporarySprites.Add(new TemporaryAnimatedSprite(
                                5, targetTile * 64f, Color.Cyan) { scale = 0.5f });

                            this.Monitor.Log($"🛡️ Pest blocked by Shelter at {protector.TileLocation}. Total blocked: {currentCount}", LogLevel.Info);
                            
                            return; // Bloqueado, salimos.
                        }

                        // 3. ATAQUE REAL (Si no hay shelter)
                        SoilData soil = GetSoilDataAt(location, targetTile);
                        
                        // Umbral de ataque (puedes ajustarlo)
                        // Si el potasio es bajo, la planta es débil contra plagas.
                        float dangerThreshold = 50f; 

                        if (soil.Potassium < dangerThreshold)
                        {
                            Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.invasion"), 3));
                            
                            // --- LÓGICA DE ANIMACIÓN (CUSTOM vs VANILLA) ---
                            if (ModEntry.PestTexture != null)
                            {
                                // Usamos tu textura personalizada (asumiendo frames de 16x16)
                                location.temporarySprites.Add(new TemporaryAnimatedSprite(
                                    textureName: null, 
                                    sourceRect: new Rectangle(0, 0, 16, 16), 
                                    position: targetTile * 64f,
                                    flipped: false, 
                                    alphaFade: 0f, 
                                    color: Color.White)
                                {
                                    texture = ModEntry.PestTexture,
                                    animationLength = 4, // Ajusta si tienes más frames
                                    interval = 100f,
                                    totalNumberOfLoops = 5,
                                    scale = 4f,
                                    motion = new Vector2((float)Game1.random.NextDouble() - 0.5f, -1f)
                                });
                            }
                            else
                            {
                                // Fallback a Vanilla (Insectos negros)
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
                            }
                            // ---------------------------------------------
                            
                            this.Monitor.Log($"🐜 PEST ATTACK at {targetTile}! Soil K: {soil.Potassium}", LogLevel.Warn);
                            
                            // Aquí podrías dañar el cultivo:
                            // dirt.crop = null; // Destruir cultivo (Cuidado, es muy agresivo)
                            
                            return; // Solo ataca uno por tick para no spamear
                        }
                    }
                }
            }

            if (!pestAttempted) 
                this.Monitor.Log("🐜 Debug: No crops found nearby to attack.", LogLevel.Info);
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
                        if (dirt.crop.indexOfHarvest.Value == centerCrop.indexOfHarvest.Value)
                        {
                            score++;
                        }
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
