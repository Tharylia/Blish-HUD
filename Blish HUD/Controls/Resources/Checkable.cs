using System.Collections.Generic;
using MonoGame.Extended.Graphics;

namespace Blish_HUD.Controls.Resources {
    public static class Checkable {

        public static readonly IReadOnlyList<Texture2DRegion> TextureRegionsCheckbox = new List<Texture2DRegion>(new[] {
            Control.TextureAtlasControl.GetRegion("checkbox/cb-unchecked"),
            Control.TextureAtlasControl.GetRegion("checkbox/cb-unchecked-active"),
            Control.TextureAtlasControl.GetRegion("checkbox/cb-unchecked-disabled"),
            Control.TextureAtlasControl.GetRegion("checkbox/cb-checked"),
            Control.TextureAtlasControl.GetRegion("checkbox/cb-checked-active"),
            Control.TextureAtlasControl.GetRegion("checkbox/cb-checked-disabled"),
        });

    }
}
