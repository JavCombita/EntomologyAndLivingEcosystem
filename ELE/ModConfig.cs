using System;

namespace ELE.Core
{
    public class ModConfig
    {
        // --- General Settings ---
        
        /// <summary>If enabled, crops may be attacked by pests if biodiversity is low.</summary>
        public bool EnablePestInvasions { get; set; } = true;

        /// <summary>If enabled, monsters will periodically invade the farm.</summary>
        public bool EnableMonsterMigration { get; set; } = true;

        /// <summary>Minimum days played before invasions can start.</summary>
        public int DaysBeforeTownInvasion { get; set; } = 20;

        /// <summary>Difficulty of the invasion (Easy, Medium, Hard, VeryHard).</summary>
        public string InvasionDifficulty { get; set; } = "Medium";

        // --- Nutrient Cycle ---

        /// <summary>If enabled, crops consume N/P/K from the soil.</summary>
        public bool EnableNutrientCycle { get; set; } = true;

        /// <summary>Multiplier for nutrient consumption rate (1.0 = standard).</summary>
        public float NutrientDepletionMultiplier { get; set; } = 1.0f;

        // --- Visuals ---

        /// <summary>If true, holding the Analyzer automatically renders the soil overlay.</summary>
        public bool ShowOverlayOnHold { get; set; } = true;
    }
}