using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;

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
            // Procesar máquinas en todas las ubicaciones
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
            // Buscar cofre adyacente (Arriba) que contenga fertilizante
            Vector2 chestTile = tile + new Vector2(0, -1);
            if (location.objects.TryGetValue(chestTile, out StardewValley.Object obj) && obj is Chest chest)
            {
                var inventory = chest.GetItemsForPlayer(Game1.player.UniqueMultiplayerID);
                
                // Buscar fertilizante compatible
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
                    // Consumir 1 unidad
                    fertilizer.Stack--;
                    if (fertilizer.Stack <= 0) chest.Items.Remove(fertilizer);

                    // Aplicar efecto en área (Radio 3 -> 7x7)
                    ApplyFertilizerArea(location, tile, fertilizer.ItemId, 3);
                    
                    // FX
                    Game1.playSound("sandPolisher");
                    // Animación breve de la máquina
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
                    if (location.terrainFeatures.TryGetValue(target, out TerrainFeature tf) && tf is StardewValley.TerrainFeatures.HoeDirt)
                    {
                        // Restaurar nutrientes usando el sistema existente
                        Mod.Ecosystem.RestoreNutrients(location, target, fertilizerId);
                    }
                }
            }
        }
    }
}