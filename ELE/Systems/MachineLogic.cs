using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures; 

namespace ELE.Core.Systems
{
    public class MachineLogic
    {
        private readonly ModEntry Mod;
        
        // IDs Máquinas
        private const string SpreaderBase = "JavCombita.ELE_NutrientSpreader";
        private const string SpreaderMk2  = "JavCombita.ELE_NutrientSpreader_Mk2";
        private const string SpreaderMk3  = "JavCombita.ELE_NutrientSpreader_Mk3";
        private const string SpreaderOmega= "JavCombita.ELE_NutrientSpreader_Omega";

        // IDs Items Procesados (Salida Técnica)
        private const string ProcN = "JavCombita.ELE_Processed_N";
        private const string ProcP = "JavCombita.ELE_Processed_P";
        private const string ProcK = "JavCombita.ELE_Processed_K";
        private const string ProcOmni = "JavCombita.ELE_Processed_Omni";

        // IDs Boosters (Para aplicar)
        private const string BoostN = "JavCombita.ELE_Fertilizer_N";
        private const string BoostP = "JavCombita.ELE_Fertilizer_P";
        private const string BoostK = "JavCombita.ELE_Fertilizer_K";
        private const string BoostOmni = "JavCombita.ELE_Fertilizer_Omni";

        public MachineLogic(ModEntry mod)
        {
            this.Mod = mod;
            // Chequeo frecuente para detectar cuando termina
            mod.Helper.Events.GameLoop.OneSecondUpdateTicked += OnUpdate;
        }

        private void OnUpdate(object sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            foreach(var location in Game1.locations)
            {
                if (!location.IsFarm && !location.Name.Contains("Greenhouse")) continue;

                foreach(var pair in location.objects.Pairs)
                {
                    StardewValley.Object machine = pair.Value;
                    if (IsSpreader(machine.ItemId) && machine.heldObject.Value != null && machine.readyForHarvest.Value)
                    {
                        ProcessFinishedMachine(location, pair.Key, machine);
                    }
                }
            }
        }

        private void ProcessFinishedMachine(GameLocation location, Vector2 tile, StardewValley.Object machine)
        {
            string outputId = machine.heldObject.Value.ItemId;
            string fertilizerToApply = null;

            // Determinar qué aplicar según el output
            switch (outputId)
            {
                case ProcN: fertilizerToApply = BoostN; break;
                case ProcP: fertilizerToApply = BoostP; break;
                case ProcK: fertilizerToApply = BoostK; break;
                case ProcOmni: fertilizerToApply = BoostOmni; break;
                default: return; // No es nuestro item procesado
            }

            if (fertilizerToApply != null)
            {
                int radius = GetRadius(machine.ItemId);
                ApplyFertilizerArea(location, tile, fertilizerToApply, radius);
                
                // Efectos visuales
                Game1.createRadialDebris(location, 12, (int)tile.X, (int)tile.Y, 6, false);
                location.playSound("coin");

                // Resetear máquina automáticamente (Simula aplicación automática)
                machine.heldObject.Value = null;
                machine.readyForHarvest.Value = false;
                machine.minutesUntilReady.Value = -1;
                machine.showNextIndex.Value = false;
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
