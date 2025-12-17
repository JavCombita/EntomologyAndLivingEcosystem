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

        public MonsterMigration(ModEntry mod)
        {
            this.Mod = mod;
        }

        public void CheckMigrationStatus()
        {
            IsInvasionActive = false;
            if (!this.Mod.Config.EnableMonsterMigration) return;
            if (Game1.stats.DaysPlayed < this.Mod.Config.DaysBeforeTownInvasion) return;

            // Probabilidad base ajustada también por dificultad implícitamente (más nivel = más invasiones)
            double chance = 0.10 + (Game1.player.CombatLevel * 0.01);
            if (Game1.random.NextDouble() < chance)
            {
                TriggerTownInvasion();
            }
        }

        private void TriggerTownInvasion()
        {
            GameLocation town = Game1.getLocationFromName("Town");
            if (town == null) return;

            IsInvasionActive = true;
            this.Mod.Monitor.Log("⚠️ INVASION STARTED: The horde is approaching!", LogLevel.Warn);
            
            Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.town_invasion"), 2));
            Game1.playSound("shadowpeep");

            // Buscar la puerta del Saloon para que los monstruos vayan hacia allá (Meta narrativa: quieren cerveza/comida)
            Point saloonDoor = town.doors.Keys.FirstOrDefault(d => town.doors[d] == "Saloon");
            if (saloonDoor != Point.Zero)
            {
                InvasionRallyPoint = new Vector2(saloonDoor.X, saloonDoor.Y);
            }

            // --- LÓGICA DE DIFICULTAD DINÁMICA ---
            int combatLevel = Game1.player.CombatLevel;
            int minMonsters = 5;
            int maxMonsters = 10;
            
            // Protección contra nulos
            string difficulty = this.Mod.Config.InvasionDifficulty ?? "Medium";

            switch (difficulty)
            {
                case "Easy":
                    minMonsters = 3;
                    maxMonsters = 5 + combatLevel;
                    break;
                case "Medium":
                    minMonsters = 5;
                    maxMonsters = 7 + (int)(combatLevel * 1.5f);
                    break;
                case "Hard":
                    minMonsters = 8;
                    maxMonsters = 10 + (combatLevel * 2);
                    break;
                case "VeryHard":
                    minMonsters = 12;
                    maxMonsters = 15 + (combatLevel * 3);
                    break;
                default:
                    // Fallback a Medium
                    minMonsters = 5;
                    maxMonsters = 7 + (int)(combatLevel * 1.5f);
                    break;
            }

            int monsterCount = Game1.random.Next(minMonsters, maxMonsters + 1); 
            // -------------------------------------

            RefreshSpawnableMonsters();

            for (int i = 0; i < monsterCount; i++)
            {
                SpawnRandomMonster(town);
            }
        }

        private void RefreshSpawnableMonsters()
        {
            CachedMonsterList.Clear();
            var monsterData = Game1.content.Load<Dictionary<string, string>>("Data/Monsters");
            int playerLevel = Game1.player.CombatLevel;

            foreach (var kvp in monsterData)
            {
                string name = kvp.Key;
                string rawData = kvp.Value;
                string[] fields = rawData.Split('/');

                if (fields.Length < 2) continue;

                int hp = int.Parse(fields[0]);
                int damage = int.Parse(fields[1]);
                
                // Fórmula de "Puntaje de Peligro" para no spawnear dragones nivel 10 si eres nivel 1
                int difficultyScore = (hp / 20) + damage;
                int maxDifficultyAllowed = (playerLevel + 1) * 20;

                if (difficultyScore <= maxDifficultyAllowed)
                {
                    int weight = 1;
                    // Spawnear más monstruos desafiantes, menos basura débil
                    if (difficultyScore > maxDifficultyAllowed / 2) weight = 3; 
                    if (difficultyScore < maxDifficultyAllowed / 10) weight = 1;

                    for(int w=0; w < weight; w++)
                    {
                        CachedMonsterList.Add(name);
                    }
                }
            }
        }

        private void SpawnRandomMonster(GameLocation location)
        {
            Vector2 spawnTile = GetRandomSpawnSpot(location);
            if (spawnTile == Vector2.Zero) return;

            if (CachedMonsterList.Count == 0) RefreshSpawnableMonsters();
            string monsterName = CachedMonsterList[Game1.random.Next(CachedMonsterList.Count)];

            Monster monster = CreateMonsterFactory(monsterName, spawnTile);
            
            if (monster != null)
            {
                monster.focusedOnFarmers = true; // Agresividad activada
                location.characters.Add(monster);
                this.Mod.Monitor.Log($"Spawned {monster.Name} at Town.", LogLevel.Trace);
            }
        }

        private Monster CreateMonsterFactory(string name, Vector2 tile)
        {
            Vector2 position = tile * 64f;
            
            // Factory Pattern compatible con Stardew 1.6
            if (name.Contains("Slime") || name.Contains("Jelly")) 
            {
                var slime = new GreenSlime(position, 0); 
                slime.Name = name;
                return slime;
            }
            
            if (name.Contains("Bat") || name.Contains("Frost Bat") || name.Contains("Lava Bat")) 
                return new Bat(position, 0) { Name = name }; 

            if (name.Contains("Bug") || name.Contains("Fly")) 
                return new Bug(position, 0); 
                
            if (name.Contains("Grub")) 
                return new Grub(position, true); 

            if (name.Contains("Ghost")) 
                return new Ghost(position);
            
            if (name.Contains("Skeleton")) 
                return new Skeleton(position);

            if (name.Contains("Crab")) 
            {
                var crab = new RockCrab(position); 
                crab.Name = name;
                return crab;
            }
            
            if (name.Contains("Golem") || name.Contains("Stone"))
                return new RockGolem(position);

            if (name.Contains("Shadow") || name.Contains("Brute"))
                return new ShadowBrute(position);

            if (name.Contains("Shaman"))
                return new ShadowShaman(position);
            
            if (name.Contains("Serpent"))
                return new Serpent(position);

            var fallback = new GreenSlime(position, 0);
            fallback.Name = name;
            return fallback;
        }

        private Vector2 GetRandomSpawnSpot(GameLocation location)
        {
            int attempts = 0;
            int mapWidth = location.Map.Layers[0].LayerWidth;
            int mapHeight = location.Map.Layers[0].LayerHeight;

            while (attempts < 20)
            {
                int x = Game1.random.Next(0, mapWidth);
                int y = Game1.random.Next(0, mapHeight);
                Vector2 tile = new Vector2(x, y);

                // Verificación robusta personalizada
                if (IsTileValidForSpawn(location, tile))
                {
                    return tile;
                }
                attempts++;
            }
            return Vector2.Zero;
        }

        /// <summary>
        /// Comprobación manual segura para Stardew Valley 1.6.
        /// Reemplaza métodos obsoletos como IsTileOccupied.
        /// </summary>
        private bool IsTileValidForSpawn(GameLocation location, Vector2 tile)
        {
            // 1. Límites del mapa
            if (!location.isTileOnMap(tile)) return false;

            // 2. Agua
            if (location.isWaterTile((int)tile.X, (int)tile.Y)) return false;

            // 3. Ocupación Manual (Reemplaza IsTileOccupied)
            // Objetos (Cofres, Máquinas)
            if (location.Objects.ContainsKey(tile)) return false;
            // Arbustos
            if (location.getLargeTerrainFeatureAt((int)tile.X, (int)tile.Y) != null) return false;
            // Jugadores
            if (location.isTileOccupiedByFarmer(tile) != null) return false;

            // 4. Colisiones (Muros, Acantilados)
            Rectangle tileRect = new Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64);
            
            // Firma de 1.6: Requiere el argumento 'character' (null en este caso)
            if (location.isCollidingPosition(tileRect, Game1.viewport, false, 0, false, null)) return false;

            return true;
        }

        public void UpdateMigratingMonsters()
        {
            if (!IsInvasionActive) return;
            if (Game1.currentLocation == null || Game1.currentLocation.Name != "Town") return;

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
            // Si el jugador está cerca, atacar al jugador (prioridad máxima)
            if (Vector2.Distance(monster.Position, Game1.player.Position) < 500f)
            {
                if (!monster.focusedOnFarmers) monster.focusedOnFarmers = true;
                return;
            }

            // Si no, ir al Rally Point (Saloon)
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