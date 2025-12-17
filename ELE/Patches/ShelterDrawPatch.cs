using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using HarmonyLib;
using StardewValley;

namespace ELE.Core.Patches
{
    // Le decimos a Harmony: "Busca el método 'draw' dentro de la clase 'StardewValley.Object'"
    [HarmonyPatch(typeof(StardewValley.Object), nameof(StardewValley.Object.draw), new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) })]
    public static class ShelterDrawPatch
    {
        // "Prefix" se ejecuta ANTES de que el juego dibuje el objeto normal.
        public static bool Prefix(StardewValley.Object __instance, SpriteBatch spriteBatch, int x, int y, float alpha)
        {
            // 1. Verificamos si este objeto es nuestro Shelter
            if (__instance.ItemId != "JavCombita.ELE_LadybugShelter") 
                return true; // Si no es, devuelve TRUE para que el juego dibuje el objeto normal.

            // 2. Si es el Shelter, dibujamos nuestra animación personalizada
            Texture2D texture = ModEntry.ShelterTexture;
            if (texture == null) return true; // Si falló la carga, dibuja el normal

            // --- Lógica de Animación ---
            int totalFrames = 4;
            int frameDuration = 250;
            int currentFrame = (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / frameDuration) % totalFrames;

            // Recorte (16x32 px)
            Rectangle sourceRect = new Rectangle(0, currentFrame * 32, 16, 32);

            // --- Posición ---
            // Convertimos Tile (x, y) a Pixeles de Pantalla
            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64, y * 64));
            
            // CORRECCIÓN VISUAL: Subimos 64px porque es un objeto de 2 tiles de alto
            screenPos.Y -= 64f; 

            // --- LAYER DEPTH (Profundidad) ---
            // Esta es la fórmula mágica. Usamos la base del objeto ((y + 1) * 64) para ordenar.
            // Si el jugador está en 'y', sus pies están en 'y', el objeto está en 'y+1', así que el objeto tapa al jugador.
            // Si el jugador está en 'y+1', sus pies están en 'y+1', compiten, el juego usa la Y precisa.
            float layerDepth = Math.Max(0f, ((y + 1) * 64) / 10000f) + x * 1E-05f;

            // Dibujar
            spriteBatch.Draw(
                texture,
                screenPos,
                sourceRect,
                Color.White * alpha, // Respetamos la transparencia (ej: al colocarlo)
                0f,
                Vector2.Zero,
                4f, // Escala 4x
                SpriteEffects.None,
                layerDepth
            );

            // 3. Devolvemos FALSE para decirle al juego: "Ya lo dibujé yo, no dibujes la versión estática fea".
            return false; 
        }
    }
}
