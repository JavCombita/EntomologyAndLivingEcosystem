using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures; // <--- Importante

namespace ELE.Core.Systems
{
    public class MachineLogic
    {
        private readonly ModEntry Mod;
        private const string SpreaderId = "JavCombita.ELE_NutrientSpreader";

        public MachineLogic(ModEntry mod)
        {
            this.Mod = mod;
            mod.Helper.Events.GameLoop.DayStarted += OnDayStarted;
        }

        private void OnDayStarted(object sender, StardewModdingAPI.Events.DayStartedEventArgs e)
        {
            foreach(var location in Game1.locations)
            {
                if (!location.IsFarm && !location.Name.Contains("Greenhouse")) continue;

                foreach(var pair in location.objects.Pairs)
                {
                    if (pair.Value.ItemId == SpreaderId)
                    {
                        ProcessSpreader(location, pair.Key, pair.Value);
                    }
                }
            }
        }

        private void ProcessSpreader(GameLocation location, Vector2 tile, StardewValley.Object machine)
        {
            Vector2 chestTile = tile + new Vector2(0, -1);
            if (location.objects.TryGetValue(chestTile, out StardewValley.Object obj) && obj is Chest chest)
            {
                var inventory = chest.GetItemsForPlayer(Game1.player.UniqueMultiplayerID);
                
                Item fertilizer = null;
                foreach(var item in inventory)
                {
                    if (item != null && (item.Category == StardewValley.Object.fertilizerCategory || item.Name.Contains("Fertilizer")))
                    {
                        fertilizer = item;
                        break;
                    }
                }

                if (fertilizer != null)
                {
                    fertilizer.Stack--;
                    if (fertilizer.Stack <= 0) chest.Items.Remove(fertilizer);

                    ApplyFertilizerArea(location, tile, fertilizer.ItemId, 3);
                    
                    Game1.playSound("sandPolisher");
                    machine.showNextIndex.Value = true; 
                }
            }
        }

        private void ApplyFertilizerArea(GameLocation location, Vector2 center, string fertilizerId, int radius)
        {
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    Vector2 target = center + new Vector2(x, y);
                    if (location.terrainFeatures.TryGetValue(target, out TerrainFeature tf) && tf is HoeDirt)
                    {
                        Mod.Ecosystem.RestoreNutrients(location, target, fertilizerId);
                    }
                }
            }
        }
    }
}
