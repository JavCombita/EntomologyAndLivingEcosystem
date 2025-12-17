using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Monsters;
using StardewValley.Locations;

namespace ELE.Core.Systems
{
    public class MonsterMigration
    {
        private readonly ModEntry Mod;
        private bool IsInvasionActive = false;
        private Vector2? InvasionRallyPoint = null;
        private List<string> CachedMonsterList = new List<string>();

        // Cache para consultar datos de mobs de otros mods rápidamente
        private Dictionary<string, string> MonsterDataCache;

        public MonsterMigration(ModEntry mod)
        {
            this.Mod = mod;
        }

        public void CheckMigrationStatus()
        {
            IsInvasionActive = false;
            if (!this.Mod.Config.EnableMonsterMigration) return;

            // 1. CHEQUEO DE EVENTOS
            if (Game1.isFestival() || Game1.eventUp || Game1.weddingToday) 
                return;

            if (Game1.stats.DaysPlayed < this.Mod.Config.DaysBeforeTownInvasion) return;

            // Probabilidad base ajustada por nivel
            double chance = 0.10 + (Game1.player.CombatLevel * 0.01);
            if (Game1.random.NextDouble() < chance)
            {
                TriggerFarmInvasion();
            }
        }

        /// <summary>
        /// DEBUG COMMAND: Called by 'ele_invasion' command.
        /// </summary>
        public void ForceInvasion()
        {
            // Nota: Recuerda actualizar tu ModEntry.cs para llamar a este método (ForceInvasion)
            // en lugar de ForceTownInvasion.
            
            GameLocation farm = Game1.getFarm();
            if (farm == null) return;

            this.Mod.Monitor.Log("⚔️ DEBUG: Forcing monster invasion on FARM...", LogLevel.Warn);
            TriggerFarmInvasion();
        }

        private void TriggerFarmInvasion()
        {
            GameLocation farm = Game1.getFarm();
            if (farm == null) return;

            IsInvasionActive = true;
            this.Mod.Monitor.Log("⚠️ FARM INVASION STARTED!", LogLevel.Warn);
            
            Game1.addHUDMessage(new HUDMessage("The monsters are attacking your farm!", 2)); // Puedes traducir esto
            Game1.playSound("shadowpeep");

            // RALLY POINT: La puerta de la casa
            // Los monstruos se agruparán en la entrada de tu casa si no te encuentran.
            Point farmhouseEntry = farm.GetMainFarmHouseEntry();
            InvasionRallyPoint = new Vector2(farmhouseEntry.X, farmhouseEntry.Y);

            // --- DIFICULTAD DINÁMICA ---
            int combatLevel = Game1.player.CombatLevel;
            int minMonsters = 5;
            int maxMonsters = 10;
            
            string difficulty = this.Mod.Config.InvasionDifficulty ?? "Medium";

            switch (difficulty)
            {
                case "Easy": minMonsters = 3; maxMonsters = 5 + combatLevel; break;
                case "Medium": minMonsters = 5; maxMonsters = 7 + (int)(combatLevel * 1.5f); break;
                case "Hard": minMonsters = 8; maxMonsters = 10 + (combatLevel * 2); break;
                case "VeryHard": minMonsters = 12; maxMonsters = 15 + (combatLevel * 3); break;
                default: minMonsters = 5; maxMonsters = 7 + (int)(combatLevel * 1.5f); break;
            }

            int monsterCount = Game1.random.Next(minMonsters, maxMonsters + 1); 
            // ---------------------------

            RefreshSpawnableMonsters();

            int spawnedCount = 0;
            for (int i = 0; i < monsterCount; i++)
            {
                if (SpawnRandomMonster(farm)) spawnedCount++;
            }
            this.Mod.Monitor.Log($"⚔️ Invasion Status: {spawnedCount}/{monsterCount} monsters spawned on Farm.", LogLevel.Info);
        }

        private void RefreshSpawnableMonsters()
        {
            CachedMonsterList.Clear();
            MonsterDataCache = Game1.content.Load<Dictionary<string, string>>("Data/Monsters");
            int playerLevel = Game1.player.CombatLevel;

            foreach (var kvp in MonsterDataCache)
            {
                string name = kvp.Key;
                string rawData = kvp.Value;
                string[] fields = rawData.Split('/');

                if (fields.Length < 2) continue;

                if (int.TryParse(fields[0], out int hp) && int.TryParse(fields[1], out int damage))
                {
                    int difficultyScore = (hp / 20) + damage;
                    int maxDifficultyAllowed = (playerLevel + 1) * 25; 

                    if (difficultyScore <= maxDifficultyAllowed)
                    {
                        int weight = 1;
                        if (difficultyScore > maxDifficultyAllowed / 2) weight = 3; 
                        if (difficultyScore < maxDifficultyAllowed / 10) weight = 1;

                        for(int w=0; w < weight; w++) CachedMonsterList.Add(name);
                    }
                }
            }
            if (CachedMonsterList.Count == 0) CachedMonsterList.Add("Green Slime");
        }

