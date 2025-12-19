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
        private const string SoilDataKey = "JavCombita.ELE/SoilData";
        private const string BoosterAppliedKey = "JavCombita.ELE/BoosterApplied"; 
        private const string ShelterCountKey = "ele_pest_eaten";
        private const string LadybugShelterId = "JavCombita.ELE_LadybugShelter";

        public EcosystemManager(ModEntry mod) { 
            this.Mod = mod; 
            this.Monitor = mod.Monitor;
        }

        public void HandleInteraction(ButtonPressedEventArgs e)
        {
            if (!e.Button.IsActionButton()) return;
            Vector2 tile = e.Cursor.Tile;
            GameLocation loc = Game1.currentLocation;

            // 1. Shelter Counter Check
            if (loc.objects.TryGetValue(tile, out StardewValley.Object obj) && obj.ItemId == LadybugShelterId)
            {
                int count = 0;
                if (obj.modData.TryGetValue(ShelterCountKey, out string sCount)) int.TryParse(sCount, out count);
                Game1.drawObjectDialogue(this.Mod.Helper.Translation.Get("message.shelter_count", new { count = count }));
                Mod.Helper.Input.Suppress(e.Button);
                return;
            }

            // 2. Manual Booster Application (On Ground)
            Item held = Game1.player.CurrentItem;
            if (held != null && held.ItemId.StartsWith("JavCombita.ELE_Fertilizer"))
            {
                if (loc.terrainFeatures.TryGetValue(tile, out TerrainFeature tf) && tf is HoeDirt)
                {
                    // Intentar aplicar
                    if (TryApplyBoosterManual(loc, tile, held.ItemId)) {
                        // CORRECCIÓN: Lógica manual de reducción de item
                        held.Stack--;
                        if (held.Stack <= 0)
                        {
                            Game1.player.Items[Game1.player.CurrentToolIndex] = null;
                        }

                        Game1.playSound("dirtyHit");
                        Mod.Helper.Input.Suppress(e.Button);
                    }
                }
            }
        }

        private bool TryApplyBoosterManual(GameLocation loc, Vector2 tile, string id)
        {
            // Lógica bypass: Aplicar aunque haya fertilizante vanilla
            RestoreNutrients(loc, tile, id);
            return true; 
        }

        public void RestoreNutrients(GameLocation location, Vector2 tile, string fertilizerId)
        {
            bool isBooster = fertilizerId.StartsWith("JavCombita.ELE_Fertilizer");
            string boosterKey = $"{BoosterAppliedKey}/{tile.X},{tile.Y}";
            
            if (isBooster)
            {
                bool hasApplied = location.modData.TryGetValue(boosterKey, out string appliedType);
                bool isOmni = fertilizerId.Contains("Omni");

                if (hasApplied)
                {
                     // Si ya tiene booster, SOLO Omni puede sobrescribir a uno NO Omni
                     if (isOmni && !appliedType.Contains("Omni")) { /* Allow Override */ }
                     else return; // Bloqueado
                }
                location.modData[boosterKey] = fertilizerId;
            }

            // Aplicar data matemática
            SoilData data = GetSoilDataAt(location, tile);
            switch (fertilizerId)
            {
                case "(O)368": case "368": data.Nitrogen += 30f; break;
                case "(O)369": case "369": data.Nitrogen += 60f; data.Phosphorus += 30f; break;
                case "JavCombita.ELE_Fertilizer_N": data.Nitrogen += 80; break;
                case "JavCombita.ELE_Fertilizer_P": data.Phosphorus += 80; break;
                case "JavCombita.ELE_Fertilizer_K": data.Potassium += 80; break;
                case "JavCombita.ELE_Fertilizer_Omni": data.Nitrogen = 100; data.Phosphorus = 100; data.Potassium = 100; break;
            }
            
            // Clamp
            data.Nitrogen = Math.Min(data.Nitrogen, 100f); 
            data.Phosphorus = Math.Min(data.Phosphorus, 100f); 
            data.Potassium = Math.Min(data.Potassium, 100f);

            SaveSoilDataAt(location, tile, data);
            Game1.createRadialDebris(location, 12, (int)tile.X, (int)tile.Y, 6, false);
        }

        // --- LÓGICA DE PLAGAS Y SHELTER ---
        public void CalculateDailyNutrients() {
            foreach (GameLocation location in Game1.locations) {
                if (!location.IsFarm && !location.Name.Contains("Greenhouse")) continue;
                var terrainFeatures = location.terrainFeatures.Pairs.ToList();
                foreach (var pair in terrainFeatures) {
                    if (pair.Value is HoeDirt dirt && dirt.crop != null) ProcessCropNutrients(location, pair.Key, dirt);
                }
            }
        }

        public void ForcePestAttack() {
            Monitor.Log("Forcing Pest Attack...", LogLevel.Alert);
            GameLocation loc = Game1.currentLocation;
            Vector2 playerTile = Game1.player.Tile;
            // Buscar cultivos cercanos
            foreach (var pair in loc.terrainFeatures.Pairs) {
                if (pair.Value is HoeDirt dirt && dirt.crop != null && Vector2.Distance(pair.Key, playerTile) < 5) {
                    TrySpawnPests(loc, pair.Key, dirt, true); 
                }
            }
        }

        private void ProcessCropNutrients(GameLocation location, Vector2 tile, HoeDirt dirt) {
            if (!this.Mod.Config.EnableNutrientCycle) return;
            SoilData data = GetSoilDataAt(location, tile);
            
            // Consumo
            data.Nitrogen = Math.Max(0, data.Nitrogen - 2);
            data.Phosphorus = Math.Max(0, data.Phosphorus - 2);
            data.Potassium = Math.Max(0, data.Potassium - 2);
            SaveSoilDataAt(location, tile, data);

            // Reset Flag
            if (dirt.crop == null) location.modData.Remove($"{BoosterAppliedKey}/{tile.X},{tile.Y}");

            // Pests check
            if (data.Potassium < 50) TrySpawnPests(location, tile, dirt, false);
        }

        private void TrySpawnPests(GameLocation location, Vector2 tile, HoeDirt dirt, bool forced)
        {
            // Verificar Shelter ANTES de decidir si hay plaga
            if (InterceptByShelter(location, tile)) return;

            // Spawn normal (5% o forzado)
            if (forced || Game1.random.NextDouble() < 0.05) SpawnPest(location, tile, dirt, forced);
        }

        private bool InterceptByShelter(GameLocation location, Vector2 tile)
        {
            foreach (var pair in location.objects.Pairs)
            {
                if (pair.Value.ItemId == LadybugShelterId)
                {
                    if (Vector2.Distance(tile, pair.Key) <= 6) 
                    {
                        // Shelter Bloquea!
                        // 1. Incrementar contador
                        int current = 0;
                        if(pair.Value.modData.TryGetValue(ShelterCountKey, out string c)) int.TryParse(c, out current);
                        pair.Value.modData[ShelterCountKey] = (current + 1).ToString();

                        // 2. FX
                        var multiplayer = this.Mod.Helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue();
                        multiplayer.broadcastSprites(location, new TemporaryAnimatedSprite(362, 30f, 1, 1, tile * 64f, false, false){ color = Color.Cyan, scale = 4f });
                        
                        return true;
                    }
                }
            }
            return false;
        }

        private void SpawnPest(GameLocation loc, Vector2 tile, HoeDirt dirt, bool forced) {
            if (ModEntry.PestTexture != null) loc.temporarySprites.Add(new VerticalPestSprite(ModEntry.PestTexture, tile * 64f));
            if (forced || Game1.random.NextDouble() < 0.30) {
                dirt.crop = null; 
                Game1.playSound("cut");
                if(!forced) Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.pest_damage"), 3));
            }
        }

        // Helpers de Data
        public SoilData GetSoilDataAt(GameLocation location, Vector2 tile) {
            if (location.modData.TryGetValue($"{SoilDataKey}/{tile.X},{tile.Y}", out string dataStr)) return SoilData.FromString(dataStr);
            return new SoilData();
        }
        public void SaveSoilDataAt(GameLocation location, Vector2 tile, SoilData data) {
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