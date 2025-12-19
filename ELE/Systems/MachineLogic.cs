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
        
        // IDs
        private const string SpreaderBase = "JavCombita.ELE_NutrientSpreader";
        private const string SpreaderMk2  = "JavCombita.ELE_NutrientSpreader_Mk2";
        private const string SpreaderMk3  = "JavCombita.ELE_NutrientSpreader_Mk3";
        private const string SpreaderOmega= "JavCombita.ELE_NutrientSpreader_Omega";

        // IDs Items
        private const string BoostN = "JavCombita.ELE_Fertilizer_N";
        private const string BoostP = "JavCombita.ELE_Fertilizer_P";
        private const string BoostK = "JavCombita.ELE_Fertilizer_K";
        private const string BoostOmni = "JavCombita.ELE_Fertilizer_Omni";
        private const string Milk = "(O)184";
        private const string LargeMilk = "(O)186";

        public MachineLogic(ModEntry mod)
        {
            this.Mod = mod;
            mod.Helper.Events.GameLoop.TimeChanged += OnTimeChanged;
        }

        public void HandleInteraction(ButtonPressedEventArgs e)
        {
            // Solo actuar en interacción principal (Botón derecho / Toque)
            if (!e.Button.IsActionButton()) return;

            Vector2 tile = e.Cursor.Tile;
            GameLocation location = Game1.currentLocation;

            if (location.objects.TryGetValue(tile, out StardewValley.Object machine) && IsSpreader(machine.ItemId))
            {
                // 1. Si está ocupada
                if (machine.modData.ContainsKey("ele_finish_time")) {
                    Game1.drawObjectDialogue(Mod.Helper.Translation.Get("machine.busy"));
                    Mod.Helper.Input.Suppress(e.Button);
                    return;
                }

                // 2. Check Item en mano
                Item held = Game1.player.CurrentItem;
                if (held == null) return; 

                if (!IsBooster(held.ItemId)) {
                    Game1.drawObjectDialogue(Mod.Helper.Translation.Get("machine.invalid_input"));
                    Mod.Helper.Input.Suppress(e.Button);
                    return;
                }

                // 3. Procesar Input
                AttemptToStart(machine, held);
                Mod.Helper.Input.Suppress(e.Button);
            }
        }

        private void AttemptToStart(StardewValley.Object machine, Item heldBooster)
        {
            // Calcular Costos
            CalculateCosts(machine.ItemId, heldBooster.ItemId, out int bCost, out int mCost, out int lmCost);

            // Verificar Booster
            if (heldBooster.Stack < bCost) {
                ShowMissingMessage(bCost, mCost, lmCost, heldBooster.ItemId);
                return;
            }

            // Verificar Leche (Prioriza Large)
            Item milkItem = Game1.player.Items.FirstOrDefault(i => i?.ItemId == LargeMilk && i.Stack >= lmCost);
            bool useLarge = (milkItem != null);

            if (!useLarge) {
                // Si mCost es el "candado" (999), no buscamos leche pequeña, fallamos directo.
                // Si es normal, buscamos leche pequeña.
                if (mCost < 999) 
                {
                    milkItem = Game1.player.Items.FirstOrDefault(i => i?.ItemId == Milk && i.Stack >= mCost);
                }
                
                if (milkItem == null) {
                    ShowMissingMessage(bCost, mCost, lmCost, heldBooster.ItemId);
                    return;
                }
            }

            // CONSUMIR
            // CORRECCIÓN: Lógica manual de reducción de item en mano
            heldBooster.Stack -= bCost;
            if (heldBooster.Stack <= 0)
            {
                Game1.player.Items[Game1.player.CurrentToolIndex] = null;
            }

            if (useLarge) ConsumeInventoryItem(LargeMilk, lmCost);
            else ConsumeInventoryItem(Milk, mCost);

            // INICIAR
            int finishTime = Utility.ModifyTime(Game1.timeOfDay, 30); // +30 mins
            machine.modData["ele_finish_time"] = finishTime.ToString();
            machine.modData["ele_processing_type"] = heldBooster.ItemId;
            
            machine.showNextIndex.Value = true; // Activar animación visual (Next Frame)
            machine.shakeTimer = 500; // Wobble!
            Game1.playSound("Ship");
            Game1.drawObjectDialogue(Mod.Helper.Translation.Get("machine.started"));
        }

        private void ShowMissingMessage(int bCost, int mCost, int lmCost, string bId)
        {
            string bName = new StardewValley.Object(bId, 1).DisplayName;
            string msg;

            // Si mCost es el "candado" (999), mostramos el mensaje exclusivo para Omni/Large
            if (mCost >= 999)
            {
                msg = Mod.Helper.Translation.Get("machine.missing_resources_omni", new { 
                    bCount = bCost, 
                    bName = bName, 
                    lmCount = lmCost 
                });
            }
            else
            {
                msg = Mod.Helper.Translation.Get("machine.missing_resources", new { 
                    bCount = bCost, 
                    bName = bName, 
                    mCount = mCost, 
                    lmCount = lmCost 
                });
            }
            
            Game1.drawObjectDialogue(msg);
        }

        private void ConsumeInventoryItem(string id, int count)
        {
            var item = Game1.player.Items.FirstOrDefault(i => i?.ItemId == id && i.Stack >= count);
            if(item != null) {
                item.Stack -= count;
                if(item.Stack <= 0) Game1.player.Items.Remove(item);
            }
        }

        private void CalculateCosts(string machineId, string boosterId, out int bCost, out int mCost, out int lmCost)
        {
            // Omni siempre cuesta 1 + 1 Large. mCost=999 bloquea el uso de leche pequeña.
            if (boosterId == BoostOmni) { bCost=1; mCost=999; lmCost=1; return; }

            switch(machineId) {
                case SpreaderMk2: bCost=7; mCost=8; lmCost=1; break;
                case SpreaderMk3: bCost=9; mCost=9; lmCost=2; break;
                case SpreaderOmega: bCost=14; mCost=14; lmCost=2; break;
                default: bCost=5; mCost=4; lmCost=1; break;
            }
        }

        private void OnTimeChanged(object sender, TimeChangedEventArgs e)
        {
            foreach(var loc in Game1.locations) {
                if(!loc.IsFarm && !loc.Name.Contains("Greenhouse")) continue;
                foreach(var pair in loc.objects.Pairs) {
                    if(IsSpreader(pair.Value.ItemId)) ProcessTick(loc, pair.Key, pair.Value, e.NewTime);
                }
            }
        }

        private void ProcessTick(GameLocation loc, Vector2 tile, StardewValley.Object machine, int time)
        {
            if(machine.modData.TryGetValue("ele_finish_time", out string tStr)) {
                int finish = int.Parse(tStr);
                // Maneja cambio de día simple (si finish > 2400 y ahora es < 600)
                if(time >= finish || (finish > 2400 && time < 600)) {
                    // TERMINAR
                    string type = machine.modData["ele_processing_type"];
                    int radius = GetRadius(machine.ItemId);
                    ApplyArea(loc, tile, type, radius);
                    
                    // Reset
                    machine.modData.Remove("ele_finish_time");
                    machine.modData.Remove("ele_processing_type");
                    machine.showNextIndex.Value = false;
                    loc.playSound("coin");
                }
            }
        }

        private void ApplyArea(GameLocation loc, Vector2 center, string boostId, int r)
        {
            for(int x = -r; x <= r; x++) {
                for(int y = -r; y <= r; y++) {
                    Vector2 target = center + new Vector2(x, y);
                    if(loc.terrainFeatures.TryGetValue(target, out TerrainFeature tf) && tf is HoeDirt) {
                        Mod.Ecosystem.RestoreNutrients(loc, target, boostId);
                    }
                }
            }
        }

        private bool IsSpreader(string id) => id.StartsWith("JavCombita.ELE_NutrientSpreader");
        private bool IsBooster(string id) => id.StartsWith("JavCombita.ELE_Fertilizer");
        private int GetRadius(string id) {
            if(id == SpreaderOmega) return 7;
            if(id == SpreaderMk3) return 4;
            if(id == SpreaderMk2) return 3;
            return 2;
        }
    }
}