using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.TerrainFeatures;
using ELE.Core.Models;

namespace ELE.Core.Systems
{
    public class EcosystemManager
    {
        private readonly ModEntry Mod;
        private readonly IMonitor Monitor;
        
        // Claves de Data
        private const string SoilDataKey = "JavCombita.ELE/SoilData";
        private const string BoosterAppliedKey = "JavCombita.ELE/BoosterApplied"; 
        private const string ShelterCountKey = "ele_pest_eaten";
        
        // IDs
        private const string LadybugShelterId = "JavCombita.ELE_LadybugShelter";

        public EcosystemManager(ModEntry mod) 
        { 
            this.Mod = mod; 
            this.Monitor = mod.Monitor;
        }

        public void HandleInteraction(ButtonPressedEventArgs e)
        {
            if (!e.Button.IsActionButton() && e.Button != SButton.MouseLeft) return;
            
            Vector2 tile = e.Cursor.Tile;
            GameLocation loc = Game1.currentLocation;

            // 1. Shelter Counter Check
            if (loc.objects.TryGetValue(tile, out StardewValley.Object obj) && obj.ItemId == LadybugShelterId)
            {
                int count = 0;
                if (obj.modData.TryGetValue(ShelterCountKey, out string sCount)) 
                    int.TryParse(sCount, out count);
                
                Game1.drawObjectDialogue(this.Mod.Helper.Translation.Get("message.shelter_count", new { count = count }));
                Mod.Helper.Input.Suppress(e.Button);
                return;
            }

            // 2. Manual Booster Application
            Item held = Game1.player.CurrentItem;
            if (held != null && held.ItemId.StartsWith("JavCombita.ELE_Fertilizer"))
            {
                if (loc.terrainFeatures.TryGetValue(tile, out TerrainFeature tf) && tf is HoeDirt)
                {
                    // Validación de Rango (0-1 Tile)
                    if (!IsInRange(tile))
                    {
                        Game1.showRedMessage("Out of Range");
                        Mod.Helper.Input.Suppress(e.Button);
                        return;
                    }

                    if (TryApplyBoosterManual(loc, tile, held.ItemId)) 
                    {
                        // CORRECCIÓN ANIMACIÓN: Usar FarmerSprite.AnimationFrame explícitamente
                        Game1.player.FarmerSprite.animateOnce(new FarmerSprite.AnimationFrame(196, 150)); 
                        
                        Game1.createRadialDebris(loc, 14, (int)tile.X, (int)tile.Y, 4, false); 
                        Game1.playSound("dirtyHit");

                        // Consumo
                        held.Stack--;
                        if (held.Stack <= 0) 
                            Game1.player.Items[Game1.player.CurrentToolIndex] = null;

                        Mod.Helper.Input.Suppress(e.Button);
                    } 
                }
            }
        }

        private bool IsInRange(Vector2 targetTile)
        {
            Vector2 playerTile = Game1.player.Tile;
            return Math.Abs(targetTile.X - playerTile.X) <= 1 && Math.Abs(targetTile.Y - playerTile.Y) <= 1;
        }

        private bool TryApplyBoosterManual(GameLocation loc, Vector2 tile, string id)
        {
            bool isBooster = id.StartsWith("JavCombita.ELE_Fertilizer");
            string boosterKey = $"{BoosterAppliedKey}/{tile.X},{tile.Y}";
            
            if (isBooster)
            {
                bool hasApplied = loc.modData.TryGetValue(boosterKey, out string appliedType);
                bool isOmni = id.Contains("Omni");

                if (hasApplied)
                {
                     if (isOmni && !appliedType.Contains("Omni")) 
                     { 
                        // Permitir upgrade
                     }
                     else 
                     {
                        return false; 
                     }
                }
            }

            RestoreNutrients(loc, tile, id);
            return true;
        }

        public void RestoreNutrients(GameLocation location, Vector2 tile, string fertilizerId)
        {
            bool isBooster = fertilizerId.StartsWith("JavCombita.ELE_Fertilizer");
            string boosterKey = $"{BoosterAppliedKey}/{tile.X},{tile.Y}";
            
            if (isBooster)
            {
                location.modData[boosterKey] = fertilizerId;
            }

            SoilData data = GetSoilDataAt(location, tile);
            
            if (fertilizerId == "(O)368" || fertilizerId == "368") data.Nitrogen += 30f;
            else if (fertilizerId == "(O)369" || fertilizerId == "369") { data.Nitrogen += 60f; data.Phosphorus += 30f; }
            
            else if (fertilizerId.Contains("Fertilizer_N")) data.Nitrogen += 80f;
            else if (fertilizerId.Contains("Fertilizer_P")) data.Phosphorus += 80f;
            else if (fertilizerId.Contains("Fertilizer_K")) data.Potassium += 80f;
            else if (fertilizerId.Contains("Fertilizer_Omni")) 
            { 
                data.Nitrogen = 100f; 
                data.Phosphorus = 100f; 
                data.Potassium = 100f; 
            }
            
            data.Nitrogen = Math.Min(data.Nitrogen, 100f); 
            data.Phosphorus = Math.Min(data.Phosphorus, 100f); 
            data.Potassium = Math.Min(data.Potassium, 100f);

            SaveSoilDataAt(location, tile, data);
            
            if (isBooster)
            {
                Game1.createRadialDebris(location, 12, (int)tile.X, (int)tile.Y, 6, false);
            }
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
            this.Monitor.Log(this.Mod.Helper.Translation.Get("log.soil_analysis_daily"), LogLevel.Trace);
        }

