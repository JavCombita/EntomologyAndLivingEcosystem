namespace ELE.Core
{
    /// <summary>
    /// The configuration model for Entomology and Living Ecosystem.
    /// Default values are set here.
    /// </summary>
    public class ModConfig
    {
        // --- Pest System Settings ---
        public bool EnablePestInvasions { get; set; } = true;
        
        // --- Nutrient System Settings ---
        public bool EnableNutrientCycle { get; set; } = true;
        
        // Multiplier for how fast crops consume NPK. 
        // 1.0 = Normal, 0.5 = Slow depletion, 2.0 = Hard mode.
        public float NutrientDepletionMultiplier { get; set; } = 1.0f; 

        // --- Monster Migration Settings ---
        public bool EnableMonsterMigration { get; set; } = true;
        
        // Minimum days played before slimes can invade the town.
        // Prevents new players from being overwhelmed immediately.
        public int DaysBeforeTownInvasion { get; set; } = 15;

        // --- Visual Settings ---
        // If true, the soil analyzer overlay shows automatically when holding the item.
        public bool ShowOverlayOnHold { get; set; } = true;
    }
}