using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using ELE.Core.Systems;
using HarmonyLib;
using ELE.Core.Integrations; 

namespace ELE.Core
{
    /// <summary>The mod entry point.</summary>
    public class ModEntry : Mod
    {
        // Singleton pattern for easy access from other classes if needed
        public static ModEntry Instance { get; private set; }

        // Configuration
        public ModConfig Config { get; private set; }

        // Core Systems
        public EcosystemManager Ecosystem { get; private set; }
        public MonsterMigration Migration { get; private set; }
        public RenderingSystem Renderer { get; private set; }

        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            Instance = this;
            
            // 1. Load Configuration
            this.Config = helper.ReadConfig<ModConfig>();

            // 2. Initialize Systems (Dependency Injection)
            try 
            {
                this.Ecosystem = new EcosystemManager(this);
                this.Migration = new MonsterMigration(this);
                this.Renderer = new RenderingSystem(this);
                
                this.Monitor.Log("Systems initialized successfully.", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Error initializing systems: {ex.Message}", LogLevel.Error);
            }
			
			// --- HARMONY PATCHING (NUEVO) ---
			try
			{
				var harmony = new Harmony(this.ModManifest.UniqueID);
        
				// Esta línea busca automáticamente todas las clases con [HarmonyPatch] en tu proyecto y las aplica.
				harmony.PatchAll(); 
        
				this.Monitor.Log("Harmony patches applied successfully.", LogLevel.Debug);
			}
			catch (Exception ex)
			{
				this.Monitor.Log($"Failed to apply Harmony patches: {ex}", LogLevel.Error);
			}
			// --------------------------------

            // 3. Hook Events
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdate;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
        }

        /// <summary>Raised after the game is launched, right before the first update tick.</summary>
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            // Generic Mod Config Menu (GMCM) Integration
            SetupConfigMenu();
        }

        /// <summary>Sets up the Generic Mod Config Menu integration.</summary>
        private void SetupConfigMenu()
        {
            // Attempt to get the API
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");

            // If GMCM is not installed, this will be null, and we just skip setup.
            if (configMenu is null)
                return;

            // Register the mod
            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () => this.Helper.WriteConfig(this.Config)
            );

            // --- General Section ---
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => this.Helper.Translation.Get("config.section.general")
            );

            // Toggle Pest Invasions
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.enableInvasions"),
                tooltip: () => this.Helper.Translation.Get("config.enableInvasions.tooltip"),
                getValue: () => this.Config.EnablePestInvasions,
                setValue: value => this.Config.EnablePestInvasions = value
            );

            // Toggle Monster Migration
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.enableMigration"),
                tooltip: () => this.Helper.Translation.Get("config.enableMigration.tooltip"),
                getValue: () => this.Config.EnableMonsterMigration,
                setValue: value => this.Config.EnableMonsterMigration = value
            );
			
			// Opción de Dificultad (Dropdown)
            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.invasionDifficulty"),
                tooltip: () => this.Helper.Translation.Get("config.invasionDifficulty.tooltip"),
                getValue: () => this.Config.InvasionDifficulty,
                setValue: value => this.Config.InvasionDifficulty = value,
                // Los valores internos (código)
                allowedValues: new string[] { "Easy", "Medium", "Hard", "VeryHard" },
                // Cómo se ven en pantalla (Traducción)
                formatAllowedValue: value => this.Helper.Translation.Get($"difficulty.{value.ToLower()}")
            );

            // Days before invasion (Int Slider)
            configMenu.AddNumberOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.daysBeforeInvasion"),
                tooltip: () => this.Helper.Translation.Get("config.daysBeforeInvasion.tooltip"),
                getValue: () => this.Config.DaysBeforeTownInvasion,
                setValue: value => this.Config.DaysBeforeTownInvasion = value,
                min: 5,
                max: 100
            );

            // --- Nutrient Section ---
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => this.Helper.Translation.Get("config.section.nutrients")
            );

            // Toggle Nutrient Cycle
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.enableNutrients"),
                tooltip: () => this.Helper.Translation.Get("config.enableNutrients.tooltip"),
                getValue: () => this.Config.EnableNutrientCycle,
                setValue: value => this.Config.EnableNutrientCycle = value
            );

            // Depletion Multiplier (Float Slider)
            configMenu.AddNumberOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.depletionMultiplier"),
                tooltip: () => this.Helper.Translation.Get("config.depletionMultiplier.tooltip"),
                getValue: () => this.Config.NutrientDepletionMultiplier,
                setValue: value => this.Config.NutrientDepletionMultiplier = value,
                min: 0.1f,
                max: 3.0f,
                interval: 0.1f
            );

            // --- Visuals Section ---
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => this.Helper.Translation.Get("config.section.visuals")
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.showOverlay"),
                tooltip: () => this.Helper.Translation.Get("config.showOverlay.tooltip"),
                getValue: () => this.Config.ShowOverlayOnHold,
                setValue: value => this.Config.ShowOverlayOnHold = value
            );
        }

        /// <summary>Raised after a new day starts.</summary>
        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            if (this.Config.EnableNutrientCycle)
            {
                this.Monitor.Log("Calculating daily soil nutrients...", LogLevel.Trace);
                this.Ecosystem.CalculateDailyNutrients();
            }

            if (this.Config.EnableMonsterMigration)
            {
                this.Migration.CheckMigrationStatus();
            }
        }

        /// <summary>Raised once per second. Optimized for Android logic.</summary>
        private void OnOneSecondUpdate(object sender, OneSecondUpdateTickedEventArgs e)
        {
            // Safety check: Don't run logic if world is not ready
            if (!Context.IsWorldReady) return;

            // Pest Logic (Low frequency update)
            if (this.Config.EnablePestInvasions)
            {
                this.Ecosystem.UpdatePests();
            }

            // Monster AI Logic (Low frequency pathfinding)
            if (this.Config.EnableMonsterMigration)
            {
                this.Migration.UpdateMigratingMonsters();
            }
        }

        /// <summary>Raised after the game world is drawn to the screen.</summary>
        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // Only call renderer if enabled in config to save GPU cycles
            if (this.Config.ShowOverlayOnHold)
            {
                this.Renderer.OnRenderedWorld(sender, e);
            }
        }
    }
}