        public void ForcePestAttack() 
        {
            Monitor.Log(this.Mod.Helper.Translation.Get("debug.pest_force"), LogLevel.Alert);
            GameLocation loc = Game1.currentLocation;
            Vector2 playerTile = Game1.player.Tile;
            
            foreach (var pair in loc.terrainFeatures.Pairs) 
            {
                if (pair.Value is HoeDirt dirt && dirt.crop != null && Vector2.Distance(pair.Key, playerTile) < 5) 
                {
                    TrySpawnPests(loc, pair.Key, dirt, true); 
                }
            }
        }

        private void ProcessCropNutrients(GameLocation location, Vector2 tile, HoeDirt dirt) 
        {
            if (!this.Mod.Config.EnableNutrientCycle) return;
            SoilData data = GetSoilDataAt(location, tile);
            
            float drain = 2.0f * this.Mod.Config.NutrientDepletionMultiplier;
            data.Nitrogen = Math.Max(0, data.Nitrogen - drain);
            data.Phosphorus = Math.Max(0, data.Phosphorus - drain);
            data.Potassium = Math.Max(0, data.Potassium - drain);
            
            SaveSoilDataAt(location, tile, data);

            if (dirt.crop == null) 
                location.modData.Remove($"{BoosterAppliedKey}/{tile.X},{tile.Y}");

            if (data.Potassium < 50) 
                TrySpawnPests(location, tile, dirt, false);
        }

        private void TrySpawnPests(GameLocation location, Vector2 tile, HoeDirt dirt, bool forced)
        {
            if (InterceptByShelter(location, tile)) return;

            if (forced || Game1.random.NextDouble() < 0.05) 
                SpawnPest(location, tile, dirt, forced);
        }

        private bool InterceptByShelter(GameLocation location, Vector2 tile)
        {
            foreach (var pair in location.objects.Pairs)
            {
                if (pair.Value.ItemId == LadybugShelterId)
                {
                    if (Vector2.Distance(tile, pair.Key) <= 6) 
                    {
                        int current = 0;
                        if(pair.Value.modData.TryGetValue(ShelterCountKey, out string c)) 
                            int.TryParse(c, out current);
                        pair.Value.modData[ShelterCountKey] = (current + 1).ToString();

                        var multiplayer = this.Mod.Helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue();
                        multiplayer.broadcastSprites(location, new TemporaryAnimatedSprite(362, 30f, 1, 1, tile * 64f, false, false){ color = Color.Cyan, scale = 4f });
                        
                        return true; 
                    }
                }
            }
            return false;
        }

        private void SpawnPest(GameLocation loc, Vector2 tile, HoeDirt dirt, bool forced) 
        {
            if (ModEntry.PestTexture != null) 
                loc.temporarySprites.Add(new VerticalPestSprite(ModEntry.PestTexture, tile * 64f));
            
            if (forced || Game1.random.NextDouble() < 0.30) 
            {
                dirt.crop = null; 
                Game1.playSound("cut");
                if(!forced) 
                    Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.pest_damage"), 3));
            }
            else
            {
                SoilData data = GetSoilDataAt(loc, tile);
                data.Nitrogen = Math.Max(0, data.Nitrogen - 20);
                data.Phosphorus = Math.Max(0, data.Phosphorus - 20);
                SaveSoilDataAt(loc, tile, data);
            }
        }

        public SoilData GetSoilDataAt(GameLocation location, Vector2 tile) 
        {
            if (location.modData.TryGetValue($"{SoilDataKey}/{tile.X},{tile.Y}", out string dataStr)) 
                return SoilData.FromString(dataStr);
            return new SoilData();
        }
        
        public void SaveSoilDataAt(GameLocation location, Vector2 tile, SoilData data) 
        {
            location.modData[$"{SoilDataKey}/{tile.X},{tile.Y}"] = data.ToString();
        }
    }
    
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