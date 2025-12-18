using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events; // Necesario para TimeChangedEventArgs
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
            
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int x = Game1.random.Next(0, farm.Map.Layers[0].LayerWidth);
                int y = Game1.random.Next(0, farm.Map.Layers[0].LayerHeight);
                
                if (Game1.random.NextDouble() < 0.5) 
                    x = (Game1.random.NextDouble() < 0.5) ? Game1.random.Next(0, 5) : Game1.random.Next(farm.Map.Layers[0].LayerWidth - 5, farm.Map.Layers[0].LayerWidth);
                else
                    y = (Game1.random.NextDouble() < 0.5) ? Game1.random.Next(0, 5) : Game1.random.Next(farm.Map.Layers[0].LayerHeight - 5, farm.Map.Layers[0].LayerHeight);

                spawnTile = new Vector2(x, y);
                
                // Corrección: PascalCase para Stardew 1.6
                if (farm.IsTileLocationTotallyClearAndPlaceable(spawnTile))
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
            
            // Corrección: Usar NewTime en lugar de Time
            if (monsterCount == 0 && e.NewTime > 630) 
            {
                IsInvasionActive = false;
                Game1.addHUDMessage(new HUDMessage(this.Mod.Helper.Translation.Get("notification.invasion_cleared"), 1));
                return;
            }

            if (FarmHouseDoor.HasValue)
            {
                foreach(var npc in farm.characters)
                {
                    if (npc is Monster monster)
                    {
                        if (Vector2.Distance(monster.Position, Game1.player.Position) > 600f)
                        {
                            // Corrección: PascalCase para Stardew 1.6
                           monster.SetMovingTowardPoint(FarmHouseDoor.Value);
                        }
                    }
                }
            }
        }
    }
}
