using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.TerrainFeatures;
using ELE.Core.Models;

namespace ELE.Core.Systems
{
    public class RenderingSystem
    {
        private readonly ModEntry Mod;
        
        // Texturas
        private Texture2D PixelTexture;
        private Texture2D ShelterTexture;

        // IDs (Cache)
        private const string AnalyzerItemId = "JavCombita.ELE_SoilAnalyzer";
        private const string ShelterItemId = "JavCombita.ELE_LadybugShelter";

        public RenderingSystem(ModEntry mod)
        {
            this.Mod = mod;

            // 1. Textura 1x1 para el Overlay (Generada en memoria)
            this.PixelTexture = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
            this.PixelTexture.SetData(new[] { Color.White });

            // 2. Cargar Textura Animada del Shelter
            // IMPORTANTE: Asegúrate de que 'ladybug_shelter_anim.png' esté en la carpeta 'assets' de ELE_Core
            try 
            {
                this.ShelterTexture = mod.Helper.ModContent.Load<Texture2D>("assets/ladybug_shelter_anim.png");
            }
            catch (Exception ex)
            {
                mod.Monitor.Log($"Failed to load animated texture: {ex.Message}. Animation disabled.", LogLevel.Warn);
                this.ShelterTexture = null;
            }

            // Eventos de dibujado
            mod.Helper.Events.Display.RenderedWorld += OnRenderedWorld;
            mod.Helper.Events.Display.RenderedHud += OnRenderedHud;
        }

        /// <summary>
        /// Dibuja capas sobre el mundo del juego (Overlay y Animaciones).
        /// </summary>
        public void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.currentLocation == null) return;

            GameLocation location = Game1.currentLocation;
            SpriteBatch b = e.SpriteBatch;
            bool showingOverlay = IsPlayerHoldingAnalyzer();

            // PERFORMANCE: Calcular Viewport (Solo iteramos lo que se ve en pantalla)
            int minX = Game1.viewport.X / 64; 
            int minY = Game1.viewport.Y / 64;
            int maxX = (Game1.viewport.X + Game1.viewport.Width) / 64 + 1;
            int maxY = (Game1.viewport.Y + Game1.viewport.Height) / 64 + 1;

            for (int x = minX; x < maxX; x++)
            {
                for (int y = minY; y < maxY; y++)
                {
                    Vector2 tile = new Vector2(x, y);

                    // --- 1. DIBUJAR OVERLAY (Si tiene el Analyzer) ---
                    if (showingOverlay)
                    {
                        if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature tf) && tf is HoeDirt)
                        {
                            SoilData data = this.Mod.Ecosystem.GetSoilDataAt(location, tile);
                            Color overlayColor = CalculateHealthColor(data);
                            
                            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, tile * 64f);
                            b.Draw(this.PixelTexture, new Rectangle((int)screenPos.X, (int)screenPos.Y, 64, 64), overlayColor);
                        }
                    }

                    // --- 2. DIBUJAR ANIMACIÓN DEL SHELTER ---
                    // Si encontramos el objeto estático, dibujamos la animación encima
                    if (this.ShelterTexture != null && location.Objects.TryGetValue(tile, out StardewValley.Object obj))
                    {
                        if (obj.ItemId == ShelterItemId)
                        {
                            DrawAnimatedShelter(b, tile);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Dibuja el sprite animado ciclando frames.
        /// </summary>
        private void DrawAnimatedShelter(SpriteBatch b, Vector2 tile)
        {
            // Lógica de Animación: 4 Frames, 250ms cada uno
            int totalFrames = 4;
            int frameDuration = 250; 
            int currentFrame = (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / frameDuration) % totalFrames;

            // Recorte del Sprite Sheet Vertical (16x64)
            Rectangle sourceRect = new Rectangle(0, currentFrame * 16, 16, 16);

            // Posición en Pantalla
            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, tile * 64f);

            // Profundidad (Layer Depth)
            // Calculamos la profundidad estándar de Stardew y le sumamos un pelín
            // para asegurar que se dibuje ENCIMA del objeto estático que pone Content Patcher.
            float layerDepth = ((tile.Y + 1) * 64f / 10000f) + (tile.X / 100000f) + 0.001f;

            b.Draw(
                this.ShelterTexture,
                screenPos,
                sourceRect,
                Color.White,
                0f,
                Vector2.Zero,
                4f, // Escala 4x (Estándar del juego)
                SpriteEffects.None,
                layerDepth
            );
        }

        /// <summary>
        /// Dibuja el tooltip flotante con info del suelo.
        /// Optimizado para Android (Texto arriba del dedo).
        /// </summary>
        private void OnRenderedHud(object sender, RenderedHudEventArgs e)
        {
            if (!Context.IsWorldReady || !IsPlayerHoldingAnalyzer()) return;

            // Obtener tile bajo el cursor/dedo
            Vector2 cursorTile = Game1.currentCursorTile;
            GameLocation location = Game1.currentLocation;

            if (location.terrainFeatures.TryGetValue(cursorTile, out TerrainFeature tf) && tf is HoeDirt)
            {
                SoilData data = this.Mod.Ecosystem.GetSoilDataAt(location, cursorTile);

                // Formatear Texto (Usa translation keys)
                string text = this.Mod.Helper.Translation.Get("message.soil_analysis", 
                    new { 
                        val1 = (int)data.Nitrogen, 
                        val2 = (int)data.Phosphorus, 
                        val3 = (int)data.Potassium 
                    });

                // --- ANDROID UX FIX ---
                float x = Game1.getMouseX();
                float y = Game1.getMouseY();

                // Mover el texto ARRIBA (-100 px) para que el dedo no lo tape
                Vector2 textPos = new Vector2(x + 32, y - 100);

                // Evitar que se salga por arriba de la pantalla
                if (textPos.Y < 0) textPos.Y = y + 64; 
                
                // Evitar que se salga por la derecha
                Vector2 textSize = Game1.smallFont.MeasureString(text);
                if (textPos.X + textSize.X > Game1.uiViewport.Width) textPos.X = x - textSize.X - 32;

                // Dibujar caja negra de fondo
                Rectangle panelRect = new Rectangle((int)textPos.X - 10, (int)textPos.Y - 10, (int)textSize.X + 20, (int)textSize.Y + 20);
                e.SpriteBatch.Draw(this.PixelTexture, panelRect, new Color(0, 0, 0, 0.75f));

                // Dibujar Texto
                e.SpriteBatch.DrawString(Game1.smallFont, text, textPos, Color.White);
            }
        }

        private bool IsPlayerHoldingAnalyzer()
        {
            // Si la config dice que no mostremos overlay automático, retornamos false
            if (!this.Mod.Config.ShowOverlayOnHold) return false;

            return Game1.player.CurrentItem != null && 
                   Game1.player.CurrentItem.ItemId == AnalyzerItemId;
        }

        private Color CalculateHealthColor(SoilData data)
        {
            // Calcular salud promedio (0.0 a 1.0)
            float averageHealth = (data.Nitrogen + data.Phosphorus + data.Potassium) / 300f;
            
            // Lógica de color semáforo
            Color c;
            if (averageHealth > 0.5f)
            {
                // Verde a Amarillo
                c = Color.Lerp(Color.Yellow, Color.LimeGreen, (averageHealth - 0.5f) * 2f);
            }
            else
            {
                // Rojo a Amarillo
                c = Color.Lerp(Color.Red, Color.Yellow, averageHealth * 2f);
            }

            // Transparencia (Alpha 0.4)
            return c * 0.4f;
        }
    }
}