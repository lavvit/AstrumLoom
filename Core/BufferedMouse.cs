namespace AstrumLoom;

/// <summary>メインスレッドで採取し、論理更新で確定する共通マウス入力。</summary>
public abstract class BufferedMouse : IMouse
{
    private readonly object _gate = new();
    private KeyEdgeBuffer _buttons = new(3);
    private double _sampleX, _sampleY, _pendingWheel, _x, _y, _wheel, _total;
    public double X { get { lock (_gate) return _x; } set => Move(value, Y); }
    public double Y { get { lock (_gate) return _y; } set => Move(X, value); }
    public double Wheel { get { lock (_gate) return InputStep.EdgesSuppressed ? 0 : _wheel; } }
    public double WheelTotal { get { lock (_gate) return _total; } }
    protected abstract void SetNativePosition(int x, int y);
    protected abstract void SetNativeVisible(bool visible);
    public abstract void Buffer();
    private void Move(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        lock (_gate) { _x = _sampleX = x; _y = _sampleY = y; }
        if (Environment.CurrentManagedThreadId == AstrumCore.MainThreadId) SetNativePosition((int)x, (int)y);
        else AstrumCore.RequestToMainThread(() => SetNativePosition((int)x, (int)y));
    }
    public void Init(bool visible)
    {
        lock (_gate) { _buttons = new(3); _pendingWheel = _wheel = _total = 0; }
        SetNativeVisible(visible);
    }
    protected void Sample(double x, double y, double wheel, int buttons)
    {
        lock (_gate)
        {
            _sampleX = x; _sampleY = y; _pendingWheel += wheel;
            for (int i = 0; i < 3; i++) _buttons.Sample(i, (buttons & (1 << i)) != 0);
        }
    }
    public void Update()
    {
        lock (_gate)
        {
            _buttons.Commit(); _x = _sampleX; _y = _sampleY;
            _wheel = _pendingWheel; _pendingWheel = 0; _total += _wheel;
        }
    }
    public bool Push(MouseButton button) { lock (_gate) return !InputStep.EdgesSuppressed && _buttons.GetKeyDown((int)button); }
    public bool Hold(MouseButton button) { lock (_gate) return _buttons.GetKey((int)button); }
    public bool Left(MouseButton button) { lock (_gate) return !InputStep.EdgesSuppressed && _buttons.GetKeyUp((int)button); }
}
