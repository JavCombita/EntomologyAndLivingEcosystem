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

        // Cache para no leer el diccionario cada frame
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

            // Chance diario (10% + 1% por cada nivel de combate del jugador)
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
            Game1.playSound("shadowpeep"); // Sonido espeluznante

            // Definir punto de reunión (Saloon)
            Point saloonDoor = town.doors.Keys.FirstOrDefault(d => town.doors[d] == "Saloon");
            if (saloonDoor != Point.Zero)
            {
                InvasionRallyPoint = new Vector2(saloonDoor.X, saloonDoor.Y);
            }

            // Calcular cantidad de monstruos (Base 5 + Nivel de Combate)
            int monsterCount = Game1.random.Next(5, 8 + Game1.player.CombatLevel); 

            // Refrescar lista de monstruos disponibles según dificultad actual
            RefreshSpawnableMonsters();

            for (int i = 0; i < monsterCount; i++)
            {
                SpawnRandomMonster(town);
            }
        }

        /// <summary>
        /// Lee Data/Monsters y filtra según el nivel del jugador.
        /// </summary>
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

                // Parsing básico
                int hp = int.Parse(fields[0]);
                int damage = int.Parse(fields[1]);

                // Cálculo de dificultad del monstruo (HP / 10 + Daño)
                int difficultyScore = (hp / 20) + damage;
                
                // LÓGICA DE FILTRADO:
                // 1. El monstruo no debe ser IMPOSIBLE (Dificultad > Nivel Jugador * 15)
                // 2. El monstruo no debe ser TRIVIAL si el jugador es nivel alto (Opcional, para evitar solo slimes verdes en late game)
                
                int maxDifficultyAllowed = (playerLevel + 1) * 20; // Ej: Nivel 10 * 20 = 200 Score (Soporta Iridium Bats)

                if (difficultyScore <= maxDifficultyAllowed)
                {
                    // PONDERACIÓN (Weighted Probability):
                    // Agregamos el monstruo a la lista múltiples veces según su dificultad.
                    // Si el jugador es nivel alto, queremos MÁS monstruos difíciles.
                    
                    int weight = 1;
                    if (difficultyScore > maxDifficultyAllowed / 2) weight = 3; // Alta probabilidad si es reto adecuado
                    if (difficultyScore < maxDifficultyAllowed / 10) weight = 1; // Baja probabilidad si es muy débil

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
            
            // Selección aleatoria de la lista ponderada
            string monsterName = CachedMonsterList[Game1.random.Next(CachedMonsterList.Count)];

            Monster monster = CreateMonsterFactory(monsterName, spawnTile);
            
            if (monster != null)
            {
                monster.focusedOnFarmers = true;
                location.characters.Add(monster);
                this.Mod.Monitor.Log($"Spawned {monster.Name} at Town.", LogLevel.Trace);
            }
        }

        /// <summary>
        /// La Fábrica: Convierte un nombre de string en una clase C# real.
        /// </summary>
        private Monster CreateMonsterFactory(string name, Vector2 tile)
        {
            Vector2 position = tile * 64f;

            // Manejo especial para variantes conocidas
            // Muchos mods solo agregan data a "Data/Monsters" pero usan estas clases base.
            
            if (name.Contains("Slime") || name.Contains("Jelly")) 
                return new GreenSlime(position, name); // GreenSlime constructor accepts name to load stats
            
            if (name.Contains("Bat") || name.Contains("Frost Bat") || name.Contains("Lava Bat")) 
                return new Bat(position, 0) { Name = name }; // 0 is mine level, defaults stats

            if (name.Contains("Bug") || name.Contains("Fly")) 
                return new Bug(position, 0); 
                
            if (name.Contains("Grub")) 
                return new Grub(position, true); // true = hard mode?

            if (name.Contains("Ghost")) 
                return new Ghost(position);
            
            if (name.Contains("Skeleton")) 
                return new Skeleton(position);

            if (name.Contains("Crab")) 
                return new RockCrab(position, name);
            
            if (name.Contains("Golem") || name.Contains("Stone"))
                return new RockGolem(position);

            if (name.Contains("Shadow") || name.Contains("Brute"))
                return new ShadowBrute(position);

            if (name.Contains("Shaman"))
                return new ShadowShaman(position);
            
            if (name.Contains("Serpent"))
                return new Serpent(position);

            // FALLBACK: Si es un mod con un nombre rarísimo (ej: "Void Eater"),
            // lo instanciamos como un GreenSlime pero le cargamos los stats del diccionario.
            // Es la forma más segura de que no crashee y tenga stats correctos.
            // O podemos usar Bat si queremos que vuele.
            return new GreenSlime(position, name);
        }

        private Vector2 GetRandomSpawnSpot(GameLocation location)
        {
            // (Mismo código de búsqueda de tiles que tenías antes)
            int attempts = 0;
            int mapWidth = location.Map.Layers[0].LayerWidth;
            int mapHeight = location.Map.Layers[0].LayerHeight;

            while (attempts < 20)
            {
                int x = Game1.random.Next(0, mapWidth);
                int y = Game1.random.Next(0, mapHeight);
                Vector2 tile = new Vector2(x, y);

                if (location.isTileLocationTotallyClearAndPlaceable(tile) && !location.isWaterTile(x, y))
                {
                    return tile;
                }
                attempts++;
            }
            return Vector2.Zero;
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
            // Lógica de Agresión Universal
            if (Vector2.Distance(monster.Position, Game1.player.Position) < 500f)
            {
                if (!monster.FocusedOnFarmers) monster.focusedOnFarmers = true;
                return;
            }

            // Pathfinding hacia el Saloon (Invasión)
            if (InvasionRallyPoint.HasValue)
            {
                Vector2 targetPixels = InvasionRallyPoint.Value * 64f;
                Vector2 trajectory = targetPixels - monster.Position;
                
                if (trajectory.Length() > 20f)
                {
                    trajectory.Normalize();
                    
                    // Velocidad adaptativa según el tipo
                    float speed = monster is Bat || monster is Ghost ? 3f : 1.5f; 
                    
                    // Empuje físico
                    monster.xVelocity = trajectory.X * speed;
                    monster.yVelocity = trajectory.Y * speed;

                    // Forzar animación de movimiento si es terrestre
                    if (monster is GreenSlime slime)
                    {
                        slime.faceDirection((int)trajectory.X > 0 ? 1 : 3);
                        // Los slimes saltan, así que les damos un empujón extra ocasional
                        if (Game1.random.NextDouble() < 0.05) slime.setTrajectory((int)(trajectory.X * 8), (int)(trajectory.Y * 8));
                    }
                }
            }
        }
    }
}