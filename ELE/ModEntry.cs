using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
        
        public static Texture2D ShelterTexture { get; private set; }
        public static Texture2D PestTexture { get; private set; }

        public ModConfig Config { get; private set; }
        
        // Sistemas
        public EcosystemManager Ecosystem { get; private set; }
        public MonsterMigration Migration { get; private set; }
        public RenderingSystem Renderer { get; private set; }
        public MachineLogic Machines { get; private set; }
        public MailSystem Mail { get; private set; }
		public InjectorSystem Injector { get; private set; }

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            this.Config = helper.ReadConfig<ModConfig>();

            LoadAssets();
            InitializeSystems();
            RegisterEvents();
            RegisterConsoleCommands();

            var harmony = new Harmony(this.ModManifest.UniqueID);
            harmony.PatchAll();
        }

        private void LoadAssets()
        {
            try 
            {
                ShelterTexture = Helper.ModContent.Load<Texture2D>("assets/ladybug_shelter_anim.png");
                
                if (Helper.ModContent.DoesAssetExist<Texture2D>("assets/pest_anim.png"))
                {
                    PestTexture = Helper.ModContent.Load<Texture2D>("assets/pest_anim.png");
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[ELE] Failed to load textures: {ex.Message}", LogLevel.Error);
            }
        }

        private void InitializeSystems()
        {
            this.Ecosystem = new EcosystemManager(this);
            this.Migration = new MonsterMigration(this);
            this.Renderer = new RenderingSystem(this);
            this.Machines = new MachineLogic(this);
            this.Mail = new MailSystem(this);
			this.Injector = new InjectorSystem(this);
        }

        private void RegisterEvents()
        {
            Helper.Events.GameLoop.DayStarted += OnDayStarted;
            Helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            Helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            Helper.Events.Input.ButtonPressed += OnButtonPressed; 
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // Delegamos la interacción a los sistemas
            this.Ecosystem.HandleInteraction(e);
            this.Machines.HandleInteraction(e);
        }

        private void RegisterConsoleCommands()
        {
            // --- FUNCIÓN AUXILIAR: Registra comando y alias apuntando a la misma lógica ---
            void AddCmd(string name, string alias, string desc, Action<string, string[]> handler)
            {
                Helper.ConsoleCommands.Add(name, desc, handler);
                Helper.ConsoleCommands.Add(alias, $"{desc} (Alias for {name})", handler);
            }

            // ==============================================================================
            // DEFINICIÓN DE HANDLERS (Lógica encapsulada)
            // ==============================================================================

            // 1. INVASIÓN
            Action<string, string[]> invasionHandler = (cmd, args) =>
            {
                if (!Context.IsWorldReady) return;
                Monitor.Log(Helper.Translation.Get("debug.invasion_force"), LogLevel.Alert);
                this.Migration.ForceInvasion();
            };

            // 2. PLAGA (PEST)
            Action<string, string[]> pestHandler = (cmd, args) =>
            {
                if (!Context.IsWorldReady) return;
                Monitor.Log(Helper.Translation.Get("debug.pest_force"), LogLevel.Alert);
                this.Ecosystem.ForcePestAttack();
            };

            // 3. ESTATUS / ANÁLISIS
            Action<string, string[]> statusHandler = (cmd, args) =>
            {
                 if (!Context.IsWorldReady) return;
                 var tile = Game1.currentCursorTile;
                 var data = this.Ecosystem.GetSoilDataAt(Game1.currentLocation, tile);
                 string analysisMsg = Helper.Translation.Get("message.soil_analysis", new { val1 = (int)data.Nitrogen, val2 = (int)data.Phosphorus, val3 = (int)data.Potassium });
                 
                 Monitor.Log(Helper.Translation.Get("log.tile_report", new { tile = tile.ToString(), msg = analysisMsg }), LogLevel.Alert);
            };

            // 4. CORREO (MAIL)
            Action<string, string[]> mailHandler = (cmd, args) =>
            {
                if (!Context.IsWorldReady) return;
                this.Mail.ForceAllMails();
                Monitor.Log(Helper.Translation.Get("debug.mail_triggered"), LogLevel.Alert);
            };

            // 5. RECETAS
            Action<string, string[]> recipesHandler = (cmd, args) =>
            {
                if (!Context.IsWorldReady) return;
                
                string[] recipes = {
                    "Ladybug_Shelter",
                    "Soil_Analyzer",
                    "Nutrient_Spreader",
                    "Nutrient_Spreader_Mk2",
                    "Nutrient_Spreader_Mk3",
                    "Nutrient_Spreader_Omega",
                    "Nitrogen_Booster",
                    "Phosphorus_Booster",
                    "Potassium_Booster",
                    "Omni_Nutrient_Mix",
					"Alchemical_Injector",
					"Mutagen_Growth",
					"Mutagen_Chaos"
                };

                int unlockedCount = 0;
                foreach (var recipe in recipes)
                {
                    if (!Game1.player.craftingRecipes.ContainsKey(recipe))
                    {
                        Game1.player.craftingRecipes.Add(recipe, 0);
                        unlockedCount++;
                        Monitor.Log(Helper.Translation.Get("debug.recipe_learned", new { val1 = recipe }), LogLevel.Info);
                    }
                }
                
                if (unlockedCount > 0)
                    Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("debug.recipes_unlocked", new { val1 = unlockedCount }), 2));
                else
                    Monitor.Log(Helper.Translation.Get("debug.recipes_known"), LogLevel.Warn);
            };

            // 6. AGREGAR ITEMS
            Action<string, string[]> addItemsHandler = (cmd, args) =>
            {
                if (!Context.IsWorldReady) return;
                
                int amount = 1;
                if (args.Length > 0 && !int.TryParse(args[0], out amount)) amount = 1;

                string[] itemIds = {
                    "(O)JavCombita.ELE_SoilAnalyzer",
                    "(O)JavCombita.ELE_Fertilizer_N",
                    "(O)JavCombita.ELE_Fertilizer_P",
                    "(O)JavCombita.ELE_Fertilizer_K",
                    "(O)JavCombita.ELE_Fertilizer_Omni",
					"(O)JavCombita.ELE_AlchemicalInjector",
					"(O)JavCombita.ELE_Mutagen_Growth",
					"(O)JavCombita.ELE_Mutagen_Chaos",
                    "(BC)JavCombita.ELE_LadybugShelter",
                    "(BC)JavCombita.ELE_NutrientSpreader",
                    "(BC)JavCombita.ELE_NutrientSpreader_Mk2",
                    "(BC)JavCombita.ELE_NutrientSpreader_Mk3",
                    "(BC)JavCombita.ELE_NutrientSpreader_Omega"
                };

                int addedCount = 0;
                foreach (string id in itemIds)
                {
                    Item item = ItemRegistry.Create(id, amount);
                    if (item != null)
                    {
                        Game1.player.addItemByMenuIfNecessary(item);
                        addedCount++;
                    }
                    else
                    {
                        Monitor.Log(Helper.Translation.Get("debug.item_error", new { val1 = id }), LogLevel.Error);
                    }
                }
                Monitor.Log(Helper.Translation.Get("debug.items_added", new { val1 = amount, val2 = addedCount }), LogLevel.Alert);
            };

            // 7. LISTAR ITEMS
            Action<string, string[]> listItemsHandler = (cmd, args) =>
            {
                if (!Context.IsWorldReady) return;
                Monitor.Log(Helper.Translation.Get("log.list_header"), LogLevel.Info);
                
                var objData = Game1.content.Load<Dictionary<string, StardewValley.GameData.Objects.ObjectData>>("Data/Objects");
                foreach(var kvp in objData) {
                    if (kvp.Key.StartsWith("JavCombita.ELE")) {
                        Monitor.Log(Helper.Translation.Get("log.list_entry", new { id = kvp.Key, name = kvp.Value.Name }), LogLevel.Info);
                    }
                }
            };

            // ==============================================================================
            // REGISTRO DE COMANDOS Y ALIAS
            // ==============================================================================

            AddCmd("ele_invasion",       "ele_i",       "Forces a monster invasion.",      invasionHandler);
            AddCmd("ele_pest",           "ele_p",       "Forces pest spawn.",              pestHandler);
            AddCmd("ele_status",         "ele_s",       "Shows nutrient levels.",          statusHandler);
            AddCmd("ele_trigger_mail",   "ele_mail",    "Forces delivery of all ELE mails.", mailHandler);
            AddCmd("ele_unlock_recipes", "ele_recipes", "Unlocks all ELE crafting recipes.", recipesHandler);
            AddCmd("ele_add_items",      "ele_add",     "Adds ELE items. Usage: <cmd> <n>",  addItemsHandler);
            AddCmd("ele_list_items",     "ele_list",    "Lists all items defined by ELE.",   listItemsHandler);
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu != null)
            {
                configMenu.Register(ModManifest, () => Config = new ModConfig(), () => Helper.WriteConfig(Config));
                configMenu.AddSectionTitle(ModManifest, () => Helper.Translation.Get("config.section.general"));
                configMenu.AddBoolOption(ModManifest, () => Config.EnablePestInvasions, val => Config.EnablePestInvasions = val, () => Helper.Translation.Get("config.enableInvasions"));
                configMenu.AddBoolOption(ModManifest, () => Config.EnableMonsterMigration, val => Config.EnableMonsterMigration = val, () => Helper.Translation.Get("config.enableMigration"));
				configMenu.AddNumberOption(
					ModManifest, 
					() => Config.DaysBeforeTownInvasion, 
					val => Config.DaysBeforeTownInvasion = val, 
					() => Helper.Translation.Get("config.daysBeforeInvasion")
				);
                configMenu.AddTextOption(ModManifest, () => Config.InvasionDifficulty, val => Config.InvasionDifficulty = val, () => Helper.Translation.Get("config.invasionDifficulty"), null, new[] { "Easy", "Medium", "Hard", "VeryHard" });
                configMenu.AddSectionTitle(ModManifest, () => Helper.Translation.Get("config.section.nutrients"));
                configMenu.AddBoolOption(ModManifest, () => Config.EnableNutrientCycle, val => Config.EnableNutrientCycle = val, () => Helper.Translation.Get("config.enableNutrients"));
                configMenu.AddNumberOption(ModManifest, () => Config.NutrientDepletionMultiplier, val => Config.NutrientDepletionMultiplier = val, () => Helper.Translation.Get("config.depletionMultiplier"), null, 0.1f, 5.0f);
                configMenu.AddSectionTitle(ModManifest, () => Helper.Translation.Get("config.section.visuals"));
                configMenu.AddBoolOption(ModManifest, () => Config.ShowOverlayOnHold, val => Config.ShowOverlayOnHold = val, () => Helper.Translation.Get("config.showOverlay"));
            }
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            if (Context.IsMainPlayer) 
            {
                this.Ecosystem.CalculateDailyNutrients();
                this.Migration.CheckMigrationStatus();
            }
        }

        private void OnTimeChanged(object sender, TimeChangedEventArgs e)
        {
            if (Context.IsMainPlayer)
            {
                this.Migration.UpdateInvasionLogic(e);
            }
        }
    }
}