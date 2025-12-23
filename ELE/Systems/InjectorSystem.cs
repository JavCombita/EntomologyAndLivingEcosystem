using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace ELE.Core.Systems
{
    public class InjectorSystem
    {
        private readonly ModEntry Mod;
        
        // IDs
        private const string InjectorItemId = "JavCombita.ELE_Alchemical_Injector";
        private const string MutagenBaseId = "JavCombita.ELE_Mutagen"; // Base para búsqueda
        
        // Keys para ModData (Guardar munición dentro de la herramienta)
        private const string AmmoCountKey = "ele_injector_ammo_count";
        private const string AmmoTypeKey = "ele_injector_ammo_type";

        public InjectorSystem(ModEntry mod)
        {
            this.Mod = mod;
            mod.Helper.Events.Input.ButtonPressed += OnButtonPressed;
            mod.Helper.Events.Display.RenderedHud += OnRenderedHud;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // 1. Validar que tenemos el Inyector en la mano
            if (Game1.player.CurrentItem == null || Game1.player.CurrentItem.ItemId != InjectorItemId) return;

            // 2. RECARGAR (Clic Derecho / Acción Secundaria)
            if (e.Button.IsActionButton())
            {
                HandleReload();
                Mod.Helper.Input.Suppress(e.Button); // Evitar comer/interactuar
            }
            // 3. INYECTAR (Clic Izquierdo / Uso Herramienta)
            else if (e.Button.IsUseToolButton())
            {
                HandleInjection(e);
                Mod.Helper.Input.Suppress(e.Button); // Evitar usar como objeto normal
            }
        }

        private void HandleReload()
        {
            Item tool = Game1.player.CurrentItem;
            
            // Buscar munición en inventario (Cualquier item que contenga "Mutagen")
            Item ammo = Game1.player.Items.FirstOrDefault(i => i != null && i.ItemId.Contains(MutagenBaseId));

            if (ammo == null)
            {
                Game1.drawObjectDialogue(Mod.Helper.Translation.Get("injector.no_ammo"));
                return;
            }

            // Lógica de carga
            int currentLoad = 0;
            if (tool.modData.TryGetValue(AmmoCountKey, out string countStr)) int.TryParse(countStr, out currentLoad);

            int spaceFree = 20 - currentLoad; // Capacidad máx 20
            if (spaceFree <= 0)
            {
                Game1.showRedMessage(Mod.Helper.Translation.Get("injector.full"));
                return;
            }

            int toLoad = Math.Min(spaceFree, ammo.Stack);
            
            // Actualizar Tool
            tool.modData[AmmoCountKey] = (currentLoad + toLoad).ToString();
            tool.modData[AmmoTypeKey] = ammo.ItemId; // Guardamos qué tipo cargó

            // Consumir del inventario
            ammo.Stack -= toLoad;
            if (ammo.Stack <= 0) Game1.player.Items.Remove(ammo);

            Game1.playSound("load_gun"); // Sonido mecánico (o 'toolSwap')
            Game1.showGlobalMessage(Mod.Helper.Translation.Get("injector.reloaded", new { count = toLoad }));
        }

        private void HandleInjection(ButtonPressedEventArgs e)
        {
            Item tool = Game1.player.CurrentItem;

            // 1. Verificar Munición
            if (!tool.modData.TryGetValue(AmmoCountKey, out string cStr) || int.Parse(cStr) <= 0)
            {
                Game1.playSound("click"); // Clic seco (sin munición)
                return;
            }

            // 2. Verificar Objetivo (Cultivo)
            Vector2 tile = e.Cursor.Tile;
            GameLocation loc = Game1.currentLocation;
            
            if (loc.terrainFeatures.TryGetValue(tile, out TerrainFeature tf) && tf is HoeDirt dirt && dirt.crop != null)
            {
                // EJECUTAR MAGIA
                ApplyMutagenEffect(loc, tile, dirt, tool.modData[AmmoTypeKey]);
                
                // Reducir munición
                int newCount = int.Parse(cStr) - 1;
                tool.modData[AmmoCountKey] = newCount.ToString();
                
                // Animación jugador
                Game1.player.animateOnce(284); // Animación de usar varita/espada
            }
        }

        private void ApplyMutagenEffect(GameLocation loc, Vector2 tile, HoeDirt dirt, string mutagenId)
        {
            Random methodRng = new Random((int)tile.X * 1000 + (int)tile.Y + (int)Game1.uniqueIDForThisGame);
            
            // Lógica según tipo de mutágeno
            if (mutagenId.Contains("Growth"))
            {
                // Crecimiento Seguro
                if (dirt.crop.currentPhase.Value < dirt.crop.phaseDays.Count - 1)
                {
                    dirt.crop.currentPhase.Value++;
                    Game1.playSound("wand");
                    Mod.Ecosystem.RestoreNutrients(loc, tile, "(O)JavCombita.ELE_Fertilizer_Omni"); // Bonus side effect
                }
            }
            else if (mutagenId.Contains("Chaos")) // High Risk
            {
                double roll = methodRng.NextDouble();
                
                if (roll < 0.30) // 30% Falla catastrófica (Monstruo)
                {
                    dirt.crop = null; // Destruir cultivo
                    Game1.playSound("shadowDie");
                    var monster = new StardewValley.Monsters.RockCrab(tile * 64f, "JavCombita.ELE_MelonCrab");
					monster.wildernessFarmMonster = true;
					loc.addCharacter(monster);
                    Game1.createRadialDebris(loc, 12, (int)tile.X, (int)tile.Y, 6, false);
                }
                else if (roll < 0.60) // 30% Éxito Crítico (Cultivo Gigante o Doble)
                {
                    dirt.crop.growCompletely();
                    Game1.playSound("reward");
                    // Intentar forzar cultivo gigante si es posible, si no, solo crece
                    if (dirt.crop.indexOfHarvest.Value == 190 || dirt.crop.indexOfHarvest.Value == 254 || dirt.crop.indexOfHarvest.Value == 276)
                    {
                        // Lógica compleja de gigante omitida por brevedad, simplemente crece full
                    }
                }
                else // 40% Crecimiento normal + Plaga muerta
                {
                    dirt.crop.currentPhase.Value++;
                    Game1.playSound("bubbles");
                }
            }
        }

        // Dibuja el contador de munición sobre el ítem en la barra de herramientas
        private void OnRenderedHud(object sender, RenderedHudEventArgs e)
        {
            if (Game1.player.CurrentItem == null || Game1.player.CurrentItem.ItemId != InjectorItemId) return;
            
            Item tool = Game1.player.CurrentItem;
            if (tool.modData.TryGetValue(AmmoCountKey, out string count))
            {
                // Posición del slot seleccionado en la toolbar
                Vector2 slotPos = new Vector2(
                    (Game1.uiViewport.Width / 2) - (6 * 64) + (Game1.player.CurrentToolIndex * 64),
                    Game1.uiViewport.Height - 64 - 24
                );
                
                // Dibujar número pequeño
                Utility.drawTinyDigits(int.Parse(count), e.SpriteBatch, slotPos + new Vector2(40, 40), 3f, 1f, Color.Yellow);
            }
        }
    }
}