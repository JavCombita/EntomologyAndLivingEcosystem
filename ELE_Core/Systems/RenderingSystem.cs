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
        private Texture2D PixelTexture;

        // Cache the Item ID to avoid hardcoded strings in the loop
        private const string AnalyzerItemId = "JavCombita.ELE_SoilAnalyzer";

        public RenderingSystem(ModEntry mod)
        {
            this.Mod = mod;
            
            // Generate a 1x1 white texture in memory for drawing the overlay
            // This is safer and faster than loading an external png for a simple solid color
            this.PixelTexture = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
            this.PixelTexture.SetData(new[] { Color.White });
            
            // Subscribe to HUD rendering for text tooltips
            mod.Helper.Events.Display.RenderedHud += OnRenderedHud;
        }

        /// <summary>
        /// Draws the color overlay on the soil.
        /// </summary>
        public void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            // 1. Basic Checks
            if (!Context.IsWorldReady || Game1.currentLocation == null) return;
            
            // Check if player is holding the Analyzer
            if (!IsPlayerHoldingAnalyzer()) return;

            GameLocation location = Game1.currentLocation;
            SpriteBatch b = e.SpriteBatch;

            // 2. Viewport Optimization (Crucial for Android)
            // Calculate which tiles are currently visible on screen
            int minX = Game1.viewport.X / 64; 
            int minY = Game1.viewport.Y / 64;
            int maxX = (Game1.viewport.X + Game1.viewport.Width) / 64 + 1;
            int maxY = (Game1.viewport.Y + Game1.viewport.Height) / 64 + 1;

            // 3. Iterate only visible tiles
            for (int x = minX; x < maxX; x++)
            {
                for (int y = minY; y < maxY; y++)
                {
                    Vector2 tile = new Vector2(x, y);
                    
                    // Only draw on HoeDirt (tilled soil)
                    if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature tf) && tf is HoeDirt dirt)
                    {
                        // Get Data
                        SoilData data = this.Mod.Ecosystem.GetSoilDataAt(location, tile);
                        
                        // Calculate Color based on Nitrogen (or average NPK)
                        Color overlayColor = CalculateHealthColor(data);

                        // Draw Overlay
                        // Convert World Coordinates to Screen Coordinates
                        Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, tile * 64f);
                        
                        b.Draw(
                            this.PixelTexture, 
                            new Rectangle((int)screenPos.X, (int)screenPos.Y, 64, 64), 
                            overlayColor
                        );
                    }
                }
            }
        }

        /// <summary>
		/// Draws text information if hovering over a tile with the analyzer.
		/// Optimized for Touch/Android visibility.
		/// </summary>
		private void OnRenderedHud(object sender, RenderedHudEventArgs e)
		{
			if (!Context.IsWorldReady || !IsPlayerHoldingAnalyzer()) return;
		
			// Get tile under cursor/finger
			Vector2 cursorTile = Game1.currentCursorTile;
			GameLocation location = Game1.currentLocation;
		
			if (location.terrainFeatures.TryGetValue(cursorTile, out TerrainFeature tf) && tf is HoeDirt)
			{
				SoilData data = this.Mod.Ecosystem.GetSoilDataAt(location, cursorTile);
			
				// Format Text
				string text = this.Mod.Helper.Translation.Get("message.soil_analysis", 
					new { 
						val1 = (int)data.Nitrogen, 
						val2 = (int)data.Phosphorus, 
						val3 = (int)data.Potassium 
					});
		
				// ANDROID TWEAK: Calculate position
				float x = Game1.getMouseX();
				float y = Game1.getMouseY();
		
				// Push the text UP and RIGHT so the user's fat finger doesn't hide it
				// On PC standard is usually +20, +20. On Mobile we want -100 Y (Up)
				Vector2 textPos = new Vector2(x + 32, y - 100);
		
				// Ensure it doesn't go off-screen (Top edge check)
				if (textPos.Y < 0) textPos.Y = y + 64; 
				
				// Ensure it doesn't go off-screen (Right edge check)
				Vector2 textSize = Game1.smallFont.MeasureString(text);
				if (textPos.X + textSize.X > Game1.uiViewport.Width) textPos.X = x - textSize.X - 32;
		
				// Draw black background panel for readability
				Rectangle panelRect = new Rectangle((int)textPos.X - 10, (int)textPos.Y - 10, (int)textSize.X + 20, (int)textSize.Y + 20);
				e.SpriteBatch.Draw(this.PixelTexture, panelRect, new Color(0, 0, 0, 0.75f)); // Darker background
		
				// Draw Text
				e.SpriteBatch.DrawString(Game1.smallFont, text, textPos, Color.White);
			}
		}

        private bool IsPlayerHoldingAnalyzer()
        {
            return Game1.player.CurrentItem != null && 
                   Game1.player.CurrentItem.ItemId == AnalyzerItemId;
        }

        private Color CalculateHealthColor(SoilData data)
        {
            // Logic: Calculate average health percentage (0.0 to 1.0)
            float averageHealth = (data.Nitrogen + data.Phosphorus + data.Potassium) / 300f;
            
            // Interpolate color: Red (Dead) -> Yellow (Warning) -> Green (Healthy)
            Color c;
            if (averageHealth > 0.5f)
            {
                // Green to Yellow
                c = Color.Lerp(Color.Yellow, Color.LimeGreen, (averageHealth - 0.5f) * 2f);
            }
            else
            {
                // Red to Yellow
                c = Color.Lerp(Color.Red, Color.Yellow, averageHealth * 2f);
            }

            // Return with transparency (Alpha 0.4)
            return c * 0.4f;
        }
    }
}