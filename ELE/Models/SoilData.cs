namespace ELE.Core.Models
{
    public class SoilData
    {
        public float Nitrogen { get; set; } = 100f;
        public float Phosphorus { get; set; } = 100f;
        public float Potassium { get; set; } = 100f;

        // Serialization helper for ModData
        public override string ToString()
        {
            return $"{Nitrogen}:{Phosphorus}:{Potassium}";
        }

        public static SoilData FromString(string data)
        {
            if (string.IsNullOrEmpty(data)) return new SoilData();
            var parts = data.Split(':');
            if (parts.Length < 3) return new SoilData();

            return new SoilData
            {
                Nitrogen = float.Parse(parts[0]),
                Phosphorus = float.Parse(parts[1]),
                Potassium = float.Parse(parts[2])
            };
        }
    }
}