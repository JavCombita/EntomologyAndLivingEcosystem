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

            // Eventos
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Input.ButtonPressed += OnButtonPressed;

            // --- COMANDOS DE CONSOLA (DEBUG) ---
            
            // 1. Forzar Plaga de Cultivos
            helper.ConsoleCommands.Add("ele_pest", "Forces a pest invasion on crops.", this.OnPestCommand);

            // 2. Forzar Invasión de Monstruos (NUEVO)
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
            // Llamamos al nuevo método en MonsterMigration
            this.Migration.ForceTownInvasion();
        }

        // --- MANEJADOR DE CLICS ---
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
                // Chequeo de Herramientas
                Item currentItem = Game1.player.CurrentItem;
                if (currentItem is StardewValley.Tools.Axe || 
                    currentItem is StardewValley.Tools.Pickaxe || 
                    currentItem is StardewValley.Tools.Hoe)
                {
                    return; 
                }

                // Chequeo de Distancia
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

        // ... (El resto del archivo ModEntry: OnDayStarted, OnUpdateTicked, OnGameLaunched sigue igual) ...
        
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
            // ... (Tu configuración de GMCM existente) ...
            // (Para ahorrar espacio no la pego toda, pero asegúrate de dejarla como estaba)
             var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null) return;

            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () => this.Helper.WriteConfig(this.Config)
            );
            // ... etc ...
        }
    }
}
