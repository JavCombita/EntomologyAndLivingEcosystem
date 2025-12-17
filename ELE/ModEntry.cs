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
            
            // Detectar interacción con objetos
            helper.Events.Input.ButtonPressed += OnButtonPressed;
        }

        // --- MANEJADOR DE CLICS (PC + ANDROID + DISTANCIA + HERRAMIENTAS) ---
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // 1. DETECCIÓN DE BOTÓN (PC + Android)
            bool isAction = e.Button.IsActionButton();
            bool isAndroidTap = Constants.TargetPlatform == GamePlatform.Android && e.Button == SButton.MouseLeft;

            if (!isAction && !isAndroidTap) return;

            Vector2 clickedTile = e.Cursor.Tile;
            GameLocation location = Game1.currentLocation;

            // 2. BUSCAR OBJETO (Lógica "Cabeza y Pies")
            StardewValley.Object obj = location.getObjectAtTile((int)clickedTile.X, (int)clickedTile.Y);

            if (obj == null)
            {
                // Si le diste al techo (vacío), revisa los pies (Y + 1)
                obj = location.getObjectAtTile((int)clickedTile.X, (int)clickedTile.Y + 1);
            }

            // 3. SI ES NUESTRO SHELTER
            if (obj != null && obj.ItemId == "JavCombita.ELE_LadybugShelter")
            {
                // --- A. CHEQUEO DE HERRAMIENTA (Axe, Pickaxe, Hoe) ---
                // Si el jugador tiene una de estas herramientas, asumimos que quiere usarla (golpear/romper)
                // y no leer el mensaje.
                Item currentItem = Game1.player.CurrentItem;
                if (currentItem is StardewValley.Tools.Axe || 
                    currentItem is StardewValley.Tools.Pickaxe || 
                    currentItem is StardewValley.Tools.Hoe) // <--- ¡Hoe agregado!
                {
                    return; // Salimos para dejar que el juego use la herramienta
                }

                // --- B. CHEQUEO DE DISTANCIA ---
                // Calculamos la distancia entre el Jugador y el Shelter.
                // 1.5 tiles permite interactuar desde casillas adyacentes y diagonales cercanas.
                if (Vector2.Distance(Game1.player.Tile, obj.TileLocation) > 1.5f)
                {
                    return; // Si está muy lejos, salimos (permitiendo que el jugador camine hacia allá en Android)
                }
                // -------------------------------------

                string key = "JavCombita.ELE/PestCount";
                int count = 0;
                
                if (obj.modData.TryGetValue(key, out string countStr))
                {
                    int.TryParse(countStr, out count);
                }

                Game1.drawObjectDialogue(this.Helper.Translation.Get("message.shelter_status", new { count = count }));
                
                // Suprimimos el clic solo si cumplió todas las condiciones (cerca y sin herramienta peligrosa)
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

            // --- LÓGICA DE CORREO DE ROBIN ---
            CheckAndSendRobinMail();
        }

        private void CheckAndSendRobinMail()
        {
            // ID debe coincidir EXACTAMENTE con el de content.json
            string mailId = "JavCombita.ELE_RobinShelterMail";

            // 1. Si ya tiene la carta, salimos
            if (Game1.player.mailReceived.Contains(mailId) || Game1.player.mailbox.Contains(mailId)) 
                return;

            // 2. Condición: 3 Corazones con Robin (750 puntos)
            // Nota: Un corazón = 250 puntos. 3 corazones = 750.
            if (Game1.player.getFriendshipHeartLevelForNPC("Robin") >= 3)
            {
                Game1.player.mailbox.Add(mailId);
                this.Monitor.Log($"📬 Requirements met! Sending '{mailId}' to player.", LogLevel.Info);
                
                // Opcional: Sonido de notificación si quieres ser muy detallista, 
                // pero el juego suele sonar al despertar si hay correo.
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

            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => this.Helper.Translation.Get("config.section.general")
            );

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

            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => this.Helper.Translation.Get("config.section.nutrients")
            );

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
    }
}
