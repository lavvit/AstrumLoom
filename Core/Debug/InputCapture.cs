using System.Globalization;
using System.Text;

namespace AstrumLoom;

/// <summary>1 論理フレーム分の入力状態。記録・再生の最小単位。</summary>
public readonly struct InputFrame
{
    /// <summary>この状態が有効になった論理フレーム番号。</summary>
    public required long Frame { get; init; }
    /// <summary>そのフレームで押されているキーの集合。</summary>
    public required Key[] Keys { get; init; }
    public required double MouseX { get; init; }
    public required double MouseY { get; init; }
    /// <summary>ホイールの累積回転量。差分ではなく総量を持つことで、記録の欠落フレームがあっても復元できる。</summary>
    public required double WheelTotal { get; init; }
    /// <summary>bit0=左 bit1=右 bit2=中。</summary>
    public required int Buttons { get; init; }

    /// <summary>直前に記録した状態と同一かどうか。差分がなければ記録行を書かずに済ませる。</summary>
    public bool SameStateAs(in InputFrame other)
        => Buttons == other.Buttons
        && MouseX == other.MouseX
        && MouseY == other.MouseY
        && WheelTotal == other.WheelTotal
        && Keys.Length == other.Keys.Length
        && !Keys.Except(other.Keys).Any();
}

/// <summary>
/// テストやツールからキー入力を合成するための注入口。
/// ここに入れたキーは、実際に押されているものと同じように扱われます。
/// </summary>
public static class VirtualInput
{
    private static readonly HashSet<Key> _held = [];
    // 前回の取り込み以降に一度でも押されたキー。押して離すまでが 1 回の取り込みの中に
    // 収まっても押下エッジを落とさないための持ち越し。
    private static readonly HashSet<Key> _pressedSinceCopy = [];

    /// <summary>合成入力を使っているか。</summary>
    public static bool Active { get; private set; }

    /// <summary>キーを合成入力として押下状態にします。以後 Active が true になり、実入力より合成入力が優先されます。</summary>
    public static void Press(Key key)
    {
        if (key == Key.None) return;
        lock (_held) { _held.Add(key); _pressedSinceCopy.Add(key); Active = true; }
    }
    /// <summary>合成入力のキーを解放します。</summary>
    public static void Release(Key key)
    {
        lock (_held) _held.Remove(key);
    }
    /// <summary>
    /// 合成入力をすべて解除し、以後はプラットフォームの入力をそのまま使う状態に戻します。
    /// Active を落とさないと、合成を使い終わったあとも押下エッジを差分から導き続けてしまう。
    /// </summary>
    public static void ReleaseAll()
    {
        lock (_held)
        {
            _held.Clear();
            _pressedSinceCopy.Clear();
            Active = false;
        }
    }
    /// <summary>キーが合成入力として押されているか。</summary>
    public static bool IsHeld(Key key)
    {
        lock (_held) return _held.Contains(key);
    }
    /// <summary>合成入力中のキーを target に加えます。InputBridge.Update が実入力の押下集合に合成する用途。</summary>
    internal static void CopyInto(HashSet<Key> target)
    {
        lock (_held)
        {
            foreach (var k in _held) target.Add(k);

            // 押下集合の取り込みは 1 反復に 1 回だが、論理フレームはキャッチアップで
            // 1 反復に複数走ることがある（INVARIANTS「キャッチアップで複数ステップ走る場合、
            // それらは同じ入力を共有します」）。押して離すまでがその中に収まると、
            // 取り込みから見れば何も起きていないことになり、Push も Left も出ない。
            // 一度でも押されたキーはこの 1 回だけ押下として混ぜ、次の取り込みで離鍵にする。
            foreach (var k in _pressedSinceCopy) target.Add(k);
            _pressedSinceCopy.Clear();
        }
    }
}

/// <summary>
/// プラットフォームの入力を包み、記録・再生・合成入力を差し込むブリッジ。
/// </summary>
internal sealed class InputBridge : IInput
{
    private readonly IInput _inner;
    private readonly InputRecorder? _recorder;
    private readonly InputPlayer? _player;

    private HashSet<Key> _held = [];
    private HashSet<Key> _prevHeld = [];

    internal InputBridge(IInput inner, InputRecorder? recorder, InputPlayer? player)
    {
        _inner = inner;
        _recorder = recorder;
        _player = player;
    }

    /// <summary>再生中はプラットフォームの入力を無視します。</summary>
    private bool Replaying => _player != null;

    public void Buffer() => _inner.Buffer();

