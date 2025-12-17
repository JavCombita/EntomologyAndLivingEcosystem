using System;

namespace ELE.Core
{
    public class ModConfig
    {
        // ... (Otras opciones existentes) ...
        
        public bool EnablePestInvasions { get; set; } = true;
        public bool EnableMonsterMigration { get; set; } = true;
        public int DaysBeforeTownInvasion { get; set; } = 10;
        
        // --- NUEVO ---
        // Opciones: "Easy", "Medium", "Hard", "VeryHard"
        public string InvasionDifficulty { get; set; } = "Medium"; 

        public bool EnableNutrientCycle { get; set; } = true;
        public float NutrientDepletionMultiplier { get; set; } = 1.0f;
        public bool ShowOverlayOnHold { get; set; } = true;
    }
}