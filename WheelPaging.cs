namespace Kakimaki.BuildHotbar;

internal sealed class WheelPaging
{
    private bool _latched;
    private float _nextPress;

    public void Reset(bool left, bool right)
    {
        _latched = left || right;
        _nextPress = float.NegativeInfinity;
    }

    // Use held-state edges, not the game's repeatable menu press events.
    // Both directions must release before another press; a brief bounce is ignored.
    public int Update(bool left, bool right, float time)
    {
        if (!left && !right) { _latched = false; return 0; }
        if (_latched) return 0;
        _latched = true;
        if (left == right || time < _nextPress) return 0;
        _nextPress = time + 0.12f;
        return right ? 1 : -1;
    }
}
