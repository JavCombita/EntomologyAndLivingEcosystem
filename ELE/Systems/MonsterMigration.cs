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

            // --- 1. CHEQUEO DE EVENTOS (NUEVO) ---
            // Si hay festival, evento (cinemática) o boda, cancelamos.
            if (Game1.isFestival() || Game1.eventUp || Game1.weddingToday) 
                return;
            // -------------------------------------

            if (Game1.stats.DaysPlayed < this.Mod.Config.DaysBeforeTownInvasion) return;

            // Probabilidad base ajustada por nivel
            double chance = 0.10 + (Game1.player.CombatLevel * 0.01);
            if (Game1.random.NextDouble() < chance)
            {
                TriggerTownInvasion();
            }
        }

        /// <summary>
        /// DEBUG COMMAND: Forces an invasion immediately in the Town.
        /// Called by 'ele_invasion' command.
        /// </summary>
        public void ForceTownInvasion()
        {
            // Opcional: Descomenta esto si quieres que el comando también respete los eventos
            // if (Game1.isFestival() || Game1.eventUp) { this.Mod.Monitor.Log("Cannot invade during event.", LogLevel.Warn); return; }

            GameLocation town = Game1.getLocationFromName("Town");
            if (town == null) return;

            this.Mod.Monitor.Log("⚔️ DEBUG: Forcing monster invasion in Town...", LogLevel.Warn);
            
            // Bypass probability checks and trigger directly
            TriggerTownInvasion();
        }

        private void TriggerTownInvasion()
        {
            GameLocation town = Game1.getLocationFromName("Town");
            if (town == null) return;

            IsInvasionActive = true;
            this.Mod.Monitor.Log("⚠️ INVASION STARTED: The horde is approaching!", LogLevel.Warn);
            
            Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.town_invasion"), 2));
            Game1.playSound("shadowpeep");

            // --- 2. EVACUAR ALDEANOS (RESTAURADO) ---
            EvacuateVillagers(town);
            // ----------------------------------------

            // Rally Point: Saloon Door
            Point saloonDoor = town.doors.Keys.FirstOrDefault(d => town.doors[d] == "Saloon");
            if (saloonDoor != Point.Zero)
            {
                InvasionRallyPoint = new Vector2(saloonDoor.X, saloonDoor.Y);
            }

            // --- DIFICULTAD DINÁMICA ---
            int combatLevel = Game1.player.CombatLevel;
            int minMonsters = 5;
            int maxMonsters = 10;
            
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
                    minMonsters = 5;
                    maxMonsters = 7 + (int)(combatLevel * 1.5f);
                    break;
            }

            int monsterCount = Game1.random.Next(minMonsters, maxMonsters + 1); 
            // ---------------------------

            RefreshSpawnableMonsters();

            int spawnedCount = 0;
            for (int i = 0; i < monsterCount; i++)
            {
                if (SpawnRandomMonster(town)) spawnedCount++;
            }
            this.Mod.Monitor.Log($"⚔️ Invasion Status: {spawnedCount}/{monsterCount} monsters spawned.", LogLevel.Info);
        }

        /// <summary>
        /// Mueve a los NPCs del Pueblo al Saloon para protegerlos.
        /// </summary>
        private void EvacuateVillagers(GameLocation town)
        {
            GameLocation saloon = Game1.getLocationFromName("Saloon");
            if (saloon == null) return;

            this.Mod.Monitor.Log("📢 Evacuating villagers to the Saloon...", LogLevel.Info);

            // Copiamos la lista para iterar seguros
            var charactersInTown = town.characters.ToList();

            foreach (NPC npc in charactersInTown)
            {
                // No mover monstruos, caballos o mascotas
                if (npc is Monster || npc is Horse || npc is Pet) continue;

                // Teletransportar al Saloon (Coordenadas aproximadas 15,18 con variación)
                Game1.warpCharacter(npc, "Saloon", new Vector2(15 + Game1.random.Next(-3, 3), 18 + Game1.random.Next(-2, 2)));
                
                // Resetear diálogo y mostrar alerta
                npc.CurrentDialogue.Clear();
                npc.showTextAboveHead("!"); 
            }
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

                int hp = int.Parse(fields[0]);
                int damage = int.Parse(fields[1]);
                
                // Filtro de dificultad
                int difficultyScore = (hp / 20) + damage;
                int maxDifficultyAllowed = (playerLevel + 1) * 25; // Le subí un poco el margen (de 20 a 25) para más variedad

                if (difficultyScore <= maxDifficultyAllowed)
                {
                    int weight = 1;
                    if (difficultyScore > maxDifficultyAllowed / 2) weight = 3; 
                    if (difficultyScore < maxDifficultyAllowed / 10) weight = 1;

                    for(int w=0; w < weight; w++)
                    {
                        CachedMonsterList.Add(name);
                    }
                }
            }

            // Fallback si la lista queda vacía
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
                this.Mod.Monitor.Log($"Spawned {monster.Name} at Town {spawnTile}.", LogLevel.Trace);
                return true;
            }
            return false;
        }

        private Monster CreateMonsterFactory(string name, Vector2 tile)
        {
            Vector2 position = tile * 64f;
            
            // 1. INTENTO POR NOMBRE (Lógica Vanilla)
            if (name.Contains("Slime") || name.Contains("Jelly")) 
            {
                var slime = new GreenSlime(position, 0); slime.Name = name; return slime;
            }
            if (name.Contains("Bat") || name.Contains("Frost Bat") || name.Contains("Lava Bat")) 
                return new Bat(position, 0) { Name = name }; 
            if (name.Contains("Bug") || name.Contains("Fly")) return new Bug(position, 0); 
            if (name.Contains("Grub")) return new Grub(position, true); 
            if (name.Contains("Ghost")) return new Ghost(position);
            if (name.Contains("Skeleton")) return new Skeleton(position);
            if (name.Contains("Crab")) { var crab = new RockCrab(position); crab.Name = name; return crab; }
            if (name.Contains("Golem") || name.Contains("Stone")) return new RockGolem(position);
            if (name.Contains("Shadow") || name.Contains("Brute")) return new ShadowBrute(position);
            if (name.Contains("Shaman")) return new ShadowShaman(position);
            if (name.Contains("Serpent")) return new Serpent(position);
            if (name.Contains("Dust")) return new DustSpirit(position);

            // 2. INTENTO POR DATOS (Smart Fallback para Mods)
            if (MonsterDataCache != null && MonsterDataCache.TryGetValue(name, out string rawData))
            {
                string[] fields = rawData.Split('/');
                // El campo 4 es "isGlider"
                if (fields.Length > 4 && bool.TryParse(fields[4], out bool isGlider) && isGlider)
                    return new Bat(position, 0) { Name = name };
                else
                    return new RockGolem(position) { Name = name }; // RockGolem es bueno persiguiendo
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

            while (attempts < 50) // Subimos intentos a 50
            {
                int x = Game1.random.Next(0, mapWidth);
                int y = Game1.random.Next(0, mapHeight);
                Vector2 tile = new Vector2(x, y);

                if (IsTileValidForSpawn(location, tile))
                {
                    return tile;
                }
                attempts++;
            }
            return Vector2.Zero;
        }

        private bool IsTileValidForSpawn(GameLocation location, Vector2 tile)
        {
            // --- 3. CORRECCIÓN DE SPAWN ---
            // Usamos este método nativo que es mucho más fiable que calcular colisiones manuales
            if (!location.isTileLocationTotallyClearAndPlaceable(tile)) return false;

            if (location.isWaterTile((int)tile.X, (int)tile.Y)) return false;
            if (location.isTileOccupiedByFarmer(tile) != null) return false;

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
            // Atacar al jugador si está cerca
            if (Vector2.Distance(monster.Position, Game1.player.Position) < 500f)
            {
                if (!monster.focusedOnFarmers) monster.focusedOnFarmers = true;
                return;
            }

            // Ir al Rally Point (Saloon)
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
