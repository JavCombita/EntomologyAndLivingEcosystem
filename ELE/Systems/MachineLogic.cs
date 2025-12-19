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
        
        // IDs Base (Sin calificar)
        private const string SpreaderBase = "JavCombita.ELE_NutrientSpreader";
        private const string SpreaderMk2  = "JavCombita.ELE_NutrientSpreader_Mk2";
        private const string SpreaderMk3  = "JavCombita.ELE_NutrientSpreader_Mk3";
        private const string SpreaderOmega= "JavCombita.ELE_NutrientSpreader_Omega";

        // IDs Items Base
        private const string BoostN = "JavCombita.ELE_Fertilizer_N";
        private const string BoostP = "JavCombita.ELE_Fertilizer_P";
        private const string BoostK = "JavCombita.ELE_Fertilizer_K";
        private const string BoostOmni = "JavCombita.ELE_Fertilizer_Omni";
        
        // IDs Vanilla (Sin calificar para comparaciones flexibles)
        private const string MilkId = "184";
        private const string LargeMilkId = "186";

        public MachineLogic(ModEntry mod)
        {
            this.Mod = mod;
            mod.Helper.Events.GameLoop.TimeChanged += OnTimeChanged;
        }

        public void HandleInteraction(ButtonPressedEventArgs e)
        {
            // Permitir clic derecho (PC) o clic izquierdo (Android Tap / PC Action)
            if (!e.Button.IsActionButton() && e.Button != SButton.MouseLeft) return;

            Vector2 tile = e.Cursor.Tile;
            GameLocation location = Game1.currentLocation;

            // Log para debuggear coordenadas y objetos
            // Mod.Monitor.Log($"[ELE Debug] Click at {tile}. Object here? {location.objects.ContainsKey(tile)}", LogLevel.Trace);

            if (location.objects.TryGetValue(tile, out StardewValley.Object machine))
            {
                if (IsSpreader(machine.ItemId))
                {
                    HandleSpreaderClick(machine, e);
                }
            }
        }

        private void HandleSpreaderClick(StardewValley.Object machine, ButtonPressedEventArgs e)
        {
            // 1. Evitar interacción si se usa herramienta de remoción
            if (Game1.player.CurrentTool != null && Game1.player.CurrentTool is StardewValley.Tools.Pickaxe or StardewValley.Tools.Axe) return;

            // 2. Si está ocupada
            if (machine.modData.ContainsKey("ele_finish_time")) {
                Game1.drawObjectDialogue(Mod.Helper.Translation.Get("machine.busy"));
                Mod.Helper.Input.Suppress(e.Button);
                return;
            }

            // 3. Check Item en mano
            Item held = Game1.player.CurrentItem;
            if (held == null) {
                // Mensaje informativo si mano vacía
                // Game1.drawObjectDialogue("Hold a Booster + Milk in inventory.");
                return; 
            }

            // Debug del item
            // Mod.Monitor.Log($"[ELE Debug] Held item: {held.ItemId} ({held.Name})", LogLevel.Trace);

            if (!IsBooster(held.ItemId)) {
                Game1.drawObjectDialogue(Mod.Helper.Translation.Get("machine.invalid_input"));
                Mod.Helper.Input.Suppress(e.Button);
                return;
            }

            // 4. Procesar Input
            AttemptToStart(machine, held);
            Mod.Helper.Input.Suppress(e.Button);
        }

        private void AttemptToStart(StardewValley.Object machine, Item heldBooster)
        {
            // Calcular Costos
            CalculateCosts(machine.ItemId, heldBooster.ItemId, out int bCost, out int mCost, out int lmCost);

            // Verificar Booster en mano
            if (heldBooster.Stack < bCost) {
                ShowMissingMessage(bCost, mCost, lmCost, heldBooster.ItemId);
                return;
            }

            // Verificar Leche en INVENTARIO (Búsqueda robusta por ID)
            Item milkItem = FindItemInInventory(LargeMilkId, lmCost);
            bool useLarge = (milkItem != null);

            if (!useLarge) {
                milkItem = FindItemInInventory(MilkId, mCost);
                if (milkItem == null) {
                    ShowMissingMessage(bCost, mCost, lmCost, heldBooster.ItemId);
                    return;
                }
            }

            // CONSUMIR
            // Reducir booster de la mano
            heldBooster.Stack -= bCost;
            if (heldBooster.Stack <= 0) Game1.player.Items[Game1.player.CurrentToolIndex] = null;

            // Reducir leche del inventario
            if (useLarge) ConsumeItem(milkItem, lmCost);
            else ConsumeItem(milkItem, mCost);

            // INICIAR
            int finishTime = Utility.ModifyTime(Game1.timeOfDay, 30); // +30 mins
            machine.modData["ele_finish_time"] = finishTime.ToString();
            machine.modData["ele_processing_type"] = heldBooster.ItemId;
            
            machine.showNextIndex.Value = true; 
            machine.shakeTimer = 500; 
            Game1.playSound("Ship");
            Game1.drawObjectDialogue(Mod.Helper.Translation.Get("machine.started"));
        }

        // Helper para buscar items ignorando prefijos (O)
        private Item FindItemInInventory(string partialId, int count)
        {
            foreach (var item in Game1.player.Items)
            {
                if (item != null && item.ItemId.Contains(partialId) && item.Stack >= count)
                    return item;
            }
            return null;
        }

        private void ConsumeItem(Item item, int count)
        {
            item.Stack -= count;
            if (item.Stack <= 0) Game1.player.Items.Remove(item);
        }

        private void ShowMissingMessage(int bCost, int mCost, int lmCost, string bId)
        {
            string bName = new StardewValley.Object(bId, 1).DisplayName;
            string msg = Mod.Helper.Translation.Get("machine.missing_resources", new { 
                bCount = bCost, 
                bName = bName, 
                mCount = mCost, 
                lmCount = lmCost 
            });
            Game1.drawObjectDialogue(msg);
        }

        private void CalculateCosts(string machineId, string boosterId, out int bCost, out int mCost, out int lmCost)
        {
            // Si es Omni (contiene el ID de Omni)
            if (boosterId.Contains(BoostOmni)) { bCost=1; mCost=999; lmCost=1; return; }

            // Comparación de IDs de máquina (Flexible)
            if (machineId.Contains(SpreaderOmega)) { bCost=14; mCost=14; lmCost=2; return; }
            if (machineId.Contains(SpreaderMk3))   { bCost=9; mCost=9; lmCost=2; return; }
            if (machineId.Contains(SpreaderMk2))   { bCost=7; mCost=8; lmCost=1; return; }
            
            // Base (Default)
            bCost=5; mCost=4; lmCost=1; 
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
                if(time >= finish || (finish > 2400 && time < 600)) {
                    string type = machine.modData["ele_processing_type"];
                    int radius = GetRadius(machine.ItemId);
                    ApplyArea(loc, tile, type, radius);
                    
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

        // Helpers de ID flexibles
        private bool IsSpreader(string id) => id != null && id.Contains("JavCombita.ELE_NutrientSpreader");
        private bool IsBooster(string id) => id != null && id.Contains("JavCombita.ELE_Fertilizer");
        
        private int GetRadius(string id) {
            if(id.Contains("Omega")) return 7;
            if(id.Contains("Mk3")) return 4;
            if(id.Contains("Mk2")) return 3;
            return 2;
        }
    }
}