    /// <summary>
    /// 生入力を 1 フレーム進めます。<see cref="GameRunner"/> がループ 1 回につき 1 度だけ呼びます。
    /// ここで進めておくことで、入力の確定より前にデバッグホットキーを判定できます。
    /// </summary>
    internal void PreUpdate() => _inner.Update();

    /// <summary>
    /// 押下集合を組み直します。生入力の前進は <see cref="PreUpdate"/> 済みなのでここでは行いません。
    /// </summary>
    public void Update()
    {
        (_prevHeld, _held) = (_held, _prevHeld);
        _held.Clear();

        if (_player != null)
        {
            foreach (var k in _player.Current.Keys) _held.Add(k);
        }
        else
        {
            foreach (var k in KeyInput.GetAllKeys())
                if (_inner.GetKey(k)) _held.Add(k);
            VirtualInput.CopyInto(_held);
        }

        _recorder?.NoteKeys(_held);
    }

    public bool GetKey(Key key)
        => Replaying || VirtualInput.Active ? _held.Contains(key) : _inner.GetKey(key);

    public bool GetKeyDown(Key key)
        => Replaying || VirtualInput.Active
            ? _held.Contains(key) && !_prevHeld.Contains(key)
            : _inner.GetKeyDown(key);

    public bool GetKeyUp(Key key)
        => Replaying || VirtualInput.Active
            ? !_held.Contains(key) && _prevHeld.Contains(key)
            : _inner.GetKeyUp(key);
}

/// <summary>プラットフォームのマウスを包み、記録・再生を差し込むブリッジ。</summary>
internal sealed class MouseBridge : IMouse
{
    private readonly IMouse _inner;
    private readonly InputRecorder? _recorder;
    private readonly InputPlayer? _player;

    private int _buttons;
    private int _prevButtons;
    private double _wheelTotal;
    private double _wheel;

    internal MouseBridge(IMouse inner, InputRecorder? recorder, InputPlayer? player)
    {
        _inner = inner;
        _recorder = recorder;
        _player = player;
    }

    private bool Replaying => _player != null;

    public double X
    {
        get => Replaying ? _player!.Current.MouseX : _inner.X;
        set { if (!Replaying) _inner.X = value; }
    }
    public double Y
    {
        get => Replaying ? _player!.Current.MouseY : _inner.Y;
        set { if (!Replaying) _inner.Y = value; }
    }
    public double Wheel => Replaying ? _wheel : _inner.Wheel;
    public double WheelTotal => Replaying ? _wheelTotal : _inner.WheelTotal;

    public void Init(bool visible) => _inner.Init(visible);

    public void Update()
    {
        _inner.Update();
        _prevButtons = _buttons;

        if (_player != null)
        {
            var f = _player.Current;
            _buttons = f.Buttons;
            _wheel = f.WheelTotal - _wheelTotal;
            _wheelTotal = f.WheelTotal;
        }
        else
        {
            _buttons = 0;
            if (_inner.Hold(MouseButton.Left)) _buttons |= 1;
            if (_inner.Hold(MouseButton.Right)) _buttons |= 2;
            if (_inner.Hold(MouseButton.Middle)) _buttons |= 4;
            _recorder?.NoteMouse(_inner.X, _inner.Y, _inner.WheelTotal, _buttons);
        }
    }

    /// <summary>ボタン種別を Buttons のビットフラグに変換します。</summary>
    private static int Bit(MouseButton b) => b switch
    {
        MouseButton.Left => 1,
        MouseButton.Right => 2,
        MouseButton.Middle => 4,
        _ => 0,
    };

    public bool Push(MouseButton button)
        => Replaying ? (_buttons & Bit(button)) != 0 && (_prevButtons & Bit(button)) == 0 : _inner.Push(button);
    public bool Hold(MouseButton button)
        => Replaying ? (_buttons & Bit(button)) != 0 : _inner.Hold(button);
    public bool Left(MouseButton button)
        => Replaying ? (_buttons & Bit(button)) == 0 && (_prevButtons & Bit(button)) != 0 : _inner.Left(button);
}

/// <summary>入力をテキストファイルへ記録します。状態が変わったフレームだけを書きます。</summary>
internal sealed class InputRecorder
{
    private readonly string _path;
    private readonly List<string> _lines = [];
    private InputFrame _last;
    private bool _hasLast;

    private Key[] _keys = [];
    private double _mx, _my, _wheel;
    private int _buttons;

