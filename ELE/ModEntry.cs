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
        
        public EcosystemManager Ecosystem { get; private set; }
        public MonsterMigration Migration { get; private set; }
        public RenderingSystem Renderer { get; private set; }
        public MachineLogic Machines { get; private set; }

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

            // Detección de clic en Ladybug Shelter (Fix Android)
            if (e.Button.IsActionButton() || e.Button == SButton.MouseLeft) 
            {
                Vector2 clickedTile = e.Cursor.Tile;
                if (Game1.currentLocation.objects.TryGetValue(clickedTile, out StardewValley.Object obj))
                {
                    if (obj.ItemId == "JavCombita.ELE_LadybugShelter")
                    {
                        Game1.drawObjectDialogue("The Ladybug Shelter is buzzing with activity.");
                        Helper.Input.Suppress(e.Button);
                    }
                }
            }
        }

        private void RegisterConsoleCommands()
        {
            Helper.ConsoleCommands.Add("ele_invasion", "Forces a monster invasion.", (cmd, args) => 
            {
                if (!Context.IsWorldReady) return;
                Monitor.Log(Helper.Translation.Get("debug.invasion_force"), LogLevel.Alert);
                this.Migration.ForceInvasion();
            });

            Helper.ConsoleCommands.Add("ele_pest", "Forces pest spawn.", (cmd, args) => 
            {
                if (!Context.IsWorldReady) return;
                Monitor.Log(Helper.Translation.Get("debug.pest_force"), LogLevel.Alert);
                this.Ecosystem.ForcePestAttack();
            });
            
            Helper.ConsoleCommands.Add("ele_status", "Shows nutrient levels.", (cmd, args) =>
            {
                 if (!Context.IsWorldReady) return;
                 var tile = Game1.currentCursorTile;
                 var data = this.Ecosystem.GetSoilDataAt(Game1.currentLocation, tile);
                 string msg = Helper.Translation.Get("message.soil_analysis", new { val1 = (int)data.Nitrogen, val2 = (int)data.Phosphorus, val3 = (int)data.Potassium });
                 Monitor.Log($"Tile {tile}: {msg}", LogLevel.Alert);
            });

            Helper.ConsoleCommands.Add("ele_trigger_mail", "Forces delivery of all ELE mails.", (cmd, args) => 
            {
                if (!Context.IsWorldReady) return;
                string[] mails = {
                    "JavCombita.ELE_RobinShelterMail",
                    "JavCombita.ELE_ClintAnalyzerMail",
                    "JavCombita.ELE_PierreSpreaderMail", 
                    "JavCombita.ELE_QiUpgradeMail",
                    "JavCombita.ELE_DemetriusBoosterMail",
                    "JavCombita.ELE_EvelynBoosterMail",
                    "JavCombita.ELE_JodiBoosterMail"
                };
                foreach(var mail in mails) {
                    if (!Game1.player.mailbox.Contains(mail)) {
                        Game1.player.mailbox.Add(mail);
                        Monitor.Log(Helper.Translation.Get("debug.mail_added", new { val1 = mail }), LogLevel.Info);
                    } else {
                        Monitor.Log(Helper.Translation.Get("debug.mail_exists", new { val1 = mail }), LogLevel.Info);
                    }
                }
            });

            Helper.ConsoleCommands.Add("ele_unlock_recipes", "Unlocks all ELE crafting recipes.", (cmd, args) =>
            {
                if (!Context.IsWorldReady) return;
                string[] recipes = {
                    "Ladybug Shelter",
                    "Soil Analyzer",
                    "Nutrient Spreader",
                    "Nutrient Spreader Mk2",
                    "Nutrient Spreader Mk3",
                    "Nutrient Spreader Omega",
                    "Nitrogen Booster",
                    "Phosphorus Booster",
                    "Potassium Booster",
                    "Omni-Nutrient Mix"
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
            });

            Helper.ConsoleCommands.Add("ele_add_items", "Adds ELE items. Usage: ele_add_items <n>", (cmd, args) =>
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
            });

            Helper.ConsoleCommands.Add("ele_list_items", "Lists all items defined by ELE.", (cmd, args) => 
            {
                if (!Context.IsWorldReady) return;
                // ... (Listas de items no necesitan traducción, son datos crudos para el dev) ...
            });
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
                configMenu.AddTextOption(ModManifest, () => Config.InvasionDifficulty, val => Config.InvasionDifficulty = val, () => Helper.Translation.Get("config.invasionDifficulty"), null, new[] { "Easy", "Medium", "Hard", "VeryHard" });
                configMenu.AddSectionTitle(ModManifest, () => Helper.Translation.Get("config.section.nutrients"));
                configMenu.AddBoolOption(ModManifest, () => Config.EnableNutrientCycle, val => Config.EnableNutrientCycle = val, () => Helper.Translation.Get("config.enableNutrients"));
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
