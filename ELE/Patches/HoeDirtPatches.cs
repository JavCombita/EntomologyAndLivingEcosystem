using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using ELE.Core.Systems;

namespace ELE.Core.Patches
{
    [HarmonyPatch(typeof(HoeDirt))]
    public static class HoeDirtPatches
    {
        // Interceptamos el método 'plant' de HoeDirt
        [HarmonyPatch(nameof(HoeDirt.plant))]
        [HarmonyPostfix]
        public static void Postfix_Plant(HoeDirt __instance, string itemId, Farmer who, bool isFertilizer, bool __result)
        {
            // 1. Si la plantación falló (__result == false), no hacemos nada.
            // 2. Si no es fertilizante, no hacemos nada.
            if (!__result || !isFertilizer) return;

            // 3. Obtenemos la ubicación y posición
            // HoeDirt no siempre tiene una referencia fácil a su Location en versiones viejas, 
            // pero en 1.6 suele tenerla o la inferimos.
            // Una forma segura en 1.6 es usar __instance.Location si existe, o Game1.currentLocation (riesgoso pero usualmente correcto para acciones del jugador)
            
            // Vamos a intentar obtener la ubicación de forma segura.
            // Nota: En Harmony, no podemos acceder a 'ModEntry.Instance' fácilmente si no es estático.
            // Usaremos el Singleton que creamos en ModEntry.cs
            
            if (ModEntry.Instance != null)
            {
                // Asumimos que la acción ocurre en la ubicación actual del jugador si 'who' es el jugador local
                if (who.IsLocalPlayer)
                {
                    ModEntry.Instance.Ecosystem.RestoreNutrients(who.currentLocation, __instance.Tile, itemId);
                }
            }
        }
    }
}