using System;

namespace Kakimaki.BuildHotbar;

internal static class HotbarScale
{
    public static float Step(float value, int direction) =>
        Math.Max(0.5f, Math.Min(2f, (float)Math.Round((value + direction * 0.05f) * 100) / 100));

    public static float Fit(float requested, float width, float height, float availableWidth, float availableHeight) =>
        Math.Max(0.01f, Math.Min(requested, Math.Min(availableWidth / width, availableHeight / height)));
}
