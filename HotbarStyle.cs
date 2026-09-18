using UnityEngine;

namespace Kakimaki.BuildHotbar;

public sealed partial class BuildHotbarPlugin
{
    // A fill-only treatment: native prefab images may be white masks whose
    // final appearance depends on a material, so do not sample their texture.
    private static readonly Color HotbarBackground = new Color(0.035f, 0.035f, 0.03f, 0.5f);
    private static readonly Color HotbarHighlight = new Color(0.24f, 0.19f, 0.10f, 0.6f);

    private static Color HotbarFill(bool selected)
    {
        return selected ? HotbarHighlight : HotbarBackground;
    }
}
