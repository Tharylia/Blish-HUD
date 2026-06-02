using Blish_HUD.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;
using System;
using System.Collections.Generic;
using MonoGame.Extended.Graphics;

namespace Blish_HUD {
    public static class SpriteFontExtensions {
        /// <summary>
        /// Converts a <see cref="SpriteFont"/> to a <see cref="BitmapFontEx"/>.
        /// </summary>
        /// <param name="font">The <see cref="SpriteFont"/> to convert.</param>
        /// <param name="lineHeight">Line height for the <see cref="BitmapFontEx"/>. By default, <see cref="SpriteFont.LineSpacing"/> will be used.</param>
        /// <returns>A <see cref="BitmapFontEx"/> as result of the conversion.</returns>
        public static BitmapFont ToBitmapFont(this SpriteFont font, int lineHeight = 0) {
            if (lineHeight < 0) {
                throw new ArgumentException("Line height cannot be negative.", nameof(lineHeight));
            }

            var regions = new List<BitmapFontCharacter>();

            var glyphs = font.GetGlyphs();

            foreach (var glyph in glyphs.Values) {
                var glyphTextureRegion = new Texture2DRegion(font.Texture,
                                                             glyph.BoundsInTexture.Left,
                                                             glyph.BoundsInTexture.Top,
                                                             glyph.BoundsInTexture.Width,
                                                             glyph.BoundsInTexture.Height);

                var region = new BitmapFontCharacter(glyph.Character,
                                                     glyphTextureRegion,
                                                     glyph.Cropping.Left,
                                                     glyph.Cropping.Top,
                                                     (int)glyph.WidthIncludingBearings);

                regions.Add(region);
            }
            return new BitmapFont($"{typeof(BitmapFont)}_{Guid.NewGuid():n}", font.LineSpacing, lineHeight > 0 ? lineHeight : font.LineSpacing, regions);//, font.Texture);
        }
    }
}