        private bool SpawnRandomMonster(GameLocation location)
        {
            Vector2 spawnTile = GetRandomSpawnSpot(location);
            if (spawnTile == Vector2.Zero) return false;

            if (CachedMonsterList.Count == 0) RefreshSpawnableMonsters();
            string monsterName = CachedMonsterList[Game1.random.Next(CachedMonsterList.Count)];

            Monster monster = CreateMonsterFactory(monsterName, spawnTile);
            
            if (monster != null)
            {
                monster.focusedOnFarmers = true; 
                location.characters.Add(monster);
                this.Mod.Monitor.Log($"Spawned {monster.Name} at {spawnTile}.", LogLevel.Trace);
                return true;
            }
            return false;
        }

        private Monster CreateMonsterFactory(string name, Vector2 tile)
        {
            Vector2 position = tile * 64f;
            
            // 1. INTENTO POR NOMBRE
            if (name.Contains("Slime") || name.Contains("Jelly")) { var m = new GreenSlime(position, 0); m.Name = name; return m; }
            if (name.Contains("Bat") || name.Contains("Frost Bat") || name.Contains("Lava Bat")) return new Bat(position, 0) { Name = name }; 
            if (name.Contains("Bug") || name.Contains("Fly")) return new Bug(position, 0); 
            if (name.Contains("Grub")) return new Grub(position, true); 
            if (name.Contains("Ghost")) return new Ghost(position);
            if (name.Contains("Skeleton")) return new Skeleton(position);
            if (name.Contains("Crab")) { var m = new RockCrab(position); m.Name = name; return m; }
            if (name.Contains("Golem") || name.Contains("Stone")) return new RockGolem(position);
            if (name.Contains("Shadow") || name.Contains("Brute")) return new ShadowBrute(position);
            if (name.Contains("Shaman")) return new ShadowShaman(position);
            if (name.Contains("Serpent")) return new Serpent(position);
            if (name.Contains("Dust")) return new DustSpirit(position);

            // 2. SMART FALLBACK (MODS)
            if (MonsterDataCache != null && MonsterDataCache.TryGetValue(name, out string rawData))
            {
                string[] fields = rawData.Split('/');
                if (fields.Length > 4 && bool.TryParse(fields[4], out bool isGlider) && isGlider)
                    return new Bat(position, 0) { Name = name };
                else
                    return new RockGolem(position) { Name = name };
            }

            // 3. FALLBACK FINAL
            var fallback = new GreenSlime(position, 0);
            fallback.Name = name;
            return fallback;
        }

        private Vector2 GetRandomSpawnSpot(GameLocation location)
        {
            int attempts = 0;
            int mapWidth = location.Map.Layers[0].LayerWidth;
            int mapHeight = location.Map.Layers[0].LayerHeight;

            while (attempts < 50)
            {
                int x = Game1.random.Next(0, mapWidth);
                int y = Game1.random.Next(0, mapHeight);
                Vector2 tile = new Vector2(x, y);

                if (IsTileValidForSpawn(location, tile)) return tile;
                attempts++;
            }
            return Vector2.Zero;
        }

        private bool IsTileValidForSpawn(GameLocation location, Vector2 tile)
        {
            // Verificación manual robusta
            if (!location.isTileOnMap(tile)) return false;
            if (location.isWaterTile((int)tile.X, (int)tile.Y)) return false;
            
            // Ocupación
            if (location.Objects.ContainsKey(tile)) return false;
            if (location.getLargeTerrainFeatureAt((int)tile.X, (int)tile.Y) != null) return false;
            if (location.isTileOccupiedByFarmer(tile) != null) return false;

            // Colisiones físicas
            Rectangle tileRect = new Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64);
            if (location.isCollidingPosition(tileRect, Game1.viewport, false, 0, false, null)) return false;

            // Caminable
            if (!location.isTilePassable(new xTile.Dimensions.Location((int)tile.X, (int)tile.Y), Game1.viewport)) return false;

            return true;
        }

        public void UpdateMigratingMonsters()
        {
            if (!IsInvasionActive) return;
            
            // IMPORTANTE: Ahora chequeamos si estamos en la Granja
            if (Game1.currentLocation == null || !Game1.currentLocation.IsFarm) return;

            foreach (var npc in Game1.currentLocation.characters)
            {
                if (npc is Monster monster)
                {
                    ControlMonsterAI(monster);
                }
            }
        }

        private void ControlMonsterAI(Monster monster)
        {
            // Atacar al jugador si está cerca
            if (Vector2.Distance(monster.Position, Game1.player.Position) < 500f)
            {
                if (!monster.focusedOnFarmers) monster.focusedOnFarmers = true;
                return;
            }

            // Ir hacia la puerta de la Casa (Rally Point)
            if (InvasionRallyPoint.HasValue)
            {
                Vector2 targetPixels = InvasionRallyPoint.Value * 64f;
                Vector2 trajectory = targetPixels - monster.Position;
                
                if (trajectory.Length() > 20f)
                {
                    trajectory.Normalize();
                    float speed = monster is Bat || monster is Ghost ? 3f : 1.5f; 
                    monster.xVelocity = trajectory.X * speed;
                    monster.yVelocity = trajectory.Y * speed;

                    if (monster is GreenSlime slime)
                    {
                        slime.faceDirection((int)trajectory.X > 0 ? 1 : 3);
                        if (Game1.random.NextDouble() < 0.05) slime.setTrajectory((int)(trajectory.X * 8), (int)(trajectory.Y * 8));
                    }
                }
            }
        }
    }
}
