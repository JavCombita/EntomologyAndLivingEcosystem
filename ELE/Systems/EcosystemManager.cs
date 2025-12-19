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

        /// <summary>
        /// Maneja clics en objetos del ecosistema (Shelter, Suelo para Boosters)
        /// </summary>
        public void HandleInteraction(ButtonPressedEventArgs e)
        {
            // Solo actuar en interacción principal (Botón derecho / Toque / Acción)
            if (!e.Button.IsActionButton() && e.Button != SButton.MouseLeft) return;
            
            Vector2 tile = e.Cursor.Tile;
            GameLocation loc = Game1.currentLocation;

            // 1. Shelter Counter Check (Clic en Shelter)
            if (loc.objects.TryGetValue(tile, out StardewValley.Object obj) && obj.ItemId == LadybugShelterId)
            {
                int count = 0;
                if (obj.modData.TryGetValue(ShelterCountKey, out string sCount)) 
                    int.TryParse(sCount, out count);
                
                Game1.drawObjectDialogue(this.Mod.Helper.Translation.Get("message.shelter_count", new { count = count }));
                
                // Prevenir menú por defecto
                Mod.Helper.Input.Suppress(e.Button);
                return;
            }

            // 2. Manual Booster Application (Clic en suelo con Booster en mano)
            Item held = Game1.player.CurrentItem;
            if (held != null && held.ItemId.StartsWith("JavCombita.ELE_Fertilizer"))
            {
                // Verificar si clicamos tierra arada
                if (loc.terrainFeatures.TryGetValue(tile, out TerrainFeature tf) && tf is HoeDirt)
                {
                    // Intentar aplicar
                    if (TryApplyBoosterManual(loc, tile, held.ItemId)) 
                    {
                        // Reducir stack manualmente (Fix para 1.6)
                        held.Stack--;
                        if (held.Stack <= 0) 
                            Game1.player.Items[Game1.player.CurrentToolIndex] = null;

                        Game1.playSound("dirtyHit");
                        Mod.Helper.Input.Suppress(e.Button);
                    } 
                }
            }
        }

        private bool TryApplyBoosterManual(GameLocation loc, Vector2 tile, string id)
        {
            // Verificación previa para no gastar item si falla la regla
            bool isBooster = id.StartsWith("JavCombita.ELE_Fertilizer");
            string boosterKey = $"{BoosterAppliedKey}/{tile.X},{tile.Y}";
            
            if (isBooster)
            {
                bool hasApplied = loc.modData.TryGetValue(boosterKey, out string appliedType);
                bool isOmni = id.Contains("Omni");

                if (hasApplied)
                {
                     // Regla: Solo Omni reemplaza a otros. Otros no reemplazan nada.
                     if (isOmni && !appliedType.Contains("Omni")) 
                     { 
                        // Permitir reemplazo (Upgrade a Omni)
                     }
                     else 
                     {
                        return false; // Bloqueado
                     }
                }
            }

            // Aplicar
            RestoreNutrients(loc, tile, id);
            return true;
        }

        /// <summary>
        /// Aplica los valores nutricionales al suelo.
        /// </summary>
        public void RestoreNutrients(GameLocation location, Vector2 tile, string fertilizerId)
        {
            bool isBooster = fertilizerId.StartsWith("JavCombita.ELE_Fertilizer");
            string boosterKey = $"{BoosterAppliedKey}/{tile.X},{tile.Y}";
            
            // Marcar suelo si es booster
            if (isBooster)
            {
                location.modData[boosterKey] = fertilizerId;
            }

            // Aplicar data matemática
            SoilData data = GetSoilDataAt(location, tile);
            
            // Vanilla IDs
            if (fertilizerId == "(O)368" || fertilizerId == "368") data.Nitrogen += 30f;
            else if (fertilizerId == "(O)369" || fertilizerId == "369") { data.Nitrogen += 60f; data.Phosphorus += 30f; }
            
            // ELE Boosters
            else if (fertilizerId.Contains("Fertilizer_N")) data.Nitrogen += 80f;
            else if (fertilizerId.Contains("Fertilizer_P")) data.Phosphorus += 80f;
            else if (fertilizerId.Contains("Fertilizer_K")) data.Potassium += 80f;
            else if (fertilizerId.Contains("Fertilizer_Omni")) 
            { 
                data.Nitrogen = 100f; 
                data.Phosphorus = 100f; 
                data.Potassium = 100f; 
            }
            
            // Clamp (Max 100)
            data.Nitrogen = Math.Min(data.Nitrogen, 100f); 
            data.Phosphorus = Math.Min(data.Phosphorus, 100f); 
            data.Potassium = Math.Min(data.Potassium, 100f);

            SaveSoilDataAt(location, tile, data);
            
            // Efecto visual solo si es booster (para no duplicar el de vanilla)
            if (isBooster)
            {
                Game1.createRadialDebris(location, 12, (int)tile.X, (int)tile.Y, 6, false);
            }
        }

        // --- LÓGICA DE PLAGAS Y SHELTER ---

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

        public void ForcePestAttack() 
        {
            Monitor.Log("Forcing Pest Attack...", LogLevel.Alert);
            GameLocation loc = Game1.currentLocation;
            Vector2 playerTile = Game1.player.Tile;
            
            // Buscar cultivos cercanos
            foreach (var pair in loc.terrainFeatures.Pairs) 
            {
                if (pair.Value is HoeDirt dirt && dirt.crop != null && Vector2.Distance(pair.Key, playerTile) < 5) 
                {
                    // Forzar ataque
                    TrySpawnPests(loc, pair.Key, dirt, true); 
                }
            }
        }

        private void ProcessCropNutrients(GameLocation location, Vector2 tile, HoeDirt dirt) 
        {
            if (!this.Mod.Config.EnableNutrientCycle) return;
            SoilData data = GetSoilDataAt(location, tile);
            
            // Consumo simple
            float drain = 2.0f * this.Mod.Config.NutrientDepletionMultiplier;
            data.Nitrogen = Math.Max(0, data.Nitrogen - drain);
            data.Phosphorus = Math.Max(0, data.Phosphorus - drain);
            data.Potassium = Math.Max(0, data.Potassium - drain);
            
            SaveSoilDataAt(location, tile, data);

            // Reset Flag si el cultivo murió o se recogió
            if (dirt.crop == null) 
                location.modData.Remove($"{BoosterAppliedKey}/{tile.X},{tile.Y}");

            // Chequeo de Plagas (Si K es bajo)
            if (data.Potassium < 50) 
                TrySpawnPests(location, tile, dirt, false);
        }

        private void TrySpawnPests(GameLocation location, Vector2 tile, HoeDirt dirt, bool forced)
        {
            // 1. Verificar Shelter (Defensa)
            if (InterceptByShelter(location, tile)) return;

            // 2. Spawn normal (5% o forzado)
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
                        // Shelter Bloquea!
                        
                        // 1. Incrementar contador
                        int current = 0;
                        if(pair.Value.modData.TryGetValue(ShelterCountKey, out string c)) 
                            int.TryParse(c, out current);
                        pair.Value.modData[ShelterCountKey] = (current + 1).ToString();

                        // 2. FX (Escudo Cian)
                        // Usamos Reflection para acceder a multiplayer de forma segura en 1.6
                        var multiplayer = this.Mod.Helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue();
                        multiplayer.broadcastSprites(location, new TemporaryAnimatedSprite(362, 30f, 1, 1, tile * 64f, false, false){ color = Color.Cyan, scale = 4f });
                        
                        return true; // Interceptado
                    }
                }
            }
            return false;
        }

        private void SpawnPest(GameLocation loc, Vector2 tile, HoeDirt dirt, bool forced) 
        {
            // Visual FX
            if (ModEntry.PestTexture != null) 
                loc.temporarySprites.Add(new VerticalPestSprite(ModEntry.PestTexture, tile * 64f));
            
            // Daño (30% Muerte)
            if (forced || Game1.random.NextDouble() < 0.30) 
            {
                dirt.crop = null; 
                Game1.playSound("cut");
                if(!forced) 
                    Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.pest_damage"), 3));
            }
            else
            {
                // Drenaje masivo si sobrevive
                SoilData data = GetSoilDataAt(loc, tile);
                data.Nitrogen = Math.Max(0, data.Nitrogen - 20);
                data.Phosphorus = Math.Max(0, data.Phosphorus - 20);
                SaveSoilDataAt(loc, tile, data);
            }
        }

        // Helpers de Data
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
    
    // Clase auxiliar para la animación de plaga
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