    internal InputRecorder(string path, GameConfig config)
    {
        _path = path;
        _lines.Add("# AstrumLoom input recording");
        _lines.Add($"v {InputCapture.FormatVersion}");
        _lines.Add($"hz {config.FixedUpdateHz.ToString("R", CultureInfo.InvariantCulture)}");
        _lines.Add($"seed {config.Seed?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        _lines.Add($"backend {config.GraphicsBackend}");
        _lines.Add($"size {config.Width}x{config.Height}");
    }

    /// <summary>今フレームの押下キー集合を記憶します。実際の書き込みは Commit まで遅延させます。</summary>
    internal void NoteKeys(IEnumerable<Key> keys) => _keys = [.. keys];
    /// <summary>今フレームのマウス状態を記憶します。</summary>
    internal void NoteMouse(double x, double y, double wheelTotal, int buttons)
    {
        _mx = x; _my = y; _wheel = wheelTotal; _buttons = buttons;
    }

    /// <summary>1 論理フレーム分を確定させます。</summary>
    internal void Commit(long frame)
    {
        var f = new InputFrame
        {
            Frame = frame,
            Keys = _keys,
            MouseX = Math.Round(_mx, 2),
            MouseY = Math.Round(_my, 2),
            WheelTotal = Math.Round(_wheel, 3),
            Buttons = _buttons,
        };
        if (_hasLast && f.SameStateAs(_last)) return;

        _last = f;
        _hasLast = true;
        _lines.Add(InputCapture.FormatLine(f));
    }

    internal void Save(long finalFrame)
    {
        try
        {
            string full = InputCapture.Resolve(_path);
            string? dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var body = new List<string>(_lines) { $"end {finalFrame}" };
            File.WriteAllLines(full, body, new UTF8Encoding(true));
            Log.Write($"入力を記録しました: {AstrumCore.FilePath(full)} ({finalFrame} フレーム)");
        }
        catch (Exception ex)
        {
            Log.Error($"入力の記録に失敗しました: {ex.Message}");
        }
    }
}

/// <summary>記録した入力を再生します。</summary>
internal sealed class InputPlayer
{
    private readonly List<InputFrame> _frames;
    private int _cursor;

    /// <summary>記録の最終フレーム。ここを過ぎたら再生終了。</summary>
    internal long EndFrame { get; }
    internal double Hz { get; }
    internal int? Seed { get; }
    internal bool Finished { get; private set; }

    internal InputFrame Current { get; private set; }

    private InputPlayer(List<InputFrame> frames, long endFrame, double hz, int? seed)
    {
        _frames = frames;
        EndFrame = endFrame;
        Hz = hz;
        Seed = seed;
        Current = frames.Count > 0 ? frames[0] : InputCapture.Empty(0);
    }

    /// <summary>読み込みに失敗したら null を返します。</summary>
    internal static InputPlayer? Load(string path)
    {
        try
        {
            string full = InputCapture.Resolve(path);
            if (!File.Exists(full))
            {
                Log.Error($"再生ファイルが見つかりません: {AstrumCore.FilePath(full)}");
                return null;
            }

            var frames = new List<InputFrame>();
            long end = 0;
            double hz = 60;
            int? seed = null;

            foreach (string raw in File.ReadAllLines(full))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line.StartsWith("hz ", StringComparison.Ordinal))
                {
                    if (double.TryParse(line[3..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
                        hz = h;
                    continue;
                }
                if (line.StartsWith("seed ", StringComparison.Ordinal))
                {
                    if (int.TryParse(line[5..].Trim(), out int s)) seed = s;
                    continue;
                }
                if (line.StartsWith("end ", StringComparison.Ordinal))
                {
                    long.TryParse(line[4..].Trim(), out end);
                    continue;
                }
                if (line.StartsWith('v') || line.StartsWith("backend", StringComparison.Ordinal)
                    || line.StartsWith("size", StringComparison.Ordinal))
                    continue;

                if (InputCapture.TryParseLine(line, out var f)) frames.Add(f);
            }

            if (frames.Count == 0)
            {
                Log.Error($"再生ファイルに入力フレームがありません: {AstrumCore.FilePath(full)}");
                return null;
            }

            frames.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            if (end <= 0) end = frames[^1].Frame;
            Log.Write($"入力を再生します: {AstrumCore.FilePath(full)} ({frames.Count} 変化点 / {end} フレーム)");
            return new InputPlayer(frames, end, hz, seed);
        }
        catch (Exception ex)
        {
            Log.Error($"再生ファイルの読み込みに失敗しました: {ex.Message}");
            return null;
        }
    }

    /// <summary>指定フレームの状態までカーソルを進めます。</summary>
    internal void Seek(long frame)
    {
        while (_cursor + 1 < _frames.Count && _frames[_cursor + 1].Frame <= frame)
            _cursor++;
        Current = _frames[_cursor];
        if (frame >= EndFrame) Finished = true;
    }
}

/// <summary>記録ファイルの書式まわりと、ブリッジの組み立て。</summary>
internal static class InputCapture
{
    internal const int FormatVersion = 1;

