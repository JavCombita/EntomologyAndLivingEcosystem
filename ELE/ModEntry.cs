using System;
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
