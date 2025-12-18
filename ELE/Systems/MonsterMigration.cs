using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Monsters;
using StardewValley.Locations;

namespace ELE.Core.Systems
{
    public class MonsterMigration
    {
        private readonly ModEntry Mod;
        private bool IsInvasionActive = false;
        private Point? FarmHouseDoor = null;

        public MonsterMigration(ModEntry mod)
        {
            this.Mod = mod;
        }

        public void CheckMigrationStatus()
        {
            IsInvasionActive = false;
            if (!this.Mod.Config.EnableMonsterMigration) return;

            if (Game1.isFestival() || Game1.eventUp || Game1.weddingToday) return;
            if (Game1.stats.DaysPlayed < this.Mod.Config.DaysBeforeTownInvasion) return;

            double chance = 0.05 + (Game1.player.CombatLevel * 0.01);
            
            if (Game1.random.NextDouble() < chance)
            {
                StartInvasion();
            }
        }

        public void ForceInvasion()
        {
            Mod.Monitor.Log("Forcing Monster Invasion...", LogLevel.Alert);
            StartInvasion();
        }

        private void StartInvasion()
        {
            IsInvasionActive = true;
            Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.farm_invasion"), 2));
            
            Farm farm = Game1.getFarm();
            
            // Stardew 1.6 Helper para encontrar la puerta
            FarmHouseDoor = farm.GetMainFarmHouseEntry();

            int mobCount = GetMobCountByDifficulty();
            Mod.Monitor.Log($"[ELE] Spawning {mobCount} monsters on Farm.", LogLevel.Info);

            for(int i=0; i<mobCount; i++)
            {
                SpawnMonster(farm);
            }
        }
        
        private void SpawnMonster(Farm farm)
        {
            Vector2 spawnTile = Vector2.Zero;
            bool found = false;
            
            for (int attempt = 0; attempt < 15; attempt++)
            {
                int x = Game1.random.Next(0, farm.Map.Layers[0].LayerWidth);
                int y = Game1.random.Next(0, farm.Map.Layers[0].LayerHeight);
                
                // Preferir bordes (lógica simple: 50% de probabilidad de forzar un borde X o Y)
                if (Game1.random.NextDouble() < 0.5) 
                    x = (Game1.random.NextDouble() < 0.5) ? Game1.random.Next(0, 5) : Game1.random.Next(farm.Map.Layers[0].LayerWidth - 5, farm.Map.Layers[0].LayerWidth);
                else
                    y = (Game1.random.NextDouble() < 0.5) ? Game1.random.Next(0, 5) : Game1.random.Next(farm.Map.Layers[0].LayerHeight - 5, farm.Map.Layers[0].LayerHeight);

                spawnTile = new Vector2(x, y);
                
                // Reemplazo manual de IsTileLocationTotallyClearAndPlaceable
                if (IsValidSpawnPosition(farm, spawnTile))
                {
                    found = true;
                    break;
                }
            }

            if (found)
            {
                Monster monster = null;
                Vector2 pos = spawnTile * 64f;
                int combatLevel = Game1.player.CombatLevel;

                if (combatLevel < 3) monster = new GreenSlime(pos);
                else if (combatLevel < 6) monster = (Game1.random.NextDouble() < 0.5) ? new GreenSlime(pos) : (Monster)new Bat(pos);
                else monster = (Game1.random.NextDouble() < 0.3) ? new GreenSlime(pos) : (Game1.random.NextDouble() < 0.5 ? (Monster)new Bat(pos) : (Monster)new Ghost(pos));

                if (monster != null)
                {
                    monster.focusedOnFarmers = true;
                    monster.wildernessFarmMonster = true;
                    farm.characters.Add(monster);
                }
            }
        }

        // Helper manual robusto para verificar spawn
        private bool IsValidSpawnPosition(GameLocation location, Vector2 tile)
        {
            // 1. Verificar colisiones (muros, acantilados)
            if (!location.isTilePassable(new xTile.Dimensions.Location((int)tile.X, (int)tile.Y), Game1.viewport))
                return false;

            // 2. Objetos (Piedras, Madera, Cofres)
            if (location.objects.ContainsKey(tile)) return false;

            // 3. TerrainFeatures (Cultivos, Arboles, Pisos)
            if (location.terrainFeatures.ContainsKey(tile)) return false;

            // 4. Large Terrain Features (Arbustos, Meteoritos)
            Rectangle tileRect = new Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64);
            if (location.largeTerrainFeatures != null)
            {
                foreach (var feature in location.largeTerrainFeatures)
                {
                    if (feature.getBoundingBox().Intersects(tileRect)) return false;
                }
            }

            // 5. Personajes (Jugador, NPCs, otros monstruos)
            if (Game1.player.Tile == tile) return false;
            foreach (var npc in location.characters)
            {
                if (npc.Tile == tile) return false;
            }

            // 6. Verificar agua
            if (location.isWaterTile((int)tile.X, (int)tile.Y))
                return false;
            
            // 7. Edificios (Solo si es Farm) - Chequeo manual de coordenadas
            if (location is Farm farm)
            {
                foreach(var building in farm.buildings)
                {
                    int bx = building.tileX.Value;
                    int by = building.tileY.Value;
                    int bw = building.tilesWide.Value;
                    int bh = building.tilesHigh.Value;

                    if (tile.X >= bx && tile.X < bx + bw && tile.Y >= by && tile.Y < by + bh)
                        return false;
                }
            }

            return true;
        }
        
        private int GetMobCountByDifficulty()
        {
            switch(this.Mod.Config.InvasionDifficulty)
            {
                case "Easy": return 3;
                case "Hard": return 12;
                case "VeryHard": return 20;
                default: return 7;
            }
        }

        public void UpdateInvasionLogic(TimeChangedEventArgs e)
        {
            if (!IsInvasionActive || Game1.currentLocation is not Farm farm) return;
            
            int monsterCount = farm.characters.Count(c => c is Monster);
            
            if (monsterCount == 0 && e.NewTime > 630) 
            {
                IsInvasionActive = false;
                Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.invasion_cleared"), 1));
                return;
            }

            if (FarmHouseDoor.HasValue)
            {
                Vector2 targetPixels = new Vector2(FarmHouseDoor.Value.X, FarmHouseDoor.Value.Y) * 64f;

                foreach(var npc in farm.characters)
                {
                    if (npc is Monster monster)
                    {
                        // IA Manual: Si está lejos, forzar movimiento hacia la puerta
                        if (Vector2.Distance(monster.Position, Game1.player.Position) > 600f)
                        {
                            MoveMonsterToward(monster, targetPixels);
                        }
                    }
                }
            }
        }

        // Movimiento vectorial manual (reemplaza a SetMovingTowardPoint)
        private void MoveMonsterToward(Monster monster, Vector2 target)
        {
            Vector2 position = monster.Position;
            Vector2 trajectory = target - position;

            if (trajectory.Length() > 4f)
            {
                trajectory.Normalize();
                // Velocidad base moderada
                float speed = 2f; 
                monster.xVelocity = trajectory.X * speed;
                monster.yVelocity = trajectory.Y * speed;

                // Actualizar dirección visual
                if (Math.Abs(trajectory.X) > Math.Abs(trajectory.Y))
                    monster.faceDirection(trajectory.X > 0 ? 1 : 3);
                else
                    monster.faceDirection(trajectory.Y > 0 ? 2 : 0);
            }
        }
    }
}