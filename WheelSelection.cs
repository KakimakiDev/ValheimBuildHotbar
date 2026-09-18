using System;

namespace Kakimaki.BuildHotbar;

internal static class WheelSelection
{
    // Eight fixed directions, clockwise from up. Hidden slots never redistribute.
    public static int Slot(float x, float y, float deadzone, int count)
    {
        if (count < 1 || count > 8) throw new ArgumentOutOfRangeException(nameof(count));
        if (x * x + y * y <= deadzone * deadzone) return -1;
        double angle = Math.Atan2(x, y);
        int slot = ((int)Math.Floor(angle / (Math.PI / 4) + 0.5) + 8) % 8;
        return slot < count ? slot : -1;
    }
}
