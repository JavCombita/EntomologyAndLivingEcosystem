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
        }

        private void RegisterConsoleCommands()
        {
            Helper.ConsoleCommands.Add("ele_invasion", "Forces a monster invasion on the farm immediately.\nUsage: ele_invasion", (cmd, args) => 
            {
                if (!Context.IsWorldReady) return;
                this.Migration.ForceInvasion();
            });

            Helper.ConsoleCommands.Add("ele_pest", "Forces pest spawn on nearby crops.\nUsage: ele_pest", (cmd, args) => 
            {
                if (!Context.IsWorldReady) return;
                this.Ecosystem.ForcePestAttack();
            });
            
            Helper.ConsoleCommands.Add("ele_status", "Shows current nutrient levels of tile under cursor.", (cmd, args) =>
            {
                 if (!Context.IsWorldReady) return;
                 var tile = Game1.currentCursorTile;
                 var data = this.Ecosystem.GetSoilDataAt(Game1.currentLocation, tile);
                 Monitor.Log($"Tile {tile}: N={data.Nitrogen} P={data.Phosphorus} K={data.Potassium}", LogLevel.Alert);
            });

            // --- NUEVOS COMANDOS DE DEBUG ---

            Helper.ConsoleCommands.Add("ele_trigger_mail", "Forces delivery of all ELE mails to mailbox.", (cmd, args) => 
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
                        Monitor.Log($"Added {mail} to mailbox.", LogLevel.Info);
                    } else {
                        Monitor.Log($"{mail} already in mailbox.", LogLevel.Info);
                    }
                }
            });

            Helper.ConsoleCommands.Add("ele_list_recipes", "Lists all crafting recipes added by ELE.", (cmd, args) => 
            {
                if (!Context.IsWorldReady) return;
                var recipes = Game1.content.Load<Dictionary<string, string>>("Data/CraftingRecipes");
                Monitor.Log("--- ELE CRAFTING RECIPES ---", LogLevel.Info);
                foreach(var kvp in recipes) {
                    if (kvp.Value.Contains("JavCombita.ELE")) {
                        Monitor.Log($"- {kvp.Key}", LogLevel.Info);
                    }
                }
            });

            Helper.ConsoleCommands.Add("ele_list_items", "Lists all items defined by ELE.", (cmd, args) => 
            {
                if (!Context.IsWorldReady) return;
                
                Monitor.Log("--- ELE OBJECTS ---", LogLevel.Info);
                var objData = Game1.content.Load<Dictionary<string, StardewValley.GameData.Objects.ObjectData>>("Data/Objects");
                foreach(var kvp in objData) {
                    if (kvp.Key.StartsWith("JavCombita.ELE")) {
                        Monitor.Log($"ID: {kvp.Key} | Name: {kvp.Value.Name}", LogLevel.Info);
                    }
                }

                Monitor.Log("--- ELE BIG CRAFTABLES ---", LogLevel.Info);
                var bigData = Game1.content.Load<Dictionary<string, StardewValley.GameData.BigCraftables.BigCraftableData>>("Data/BigCraftables");
                foreach(var kvp in bigData) {
                    if (kvp.Key.StartsWith("JavCombita.ELE")) {
                        Monitor.Log($"ID: {kvp.Key} | Name: {kvp.Value.Name}", LogLevel.Info);
                    }
                }
            });

            Helper.ConsoleCommands.Add("ele_unlock_recipes", "Unlocks all ELE crafting recipes immediately.", (cmd, args) =>
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

                foreach (var recipe in recipes)
                {
                    if (!Game1.player.craftingRecipes.ContainsKey(recipe))
                    {
                        Game1.player.craftingRecipes.Add(recipe, 0);
                        Monitor.Log($"Unlocked: {recipe}", LogLevel.Info);
                    }
                    else
                    {
                        Monitor.Log($"Already known: {recipe}", LogLevel.Warn);
                    }
                }
            });

            Helper.ConsoleCommands.Add("ele_add_items", "Adds n amount of all ELE items. Usage: ele_add_items <amount>", (cmd, args) =>
            {
                if (!Context.IsWorldReady) return;
                
                int amount = 1;
                if (args.Length > 0 && !int.TryParse(args[0], out amount))
                {
                    Monitor.Log("Invalid amount. Usage: ele_add_items <n>", LogLevel.Error);
                    return;
                }

                // Lista de IDs (Objects y BigCraftables)
                // Usamos Qualified IDs para mayor seguridad en 1.6
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

                foreach (string id in itemIds)
                {
                    Item item = ItemRegistry.Create(id, amount);
                    if (item != null)
                    {
                        Game1.player.addItemByMenuIfNecessary(item);
                    }
                    else
                    {
                        Monitor.Log($"Could not create item: {id}", LogLevel.Error);
                    }
                }
                
                Monitor.Log($"Added {amount} of each ELE item to inventory.", LogLevel.Alert);
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
                
                configMenu.AddTextOption(
                    ModManifest, 
                    () => Config.InvasionDifficulty, 
                    val => Config.InvasionDifficulty = val, 
                    () => Helper.Translation.Get("config.invasionDifficulty"), 
                    null, 
                    new[] { "Easy", "Medium", "Hard", "VeryHard" }
                );
                
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
