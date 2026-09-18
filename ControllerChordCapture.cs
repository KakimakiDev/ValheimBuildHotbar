namespace Kakimaki.BuildHotbar;

internal sealed class ControllerChordCapture
{
    public int Buttons { get; private set; }
    private bool _ready;
    public void Reset() { Buttons = 0; _ready = false; }
    public bool Sample(int held)
    {
        // The confirm button used to enter recording must not become the binding.
        if (!_ready) { _ready = held == 0; return false; }
        if (held != 0) { Buttons |= held; return false; }
        return Buttons != 0;
    }
}
