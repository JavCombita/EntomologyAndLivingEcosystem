using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures; 

namespace ELE.Core.Systems
{
    public class MachineLogic
    {
        private readonly ModEntry Mod;
        private const string SpreaderBase = "JavCombita.ELE_NutrientSpreader";
        private const string SpreaderMk2  = "JavCombita.ELE_NutrientSpreader_Mk2";
        private const string SpreaderMk3  = "JavCombita.ELE_NutrientSpreader_Mk3";
        private const string SpreaderOmega= "JavCombita.ELE_NutrientSpreader_Omega";

        public MachineLogic(ModEntry mod)
        {
            this.Mod = mod;
            mod.Helper.Events.GameLoop.TimeChanged += OnTimeChanged;
        }

        private void OnTimeChanged(object sender, StardewModdingAPI.Events.TimeChangedEventArgs e)
        {
            if (e.NewTime != 700) return;

            foreach(var location in Game1.locations)
            {
                if (!location.IsFarm && !location.Name.Contains("Greenhouse")) continue;

                foreach(var pair in location.objects.Pairs)
                {
                    if (IsSpreader(pair.Value.ItemId))
                    {
                        ProcessSpreader(location, pair.Key, pair.Value);
                    }
                }
            }
        }

        private bool IsSpreader(string id)
        {
            return id == SpreaderBase || id == SpreaderMk2 || id == SpreaderMk3 || id == SpreaderOmega;
        }

        private int GetRadius(string id)
        {
            switch (id)
            {
                case SpreaderBase: return 2; // 5x5
                case SpreaderMk2:  return 3; // 7x7
                case SpreaderMk3:  return 4; // 9x9
                case SpreaderOmega:return 7; // 15x15
                default: return 2;
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
                    if (item != null && (item.Category == StardewValley.Object.fertilizerCategory || item.Name.Contains("Fertilizer") || item.Name.Contains("Booster")))
                    {
                        fertilizer = item;
                        break;
                    }
                }

                if (fertilizer != null)
                {
                    fertilizer.Stack--;
                    if (fertilizer.Stack <= 0) chest.Items.Remove(fertilizer);

                    int radius = GetRadius(machine.ItemId);
                    ApplyFertilizerArea(location, tile, fertilizer.ItemId, radius);
                    
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
