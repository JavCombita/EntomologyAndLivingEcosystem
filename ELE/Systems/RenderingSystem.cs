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
        
        // IDs
        private const string AnalyzerItemId = "JavCombita.ELE_SoilAnalyzer";
        private const string SpreaderBaseId = "JavCombita.ELE_NutrientSpreader";
        private const string ShelterItemId = "JavCombita.ELE_LadybugShelter";
        // NUEVO: ID del Inyector para la retícula
        private const string InjectorItemId = "JavCombita.ELE_AlchemicalInjector";

        public RenderingSystem(ModEntry mod)
        {
            this.Mod = mod;

            // 1. Textura 1x1 para el Overlay
            this.PixelTexture = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
            this.PixelTexture.SetData(new[] { Color.White });

            // Eventos
            mod.Helper.Events.Display.RenderedWorld += OnRenderedWorld;
            mod.Helper.Events.Display.RenderedHud += OnRenderedHud;
        }

        public void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.currentLocation == null) return;

            SpriteBatch b = e.SpriteBatch;

            // 1. DIBUJAR VISTA PREVIA DE RANGO (Si tiene máquina o shelter en mano)
            // (Esta es tu lógica original intacta)
            DrawPlacementRadius(b);

            // 2. DIBUJAR OVERLAY DE NUTRIENTES (Si tiene analizador)
            // (Tu lógica original intacta)
            if (IsPlayerHoldingAnalyzer())
            {
                DrawSoilOverlay(b);
            }

            // 3. NUEVO: DIBUJAR RETÍCULA DEL INYECTOR
            // Solo si tiene el inyector en la mano
            if (Game1.player.CurrentItem != null && Game1.player.CurrentItem.ItemId == InjectorItemId)
            {
                DrawInjectorTarget(b);
            }
        }

        // ========================================================================================
        // LOGICA NUEVA: INYECTOR (Retícula de puntería)
        // ========================================================================================
        private void DrawInjectorTarget(SpriteBatch b)
        {
            // Detectamos el tile al que se está apuntando (Mouse o Frente del jugador)
            Vector2 targetTile = GetTargetTile();
            
            GameLocation loc = Game1.currentLocation;
            
            // Verificamos si es un objetivo válido (Tierra arada con cultivo)
            if (loc.terrainFeatures.TryGetValue(targetTile, out TerrainFeature tf) && tf is HoeDirt dirt && dirt.crop != null)
            {
                // Calcular posición en pantalla
                Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, targetTile * 64f);
                Rectangle rect = new Rectangle((int)screenPos.X, (int)screenPos.Y, 64, 64);

                // Verificar rango (0-1 tiles, igual que en EcosystemManager)
                bool inRange = IsInRange(targetTile);
                
                // Verde si está en rango, Rojo si está lejos
                Color color = inRange ? Color.LimeGreen * 0.5f : Color.Red * 0.5f;

                // Dibujar el borde
                DrawBorder(b, screenPos, 64, 64, color, 4);

                // Indicador extra (X roja) si está fuera de rango para mayor claridad
                if (!inRange)
                {
                    b.Draw(Game1.mouseCursors, screenPos + new Vector2(16, 16), new Rectangle(266, 470, 16, 16), Color.Red, 0f, Vector2.Zero, 2f, SpriteEffects.None, 1f);
                }
            }
        }

        private Vector2 GetTargetTile()
        {
            // Lógica unificada para PC (Mouse) y Android/Gamepad (Frente)
            // Prioridad 1: Mouse (si se ha movido y está dentro del mapa)
            if (Mod.Helper.Input.GetCursorPosition().GetScaledScreenPixels() != Vector2.Zero)
            {
               Vector2 mouseTile = Mod.Helper.Input.GetCursorPosition().Tile;
               if (Game1.currentLocation.isTileOnMap(mouseTile)) 
                   return mouseTile;
            }

            // Prioridad 2: Tile frontal (Android/Controller fallback)
            return new Vector2((int)(Game1.player.GetToolLocation().X / 64f), (int)(Game1.player.GetToolLocation().Y / 64f));
        }

        private bool IsInRange(Vector2 targetTile)
        {
            Vector2 playerTile = Game1.player.Tile;
            // Distancia Chebyshev (radio cuadrado de 1 tile)
            return Math.Abs(targetTile.X - playerTile.X) <= 1 && Math.Abs(targetTile.Y - playerTile.Y) <= 1;
        }

        // ========================================================================================
        // LOGICA EXISTENTE: RADIOS Y OVERLAYS (Intacta)
        // ========================================================================================

        private void DrawPlacementRadius(SpriteBatch b)
        {
            Item heldItem = Game1.player.CurrentItem;
            if (heldItem == null) return;

            int radius = 0;
            bool isCircular = false;
            Color areaColor = new Color(0, 255, 0, 100);
            Color borderColor = new Color(0, 200, 0, 200);

            // 1. Identificar si es Spreader (Cuadrado) o Shelter (Circular)
            if (heldItem.ItemId.Contains(SpreaderBaseId))
            {
                radius = GetRadiusFromItem(heldItem.ItemId);
                isCircular = false; 
            }
            else if (heldItem.ItemId == ShelterItemId)
            {
                radius = 6; 
                isCircular = true;  
                areaColor = new Color(0, 200, 255, 100); 
                borderColor = new Color(0, 150, 200, 200);
            }

            if (radius <= 0) return;

            Vector2 cursorTile = Game1.currentCursorTile;

            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    if (isCircular)
                    {
                        if (Vector2.Distance(Vector2.Zero, new Vector2(x, y)) > radius)
                            continue;
                    }

                    Vector2 targetTile = cursorTile + new Vector2(x, y);
                    Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, targetTile * 64f);

                    // Dibujar fondo
                    b.Draw(this.PixelTexture, 
                           new Rectangle((int)screenPos.X, (int)screenPos.Y, 64, 64), 
                           areaColor * 0.5f);

                    // Dibujar borde
                    DrawBorder(b, screenPos, 64, 64, borderColor, 2);
                }
            }
        }

        private void DrawSoilOverlay(SpriteBatch b)
        {
            GameLocation location = Game1.currentLocation;
            
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

                    if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature tf) && tf is HoeDirt)
                    {
                        SoilData data = this.Mod.Ecosystem.GetSoilDataAt(location, tile);
                        Color overlayColor = CalculateHealthColor(data);
                        Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, tile * 64f);
                        b.Draw(this.PixelTexture, new Rectangle((int)screenPos.X, (int)screenPos.Y, 64, 64), overlayColor);
                    }
                }
            }
        }

        // Modificado ligeramente para aceptar thickness y color parametrizados (compatible con lo anterior y lo nuevo)
        private void DrawBorder(SpriteBatch b, Vector2 pos, int width, int height, Color color, int thickness)
        {
            // Top
            b.Draw(PixelTexture, new Rectangle((int)pos.X, (int)pos.Y, width, thickness), color);
            // Bottom
            b.Draw(PixelTexture, new Rectangle((int)pos.X, (int)pos.Y + height - thickness, width, thickness), color);
            // Left
            b.Draw(PixelTexture, new Rectangle((int)pos.X, (int)pos.Y, thickness, height), color);
            // Right
            b.Draw(PixelTexture, new Rectangle((int)pos.X + width - thickness, (int)pos.Y, thickness, height), color);
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

        private int GetRadiusFromItem(string itemId)
        {
            if (itemId.Contains("Omega")) return 7;
            if (itemId.Contains("Mk3")) return 4;
            if (itemId.Contains("Mk2")) return 3;
            return 2; // Base
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