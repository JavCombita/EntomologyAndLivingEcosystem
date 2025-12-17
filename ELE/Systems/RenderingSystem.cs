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

        // CONFIGURACIÓN DE ANIMACIÓN
        private const int FrameWidth = 16;
        private const int FrameHeight = 32; // CAMBIO: Ahora es 32px de alto (16x32)

        public RenderingSystem(ModEntry mod)
        {
            this.Mod = mod;

            // 1. Textura 1x1 para el Overlay
            this.PixelTexture = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
            this.PixelTexture.SetData(new[] { Color.White });

            // 2. Cargar Textura Animada
            try 
            {
                this.ShelterTexture = mod.Helper.ModContent.Load<Texture2D>("assets/ladybug_shelter_anim.png");
            }
            catch (Exception ex)
            {
                mod.Monitor.Log($"Failed to load animated texture: {ex.Message}. Animation disabled.", LogLevel.Warn);
                this.ShelterTexture = null;
            }

            // Eventos
            mod.Helper.Events.Display.RenderedWorld += OnRenderedWorld;
            mod.Helper.Events.Display.RenderedHud += OnRenderedHud;
        }

        public void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.currentLocation == null) return;

            GameLocation location = Game1.currentLocation;
            SpriteBatch b = e.SpriteBatch;
            bool showingOverlay = IsPlayerHoldingAnalyzer();

            // PERFORMANCE: Viewport culling
            int minX = Game1.viewport.X / 64; 
            int minY = Game1.viewport.Y / 64;
            int maxX = (Game1.viewport.X + Game1.viewport.Width) / 64 + 1;
            int maxY = (Game1.viewport.Y + Game1.viewport.Height) / 64 + 1;

            for (int x = minX; x < maxX; x++)
            {
                for (int y = minY; y < maxY; y++)
                {
                    Vector2 tile = new Vector2(x, y);

                    // 1. DIBUJAR OVERLAY
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

                    // 2. DIBUJAR ANIMACIÓN DEL SHELTER
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

        private void DrawAnimatedShelter(SpriteBatch b, Vector2 tile)
        {
            // Lógica de Frames
            int totalFrames = 4;
            int frameDuration = 250; 
            int currentFrame = (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / frameDuration) % totalFrames;

            // RECORTAR TEXTURA (Ahora usamos FrameHeight 32)
            Rectangle sourceRect = new Rectangle(0, currentFrame * FrameHeight, FrameWidth, FrameHeight);

            // POSICIÓN EN PANTALLA
            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, tile * 64f);

            // --- CORRECCIÓN DE ALTURA ---
            // Como la imagen mide 32px (2 tiles visuales), subimos el dibujo 64 píxeles 
            // para que los "pies" del Shelter toquen el suelo del tile actual.
            screenPos.Y -= 64f; 
            // ----------------------------

            // PROFUNDIDAD (Layer Depth)
            // Usamos la base del objeto ((tile.Y + 1)) para que el jugador pase por detrás del techo correctamente.
            // Agregamos un epsilon pequeño (+0.0001f) para que se dibuje justo encima de la textura estática.
            float layerDepth = ((tile.Y + 1) * 64f) / 10000f + 0.0001f;

            b.Draw(
                this.ShelterTexture,
                screenPos,
                sourceRect,
                Color.White,
                0f,
                Vector2.Zero,
                4f, // Escala 4x
                SpriteEffects.None,
                layerDepth
            );
        }

        private void OnRenderedHud(object sender, RenderedHudEventArgs e)
        {
            if (!Context.IsWorldReady || !IsPlayerHoldingAnalyzer()) return;

            Vector2 cursorTile = Game1.currentCursorTile;
            GameLocation location = Game1.currentLocation;

            if (location.terrainFeatures.TryGetValue(cursorTile, out TerrainFeature tf) && tf is HoeDirt)
            {
                SoilData data = this.Mod.Ecosystem.GetSoilDataAt(location, cursorTile);

                string text = this.Mod.Helper.Translation.Get("message.soil_analysis", 
                    new { 
                        val1 = (int)data.Nitrogen, 
                        val2 = (int)data.Phosphorus, 
                        val3 = (int)data.Potassium 
                    });

                // Android UX Fix
                float x = Game1.getMouseX();
                float y = Game1.getMouseY();
                Vector2 textPos = new Vector2(x + 32, y - 100);

                if (textPos.Y < 0) textPos.Y = y + 64; 
                Vector2 textSize = Game1.smallFont.MeasureString(text);
                if (textPos.X + textSize.X > Game1.uiViewport.Width) textPos.X = x - textSize.X - 32;

                Rectangle panelRect = new Rectangle((int)textPos.X - 10, (int)textPos.Y - 10, (int)textSize.X + 20, (int)textSize.Y + 20);
                e.SpriteBatch.Draw(this.PixelTexture, panelRect, new Color(0, 0, 0, 0.75f));
                e.SpriteBatch.DrawString(Game1.smallFont, text, textPos, Color.White);
            }
        }

        private bool IsPlayerHoldingAnalyzer()
        {
            if (!this.Mod.Config.ShowOverlayOnHold) return false;
            return Game1.player.CurrentItem != null && 
                   Game1.player.CurrentItem.ItemId == AnalyzerItemId;
        }

        private Color CalculateHealthColor(SoilData data)
        {
            float averageHealth = (data.Nitrogen + data.Phosphorus + data.Potassium) / 300f;
            Color c;
            if (averageHealth > 0.5f)
                c = Color.Lerp(Color.Yellow, Color.LimeGreen, (averageHealth - 0.5f) * 2f);
            else
                c = Color.Lerp(Color.Red, Color.Yellow, averageHealth * 2f);

            return c * 0.4f;
        }
    }
}
