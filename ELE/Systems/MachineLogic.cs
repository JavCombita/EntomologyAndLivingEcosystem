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
        
        // IDs Parciales (Para coincidencia flexible con .Contains)
        private const string SpreaderBase = "JavCombita.ELE_NutrientSpreader";
        private const string SpreaderMk2  = "Mk2"; // Parte única del ID
        private const string SpreaderMk3  = "Mk3";
        private const string SpreaderOmega= "Omega";

        // IDs Boosters
        private const string BoostOmni = "JavCombita.ELE_Fertilizer_Omni";
        private const string BoostTag = "JavCombita.ELE_Fertilizer"; // Parte común

        // IDs Leche (Vanilla) - Buscamos por el número para evitar problemas con (O)
        private const string MilkId = "184";
        private const string LargeMilkId = "186";

        public MachineLogic(ModEntry mod)
        {
            this.Mod = mod;
            // Solo nos suscribimos al TimeChanged aquí, el input viene de ModEntry
            mod.Helper.Events.GameLoop.TimeChanged += OnTimeChanged;
        }

        public void HandleInteraction(ButtonPressedEventArgs e)
        {
            // Permitir clic derecho (PC Acción) o clic izquierdo (Android Tap)
            if (!e.Button.IsActionButton() && e.Button != SButton.MouseLeft) return;

            Vector2 tile = e.Cursor.Tile;
            GameLocation location = Game1.currentLocation;

            // Intentar obtener objeto en el tile
            if (location.objects.TryGetValue(tile, out StardewValley.Object machine))
            {
                // Validación Flexible: ¿El ID contiene la base de nuestro mod?
                if (machine.ItemId != null && machine.ItemId.Contains("JavCombita.ELE_NutrientSpreader"))
                {
                    Mod.Monitor.Log($"[ELE Debug] Interaction detected with Machine: {machine.ItemId} at {tile}", LogLevel.Trace);
                    HandleSpreaderClick(machine, e.Button);
                }
            }
        }

        private void HandleSpreaderClick(StardewValley.Object machine, SButton button)
        {
            // 1. Evitar interacción si se usa herramienta de remoción (Pico/Hacha)
            if (Game1.player.CurrentTool != null && Game1.player.CurrentTool is StardewValley.Tools.Pickaxe or StardewValley.Tools.Axe) 
            {
                Mod.Monitor.Log("[ELE Debug] Interaction skipped: Player holding removal tool.", LogLevel.Trace);
                return;
            }

            // 2. Verificar si está ocupada
            if (machine.modData.ContainsKey("ele_finish_time")) 
            {
                Game1.drawObjectDialogue(Mod.Helper.Translation.Get("machine.busy"));
                Mod.Helper.Input.Suppress(button);
                return;
            }

            // 3. Validar Item en Mano
            Item held = Game1.player.CurrentItem;
            
            if (held == null) 
            {
                Mod.Monitor.Log("[ELE Debug] Interaction skipped: Empty hand.", LogLevel.Trace);
                return; // Mano vacía, permitir inspección normal o no hacer nada
            }

            Mod.Monitor.Log($"[ELE Debug] Player holding item: {held.ItemId} (Stack: {held.Stack})", LogLevel.Trace);

            // Detección flexible de Booster
            if (!held.ItemId.Contains(BoostTag)) 
            {
                Game1.drawObjectDialogue(Mod.Helper.Translation.Get("machine.invalid_input"));
                Mod.Monitor.Log($"[ELE Debug] Invalid input. Item '{held.ItemId}' does not contain '{BoostTag}'", LogLevel.Warn);
                Mod.Helper.Input.Suppress(button);
                return;
            }

            // 4. Procesar Input
            AttemptToStart(machine, held, button);
        }

        private void AttemptToStart(StardewValley.Object machine, Item heldBooster, SButton button)
        {
            CalculateCosts(machine.ItemId, heldBooster.ItemId, out int bCost, out int mCost, out int lmCost);

            Mod.Monitor.Log($"[ELE Debug] Costs calculated -> Booster: {bCost}, Milk: {mCost}, L.Milk: {lmCost}", LogLevel.Trace);

            // Validar Stack Booster
            if (heldBooster.Stack < bCost) 
            {
                ShowMissingMessage(bCost, mCost, lmCost, heldBooster.ItemId);
                Mod.Monitor.Log($"[ELE Debug] Missing Boosters. Has {heldBooster.Stack}, Needs {bCost}", LogLevel.Info);
                Mod.Helper.Input.Suppress(button);
                return;
            }

            // Validar Leche (Inventario)
            // Búsqueda flexible: que contenga el ID (para que sirva "186" y "(O)186")
            Item milkItem = FindItemInInventory(LargeMilkId, lmCost);
            bool useLarge = (milkItem != null);

            if (!useLarge) 
            {
                // Si no es Omni (que bloquea leche pequeña con costo 999), buscamos leche normal
                if (mCost < 999) 
                    milkItem = FindItemInInventory(MilkId, mCost);
                
                if (milkItem == null) 
                {
                    ShowMissingMessage(bCost, mCost, lmCost, heldBooster.ItemId);
                    Mod.Monitor.Log("[ELE Debug] Missing Milk in inventory.", LogLevel.Info);
                    Mod.Helper.Input.Suppress(button);
                    return;
                }
            }

            Mod.Monitor.Log("[ELE Debug] Requirements met. Consuming items...", LogLevel.Info);

            // CONSUMIR
            heldBooster.Stack -= bCost;
            if (heldBooster.Stack <= 0) 
                Game1.player.Items[Game1.player.CurrentToolIndex] = null;

            if (useLarge) ConsumeItem(milkItem, lmCost);
            else ConsumeItem(milkItem, mCost);

            // INICIAR
            int finishTime = Utility.ModifyTime(Game1.timeOfDay, 30);
            machine.modData["ele_finish_time"] = finishTime.ToString();
            machine.modData["ele_processing_type"] = heldBooster.ItemId;
            
            machine.showNextIndex.Value = true; 
            machine.shakeTimer = 500; 
            
            Game1.playSound("Ship");
            Game1.drawObjectDialogue(Mod.Helper.Translation.Get("machine.started"));
            
            Mod.Helper.Input.Suppress(button); // Importante para no comer/usar el item sobrante
        }

        private Item FindItemInInventory(string partialId, int count)
        {
            // Busca cualquier item cuyo ID contenga el número (ej: "186" en "(O)186")
            return Game1.player.Items.FirstOrDefault(i => i != null && i.ItemId.Contains(partialId) && i.Stack >= count);
        }

        private void ConsumeItem(Item item, int count)
        {
            if (item == null) return;
            item.Stack -= count;
            if (item.Stack <= 0) Game1.player.Items.Remove(item);
        }

        private void ShowMissingMessage(int bCost, int mCost, int lmCost, string bId)
        {
            string bName = new StardewValley.Object(bId, 1).DisplayName;
            string msg = (mCost >= 999) 
                ? Mod.Helper.Translation.Get("machine.missing_resources_omni", new { bCount = bCost, bName = bName, lmCount = lmCost })
                : Mod.Helper.Translation.Get("machine.missing_resources", new { bCount = bCost, bName = bName, mCount = mCost, lmCount = lmCost });
            
            Game1.drawObjectDialogue(msg);
        }

        private void CalculateCosts(string machineId, string boosterId, out int bCost, out int mCost, out int lmCost)
        {
            if (boosterId.Contains(BoostOmni)) { bCost=2; mCost=999; lmCost=2; return; }

            // Detección por sub-string para ignorar prefijos de máquina
            if (machineId.Contains(SpreaderOmega)) { bCost=14; mCost=14; lmCost=2; return; }
            if (machineId.Contains(SpreaderMk3))   { bCost=9; mCost=9; lmCost=2; return; }
            if (machineId.Contains(SpreaderMk2))   { bCost=7; mCost=8; lmCost=1; return; }
            
            bCost=5; mCost=4; lmCost=1; // Base
        }

        private void OnTimeChanged(object sender, TimeChangedEventArgs e)
        {
            foreach(var loc in Game1.locations) {
                if(!loc.IsFarm && !loc.Name.Contains("Greenhouse")) continue;
                foreach(var pair in loc.objects.Pairs) {
                    // Chequeo ligero en update loop
                    if(pair.Value.ItemId.Contains(SpreaderBase)) 
                        ProcessTick(loc, pair.Key, pair.Value, e.NewTime);
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
                    Mod.Monitor.Log($"[ELE] Machine at {tile} finished processing. Radius: {radius}", LogLevel.Trace);
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

        private int GetRadius(string id) {
            if(id.Contains("Omega")) return 7;
            if(id.Contains("Mk3")) return 4;
            if(id.Contains("Mk2")) return 3;
            return 2;
        }
    }
}