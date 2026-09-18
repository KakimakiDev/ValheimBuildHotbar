using System;

namespace Kakimaki.BuildHotbar;

[Flags]
public enum WheelButtons
{
    None = 0, Cross_A = 1, Circle_B = 2, Square_X = 4, Triangle_Y = 8,
    LeftBumper = 16, RightBumper = 32, LeftTrigger = 64, RightTrigger = 128,
    LeftStickClick = 256, RightStickClick = 512
}

public sealed partial class BuildHotbarPlugin
{
    private static readonly string[] WheelButtonActions = { "JoyButtonA", "JoyButtonB", "JoyButtonX", "JoyButtonY",
        "JoyLBumper", "JoyRBumper", "JoyLTrigger", "JoyRTrigger", "JoyLStick", "JoyRStick" };

    private static bool ShortcutHeld(WheelButtons buttons)
    {
        int mask = (int)buttons;
        if (mask == 0 || (mask & ~1023) != 0) return false;
        for (int i = 0; i < WheelButtonActions.Length; i++)
            if ((mask & (1 << i)) != 0 && !ZInput.GetButton(WheelButtonActions[i])) return false;
        return true;
    }
}
