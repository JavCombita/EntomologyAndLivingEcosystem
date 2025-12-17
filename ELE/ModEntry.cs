using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using ELE.Core.Systems;
using ELE.Core.Integrations;
using HarmonyLib;

namespace ELE.Core
{
    public class ModEntry : Mod
    {
        public static ModEntry Instance { get; private set; }
        public ModConfig Config { get; private set; }
        
        // Sistemas
        public EcosystemManager Ecosystem { get; private set; }
        public MonsterMigration Migration { get; private set; }
        public RenderingSystem Renderer { get; private set; }

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            this.Config = helper.ReadConfig<ModConfig>();

            // Inicializar Sistemas
            this.Ecosystem = new EcosystemManager(this);
            this.Migration = new MonsterMigration(this);
            this.Renderer = new RenderingSystem(this);

            // Harmony Patches
            try
            {
                var harmony = new Harmony(this.ModManifest.UniqueID);
                harmony.PatchAll();
                this.Monitor.Log("Harmony patches applied successfully.", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Failed to apply Harmony patches: {ex}", LogLevel.Error);
            }

            // Eventos del Ciclo de Juego
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            
            // Evento de Input (Clics / Toques)
            helper.Events.Input.ButtonPressed += OnButtonPressed;

            // --- COMANDOS DE CONSOLA (DEBUG) ---
            // Escribe 'ele_pest' en la consola SMAPI para forzar una plaga inmediata
            helper.ConsoleCommands.Add("ele_pest", "Forces a pest invasion nearby.", this.OnPestCommand);
        }

        // --- 1. MANEJADOR DE COMANDOS ---
        private void OnPestCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady) return;
            this.Monitor.Log("🐜 Command received: Forcing pest invasion...", LogLevel.Warn);
            
