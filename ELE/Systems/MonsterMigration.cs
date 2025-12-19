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

            double chance = 0.10 + (Game1.player.CombatLevel * 0.02);
            
            if (Game1.random.NextDouble() < chance)
            {
                StartInvasion();
            }
        }

        public void ForceInvasion()
        {
            Mod.Monitor.Log(Mod.Helper.Translation.Get("debug.invasion_force"), LogLevel.Alert);
            StartInvasion();
        }

        private void StartInvasion()
        {
            IsInvasionActive = true;
            Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.farm_invasion"), 2));
            
            Farm farm = Game1.getFarm();
            FarmHouseDoor = farm.GetMainFarmHouseEntry();

            int mobCount = GetMobCountRange();
            Mod.Monitor.Log(Mod.Helper.Translation.Get("log.invasion_started", new { 
                diff = this.Mod.Config.InvasionDifficulty, 
                count = mobCount 
            }), LogLevel.Info);

            for(int i=0; i < mobCount; i++)
            {
                SpawnRandomMonster(farm);
            }
        }
        
        private int GetMobCountRange()
        {
            switch(this.Mod.Config.InvasionDifficulty)
            {
                case "Easy": return Game1.random.Next(10, 20);
                case "Hard": return Game1.random.Next(30, 50);
                case "VeryHard": return Game1.random.Next(50, 70); 
                default: return Game1.random.Next(20, 30); 
            }
        }

        private void SpawnRandomMonster(Farm farm)
        {
            Vector2 spawnTile = GetValidSpawnPosition(farm);
            if (spawnTile == Vector2.Zero) return; 

            Vector2 pixelPos = spawnTile * 64f;
            Monster monster = CreateMonsterForLevel(pixelPos);

            if (monster != null)
            {
                monster.focusedOnFarmers = true;
                monster.wildernessFarmMonster = true; 
                farm.characters.Add(monster);
            }
        }

        private Monster CreateMonsterForLevel(Vector2 position)
        {
            int combatLevel = Game1.player.CombatLevel;
            int difficultyTier = 0; 

            if (combatLevel >= 8 || this.Mod.Config.InvasionDifficulty == "VeryHard") difficultyTier = 2;
            else if (combatLevel >= 4 || this.Mod.Config.InvasionDifficulty == "Hard") difficultyTier = 1;

            double roll = Game1.random.NextDouble();

            switch (difficultyTier)
            {
                case 2: 
                    if (roll < 0.3) return new Serpent(position);
                    if (roll < 0.5) return new ShadowBrute(position);
                    if (roll < 0.7) return new Skeleton(position, true); 
                    if (roll < 0.9) return new GreenSlime(position, 121); 
                    return new Bat(position, 81); 

                case 1: 
                    if (roll < 0.3) return new Ghost(position);
                    if (roll < 0.5) return new Bat(position, 41); 
                    if (roll < 0.7) return new Skeleton(position, false);
                    if (roll < 0.9) return new GreenSlime(position, 40); 
                    return new DustSpirit(position);

                default: 
                    if (roll < 0.4) return new GreenSlime(position, 0);
                    if (roll < 0.7) return new RockCrab(position);
                    if (roll < 0.9) return new Grub(position, true); 
                    return new Bat(position, 0); 
            }
        }

        private Vector2 GetValidSpawnPosition(Farm farm)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int x, y;
                if (Game1.random.NextDouble() < 0.5) {
                    x = (Game1.random.NextDouble() < 0.5) ? Game1.random.Next(0, 4) : Game1.random.Next(farm.Map.Layers[0].LayerWidth - 4, farm.Map.Layers[0].LayerWidth);
                    y = Game1.random.Next(0, farm.Map.Layers[0].LayerHeight);
                } else {
                    x = Game1.random.Next(0, farm.Map.Layers[0].LayerWidth);
                    y = (Game1.random.NextDouble() < 0.5) ? Game1.random.Next(0, 4) : Game1.random.Next(farm.Map.Layers[0].LayerHeight - 4, farm.Map.Layers[0].LayerHeight);
                }

                Vector2 tile = new Vector2(x, y);
                if (IsValidSpawnPosition(farm, tile)) return tile;
            }
            
            return Vector2.Zero; 
        }

        private bool IsValidSpawnPosition(GameLocation location, Vector2 tile)
        {
            // Verificaciones estándar de terreno
            if (!location.isTilePassable(new xTile.Dimensions.Location((int)tile.X, (int)tile.Y), Game1.viewport)) return false;
            if (location.isWaterTile((int)tile.X, (int)tile.Y)) return false;
            if (location.objects.ContainsKey(tile)) return false;
            if (location.terrainFeatures.ContainsKey(tile)) return false;
            
            // Verificación específica de edificios en la granja
            if (location is Farm farm)
            {
                foreach(var building in farm.buildings)
                {
                    // [FIX] Usamos occupiesTile(Vector2) en lugar de containsPoint(int, int)
                    if (building.occupiesTile(tile)) return false;
                }
            }
            return true;
        }

        public void UpdateInvasionLogic(TimeChangedEventArgs e)
        {
            if (!IsInvasionActive || Game1.currentLocation is not Farm farm) return;
            
            int monsterCount = farm.characters.Count(c => c is Monster);
            
            if ((monsterCount == 0 && e.NewTime > 630) || e.NewTime > 1000) 
            {
                EndInvasion();
                return;
            }

            if (FarmHouseDoor.HasValue)
            {
                Vector2 targetPixels = new Vector2(FarmHouseDoor.Value.X, FarmHouseDoor.Value.Y) * 64f;
                foreach(var npc in farm.characters)
                {
                    if (npc is Monster monster)
                    {
                        if (Vector2.Distance(monster.Position, Game1.player.Position) > 512f)
                        {
                            MoveMonsterToward(monster, targetPixels);
                        }
                    }
                }
            }
        }

        private void EndInvasion()
        {
            IsInvasionActive = false;
            Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.invasion_cleared"), 1));
        }

        private void MoveMonsterToward(Monster monster, Vector2 target)
        {
            Vector2 position = monster.Position;
            Vector2 trajectory = target - position;

            if (trajectory.Length() > 4f)
            {
                trajectory.Normalize();
                float speed = 2f; 
                monster.xVelocity = trajectory.X * speed;
                monster.yVelocity = trajectory.Y * speed;

                if (Math.Abs(trajectory.X) > Math.Abs(trajectory.Y))
                    monster.faceDirection(trajectory.X > 0 ? 1 : 3);
                else
                    monster.faceDirection(trajectory.Y > 0 ? 2 : 0);
            }
        }
    }
}