    internal static InputBridge? Bridge { get; private set; }
    internal static MouseBridge? MouseWrapper { get; private set; }
    internal static InputRecorder? Recorder { get; private set; }
    internal static InputPlayer? Player { get; private set; }

    /// <summary>再生が最終フレームまで到達したか。</summary>
    internal static bool ReplayFinished => Player?.Finished ?? false;

    internal static InputFrame Empty(long frame) => new()
    {
        Frame = frame,
        Keys = [],
        MouseX = 0,
        MouseY = 0,
        WheelTotal = 0,
        Buttons = 0,
    };

    internal static string Resolve(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(AstrumCore.AppPath, path);

    /// <summary>記録・再生・合成入力のためにプラットフォーム入力を包みます。</summary>
    internal static (IInput input, IMouse mouse) Install(
        IGamePlatform platform, GameConfig config, LaunchOptions options)
    {
        Recorder = options.RecordPath != null ? new InputRecorder(options.RecordPath, config) : null;
        Player = options.ReplayPath != null ? InputPlayer.Load(options.ReplayPath) : null;

        if (options.ReplayPath != null && Player == null)
        {
            // 再生できないのに再生したつもりで走ると、結果を誤解する。ここで止める。
            throw new InvalidOperationException($"入力の再生を開始できませんでした: {options.ReplayPath}");
        }

        Bridge = new InputBridge(platform.Input, Recorder, Player);
        MouseWrapper = new MouseBridge(platform.Mouse, Recorder, Player);
        return (Bridge, MouseWrapper);
    }

    /// <summary>
    /// このフレームで再生すべき状態を用意します。入力を確定させる「前」に呼びます。
    /// </summary>
    internal static void BeginFrame(long frame) => Player?.Seek(frame);

    /// <summary>
    /// このフレームの入力を記録に確定させます。入力を確定させた「後」に呼びます。
    /// ここを Begin と同じ場所でやると、1 フレーム前の状態を書いてしまう。
    /// </summary>
    internal static void EndFrame(long frame) => Recorder?.Commit(frame);

    internal static void Finish(long finalFrame) => Recorder?.Save(finalFrame);

    internal static void Reset()
    {
        Bridge = null;
        MouseWrapper = null;
        Recorder = null;
        Player = null;
    }

    /// <summary>InputFrame を記録ファイルの 1 行に整形します。</summary>
    internal static string FormatLine(in InputFrame f)
    {
        string keys = f.Keys.Length == 0 ? "-" : string.Join(',', f.Keys.Select(k => k.ToString()));
        var ci = CultureInfo.InvariantCulture;
        return $"{f.Frame} {keys} {f.MouseX.ToString(ci)},{f.MouseY.ToString(ci)} {f.WheelTotal.ToString(ci)} {f.Buttons}";
    }

    /// <summary>記録ファイルの 1 行を InputFrame に復元します。書式が壊れていれば false。</summary>
    internal static bool TryParseLine(string line, out InputFrame frame)
    {
        frame = default;
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5) return false; // frame keys pos wheel buttons の 5 トークン必須
        var ci = CultureInfo.InvariantCulture;

        if (!long.TryParse(parts[0], out long f)) return false;

        Key[] keys = [];
        if (parts[1] != "-")
        {
            var list = new List<Key>();
            foreach (string name in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (KeyInput.TryParse(name, out var k)) list.Add(k);
            keys = [.. list];
        }

        string[] pos = parts[2].Split(',');
        if (pos.Length < 2) return false;
        if (!double.TryParse(pos[0], NumberStyles.Float, ci, out double mx)) return false;
        if (!double.TryParse(pos[1], NumberStyles.Float, ci, out double my)) return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, ci, out double wheel)) return false;
        if (!int.TryParse(parts[4], out int buttons)) return false;

        frame = new InputFrame
        {
            Frame = f,
            Keys = keys,
            MouseX = mx,
            MouseY = my,
            WheelTotal = wheel,
            Buttons = buttons,
        };
        return true;
    }
}