            // Llama al método que creamos en EcosystemManager
            // (Asegúrate de haber agregado 'ForcePestInvasion' en EcosystemManager.cs como vimos antes)
            this.Ecosystem.ForcePestInvasion();
        }

        // --- 2. MANEJADOR DE CLICS (FINAL) ---
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // A. Detección de Input (PC Clic Derecho / Android Toque)
            bool isAction = e.Button.IsActionButton();
            bool isAndroidTap = Constants.TargetPlatform == GamePlatform.Android && e.Button == SButton.MouseLeft;

            if (!isAction && !isAndroidTap) return;

            Vector2 clickedTile = e.Cursor.Tile;
            GameLocation location = Game1.currentLocation;

            // B. Buscar Objeto (Lógica Cabeza y Pies)
            StardewValley.Object obj = location.getObjectAtTile((int)clickedTile.X, (int)clickedTile.Y);

            if (obj == null)
            {
                // Si clickeaste el techo (aire), revisa el tile de abajo (pies)
                obj = location.getObjectAtTile((int)clickedTile.X, (int)clickedTile.Y + 1);
            }

            // C. Si es el Ladybug Shelter
            if (obj != null && obj.ItemId == "JavCombita.ELE_LadybugShelter")
            {
                // --- CHEQUEO DE HERRAMIENTAS ---
                // Si tienes Hacha, Pico o Azada, asumimos que quieres quitarlo.
                // Salimos (return) para dejar que el juego use la herramienta.
                Item currentItem = Game1.player.CurrentItem;
                if (currentItem is StardewValley.Tools.Axe || 
                    currentItem is StardewValley.Tools.Pickaxe || 
                    currentItem is StardewValley.Tools.Hoe)
                {
                    return; 
                }

                // --- CHEQUEO DE DISTANCIA ---
                // Solo mostrar mensaje si estás cerca (1.5 tiles = adyacente o diagonal cercana)
                if (Vector2.Distance(Game1.player.Tile, obj.TileLocation) > 1.5f)
                {
                    return; // En Android, esto hará que el personaje camine hacia el objeto
                }

                // --- MOSTRAR ESTADO ---
                string key = "JavCombita.ELE/PestCount";
                int count = 0;
                if (obj.modData.TryGetValue(key, out string countStr))
                {
                    int.TryParse(countStr, out count);
                }

                Game1.drawObjectDialogue(this.Helper.Translation.Get("message.shelter_status", new { count = count }));
                
                // Suprimimos el clic para evitar golpes accidentales (si no tiene herramienta de demolición)
                this.Helper.Input.Suppress(e.Button);
            }
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            if (this.Config.EnableNutrientCycle)
            {
                this.Ecosystem.CalculateDailyNutrients();
            }
            
            this.Migration.CheckMigrationStatus();
            CheckAndSendRobinMail();
        }

        private void CheckAndSendRobinMail()
        {
            string mailId = "JavCombita.ELE_RobinShelterMail";

            if (Game1.player.mailReceived.Contains(mailId) || Game1.player.mailbox.Contains(mailId)) 
                return;

            if (Game1.player.getFriendshipHeartLevelForNPC("Robin") >= 3)
            {
                Game1.player.mailbox.Add(mailId);
                this.Monitor.Log($"📬 Requirements met! Sending '{mailId}' to player.", LogLevel.Info);
            }
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            if (e.IsMultipleOf(60) && this.Config.EnablePestInvasions)
            {
                this.Ecosystem.UpdatePests();
            }

            if (e.IsMultipleOf(15) && this.Config.EnableMonsterMigration)
            {
                this.Migration.UpdateMigratingMonsters();
            }
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null) return;

            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () => this.Helper.WriteConfig(this.Config)
            );

            // ... (Toda tu configuración de GMCM sigue aquí igual que antes) ...
            
            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.section.general"));
            
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.enableInvasions"),
                tooltip: () => this.Helper.Translation.Get("config.enableInvasions.tooltip"),
                getValue: () => this.Config.EnablePestInvasions,
                setValue: value => this.Config.EnablePestInvasions = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.enableMigration"),
                tooltip: () => this.Helper.Translation.Get("config.enableMigration.tooltip"),
                getValue: () => this.Config.EnableMonsterMigration,
                setValue: value => this.Config.EnableMonsterMigration = value
            );

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.invasionDifficulty"),
                tooltip: () => this.Helper.Translation.Get("config.invasionDifficulty.tooltip"),
                getValue: () => this.Config.InvasionDifficulty,
                setValue: value => this.Config.InvasionDifficulty = value,
                allowedValues: new string[] { "Easy", "Medium", "Hard", "VeryHard" },
                formatAllowedValue: value => this.Helper.Translation.Get($"difficulty.{value.ToLower()}")
            );

            configMenu.AddNumberOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.daysBeforeInvasion"),
                tooltip: () => this.Helper.Translation.Get("config.daysBeforeInvasion.tooltip"),
                getValue: () => this.Config.DaysBeforeTownInvasion,
                setValue: value => this.Config.DaysBeforeTownInvasion = value,
                min: 5, max: 100
            );

            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.section.nutrients"));

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.enableNutrients"),
                tooltip: () => this.Helper.Translation.Get("config.enableNutrients.tooltip"),
                getValue: () => this.Config.EnableNutrientCycle,
                setValue: value => this.Config.EnableNutrientCycle = value
            );

            configMenu.AddNumberOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.depletionMultiplier"),
                tooltip: () => this.Helper.Translation.Get("config.depletionMultiplier.tooltip"),
                getValue: () => this.Config.NutrientDepletionMultiplier,
                setValue: value => this.Config.NutrientDepletionMultiplier = value,
                min: 0.1f, max: 5.0f
            );

            configMenu.AddSectionTitle(mod: this.ModManifest, text: () => this.Helper.Translation.Get("config.section.visuals"));

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.showOverlay"),
                tooltip: () => this.Helper.Translation.Get("config.showOverlay.tooltip"),
                getValue: () => this.Config.ShowOverlayOnHold,
                setValue: value => this.Config.ShowOverlayOnHold = value
            );
        }
    }
}
