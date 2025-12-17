using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics; // <--- NECESARIO
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
        
        // --- NUEVO: Textura Estática para acceso global desde el Patch ---
        public static Texture2D ShelterTexture { get; private set; }

        public static Texture2D PestTexture { get; private set; }
        // ---------------------------------------------------------------

        public ModConfig Config { get; private set; }
        
        // Sistemas
        public EcosystemManager Ecosystem { get; private set; }
        public MonsterMigration Migration { get; private set; }
        public RenderingSystem Renderer { get; private set; }

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            this.Config = helper.ReadConfig<ModConfig>();

            // --- 1. CARGAR TEXTURA DEL SHELTER ---
            try 
            {
                ShelterTexture = helper.ModContent.Load<Texture2D>("assets/ladybug_shelter_anim.png");
                
                // Intenta cargar la textura de plaga, si no existe no pasa nada
                PestTexture = helper.ModContent.Load<Texture2D>("assets/pest_anim.png");
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Failed to load textures: {ex.Message}", LogLevel.Warn);
            }
            // -------------------------------------

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
            helper.ConsoleCommands.Add("ele_pest", "Forces a pest invasion on crops.", this.OnPestCommand);
            helper.ConsoleCommands.Add("ele_invasion", "Forces a monster invasion in Town.", this.OnInvasionCommand);
        }

        // --- MANEJADORES DE COMANDOS ---

        private void OnPestCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady) return;
            this.Ecosystem.ForcePestInvasion();
        }

        private void OnInvasionCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady) return;
            this.Migration.ForceTownInvasion();
        }

        // --- MANEJADOR DE CLICS (PC + ANDROID + HERRAMIENTAS) ---
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            bool isAction = e.Button.IsActionButton();
            bool isAndroidTap = Constants.TargetPlatform == GamePlatform.Android && e.Button == SButton.MouseLeft;

            if (!isAction && !isAndroidTap) return;

            Vector2 clickedTile = e.Cursor.Tile;
            GameLocation location = Game1.currentLocation;

            StardewValley.Object obj = location.getObjectAtTile((int)clickedTile.X, (int)clickedTile.Y);

            if (obj == null)
            {
                obj = location.getObjectAtTile((int)clickedTile.X, (int)clickedTile.Y + 1);
            }

            if (obj != null && obj.ItemId == "JavCombita.ELE_LadybugShelter")
            {
                Item currentItem = Game1.player.CurrentItem;
                if (currentItem is StardewValley.Tools.Axe || 
                    currentItem is StardewValley.Tools.Pickaxe || 
                    currentItem is StardewValley.Tools.Hoe)
                {
                    return; 
                }

                if (Vector2.Distance(Game1.player.Tile, obj.TileLocation) > 1.5f)
                {
                    return; 
                }

                string key = "JavCombita.ELE/PestCount";
                int count = 0;
                if (obj.modData.TryGetValue(key, out string countStr))
                {
                    int.TryParse(countStr, out count);
                }

                Game1.drawObjectDialogue(this.Helper.Translation.Get("message.shelter_status", new { count = count }));
                this.Helper.Input.Suppress(e.Button);
            }
        }

        // --- EVENTOS DE JUEGO ---

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            if (this.Config.EnableNutrientCycle) this.Ecosystem.CalculateDailyNutrients();
            this.Migration.CheckMigrationStatus();
            CheckAndSendRobinMail();
        }

        private void CheckAndSendRobinMail()
        {
            string mailId = "JavCombita.ELE_RobinShelterMail";
            if (Game1.player.mailReceived.Contains(mailId) || Game1.player.mailbox.Contains(mailId)) return;
            if (Game1.player.getFriendshipHeartLevelForNPC("Robin") >= 3)
            {
                Game1.player.mailbox.Add(mailId);
                this.Monitor.Log($"📬 Requirements met! Sending '{mailId}' to player.", LogLevel.Info);
            }
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (e.IsMultipleOf(60) && this.Config.EnablePestInvasions) this.Ecosystem.UpdatePests();
            if (e.IsMultipleOf(15) && this.Config.EnableMonsterMigration) this.Migration.UpdateMigratingMonsters();
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

            // Configuración General
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

            // Configuración Nutrientes
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

            // Configuración Visuales
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
