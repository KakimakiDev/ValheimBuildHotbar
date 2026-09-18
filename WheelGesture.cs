namespace Kakimaki.BuildHotbar;

internal sealed class WheelGesture
{
    public int Selected { get; private set; } = -1;
    private bool _waitForCentre;
    public void Reset() { Selected = -1; _waitForCentre = false; }
    public void ClearHighlight() { Selected = -1; _waitForCentre = true; }

    public void Update(float x, float y, float deadzone, int count)
    {
        if (_waitForCentre)
        {
            if (x * x + y * y <= deadzone * deadzone) _waitForCentre = false;
            return;
        }
        // Centre/drift preserves the last highlight; only the opener release confirms.
        if (x * x + y * y > deadzone * deadzone)
            Selected = WheelSelection.Slot(x, y, deadzone, count);
    }
}
