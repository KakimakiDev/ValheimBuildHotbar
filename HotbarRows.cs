using System;

namespace Kakimaki.BuildHotbar;

internal static class HotbarRows
{
    // One-based rows, wrap in either direction. Clamp stale configuration first.
    public static int Step(int current, int count, int direction)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        current = Math.Max(1, Math.Min(current, count));
        return ((current - 1 + direction % count + count) % count) + 1;
    }
}
