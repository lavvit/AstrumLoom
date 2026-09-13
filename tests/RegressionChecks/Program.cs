using AstrumLoom;
using AstrumLoom.Extend;
using System.Reflection;

try
{
if (args.Length == 2 && args[0] == "--native") { NativeChecks.Run(args[1]); return; }

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
AstrumCore.MainThreadId = Environment.CurrentManagedThreadId;
typeof(AstrumCore).GetProperty(nameof(AstrumCore.WindowConfig))!.SetValue(null, new GameConfig());
var platform = DispatchProxy.Create<IGamePlatform, PlatformProxy>();
AstrumCore.Platform = platform;
var proxy = (PlatformProxy)(object)platform;
KeyInput.Initialize(new AlwaysDown(), new TextEnter(null!, null!));
var edit = new TextEditBuffer { Filter = char.IsAsciiDigit };
edit.Reset("123"); edit.SelectAll(); edit.Insert("x");
Check(edit.Text == "123" && edit.HasSelection, "rejected replacement preserves selection and text");
edit.MaxLength = 3; edit.Insert("4567");
Check(edit.Text == "456", "replacement can use selected capacity");
edit = new TextEditBuffer { MaxLength = 1 }; edit.Reset("\U0001F600");
Check(edit.Text == "", "Reset never cuts a surrogate pair");
edit.Reset("a"); edit.SelectAll(); edit.Insert("\U0001F600");
Check(edit.Text == "a" && edit.HasSelection, "unfittable surrogate preserves selection");

var game = new ProbeGame();
var cfg = new GameConfig { FixedUpdate = true, FixedUpdateHz = 60, MaxCatchUpSteps = 5 };
var runner = new GameRunner(platform, game, cfg);
var run = typeof(GameRunner).GetMethod("RunLogicSteps", BindingFlags.NonPublic | BindingFlags.Instance)!;
var acc = typeof(GameRunner).GetField("_accumulator", BindingFlags.NonPublic | BindingFlags.Instance)!;
acc.SetValue(runner, 0.085f); run.Invoke(runner, [game, 0.085f]);
Check(game.Updates == 5 && game.Pushes == 1, "one edge across five catch-up steps");
Check(Math.Abs(KeyInput.PressedFrameCount(Key.A) - 1000.0 / 12) < 0.01, "hold time advances by logical steps");
game.Throw = true; acc.SetValue(runner, 0.085f);
try { run.Invoke(runner, [game, 0.085f]); } catch (TargetInvocationException) { }
Check(!InputStep.EdgesSuppressed, "catch-up exception restores edge visibility");

var mouse = new TestMouse(); mouse.Init(false);
mouse.Feed(10, 20, .25, 1); mouse.Feed(50, 60, .5, 0);
mouse.Update();
Check(mouse.Push(MouseButton.Left) && mouse.Hold(MouseButton.Left), "short mouse tap survives between updates");
Check(mouse.Wheel == .75 && mouse.WheelTotal == .75 && mouse.X == 50, "mouse sample coalesces wheel and position");
InputStep.EdgesSuppressed = true;
Check(!mouse.Push(MouseButton.Left) && mouse.Wheel == 0, "mouse edge and wheel are not replayed during catch-up");
InputStep.EdgesSuppressed = false; mouse.Update();
Check(mouse.Left(MouseButton.Left) && mouse.Wheel == 0, "mouse release delivered and wheel consumed once");
mouse.Feed(0, 0, 0, 1); mouse.Update(); mouse.Feed(100, 100, 0, 1); mouse.Update();
Check(mouse.Hold(MouseButton.Left) && !mouse.Left(MouseButton.Left), "drag remains held beyond three pixels");

using (var resource = new LoadProbe())
{
    Task.Run(resource.Start).GetAwaiter().GetResult();
    Check(resource.Entered.Wait(3000), "background preparation begins");
    Check(!resource.Ready && resource.NativeLoads == 0, "readiness getter has no native side effect");
    resource.Dispose(); resource.Release.Set();
    Check(resource.Prepared.Wait(3000), "cancelled preparation finishes");
    resource.Pump(); AsyncLoadableBase.PumpPending();
    Check(resource.StateValue == -2 && resource.NativeLoads == 0 && resource.Disposals == 1, "disposed resource never resurrects after preparation");
}
using (var resource = new LoadProbe())
{
    Task.Run(resource.Start).GetAwaiter().GetResult(); resource.Release.Set();
    var deadline = Environment.TickCount64 + 3000;
    while (!resource.Ready && Environment.TickCount64 < deadline) { AsyncLoadableBase.PumpPending(); Thread.Sleep(1); }
    Check(resource.Ready && resource.NativeLoads == 1, "main loop completes unread resource exactly once");
    for (int i = 0; i < 10; i++) resource.Pump();
    Check(resource.NativeLoads == 1, "extra pumping does not allocate duplicate handles");
}
using (var resource = new LoadProbe())
{
    Task.Run(resource.Start).GetAwaiter().GetResult(); AsyncLoadableBase.CancelPending(); resource.Release.Set();
    Check(resource.StateValue == -2 && resource.Disposals == 1, "shutdown invokes resource-specific disposer");
}

var scratch = Path.Combine(Path.GetTempPath(), "AstrumLoom-regression-" + Guid.NewGuid().ToString("N") + ".txt");
try
{
    var recorder = new InputRecorder(scratch, new GameConfig { Seed = 123 });
    var legacy = DispatchProxy.Create<IMouse, MouseProxy>();
    var bridge = new MouseBridge(legacy, recorder, null); bridge.Update(); recorder.Commit(1); recorder.Save(2);
    var player = InputPlayer.Load(scratch)!; player.Seek(1);
    Check(player.Current.Buttons == 1, "mouse recorder includes first pressed frame");
    File.WriteAllText(scratch, "v 1\nhz 120\nseed 987\nsize 800x600\n5 A 0,0 0 0\nend 9\n");
    player = InputPlayer.Load(scratch)!; player.Seek(1);
    Check(player.Current.Keys.Length == 0, "replay does not apply first change early");
    player.Seek(5); Check(player.Current.Keys.SequenceEqual(new[] { Key.A }), "replay applies change on recorded frame");
    var replayConfig = new GameConfig(); replayConfig.Apply(new LaunchOptions { ReplayPath = scratch });
    Check(replayConfig.FixedUpdateHz == 120 && replayConfig.Seed == 987 && replayConfig.Width == 800, "replay restores rate seed and size");
    bool rejected = false;
    try { replayConfig.Apply(new LaunchOptions { ReplayPath = scratch, FixedUpdateHz = 60 }); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "explicit replay rate conflict rejected");
    foreach (var text in new[] { "v 99\n1 - 0,0 0 0", "hz abc\n1 - 0,0 0 0", "1 - NaN,0 0 0", "2 - 0,0 0 0\n1 - 0,0 0 0" })
    { File.WriteAllText(scratch, text); Check(InputPlayer.Load(scratch) == null, "malformed replay rejected"); }
    Check(!InputCapture.TryParseLine("1 NotAKey 0,0 0 0", out _) && !InputCapture.TryParseLine("1 99999 0,0 0 0", out _), "invalid replay key identifiers rejected");
    var recordConfig = new GameConfig(); recordConfig.Apply(new LaunchOptions { RecordPath = scratch });
    Check(recordConfig.Seed.HasValue, "recording always specifies an actual random seed");
}
finally { File.Delete(scratch); }

proxy.LastTexture = null;
using (var texture = new Texture("fake"))
{
    var native = proxy.LastTexture!;
    texture.XYScale = (2, 3); texture.DrawSize(0, 0, new(50, 100));
    Check(native.Last.Scale == (5.0, 5.0) && texture.XYScale == (2.0, 3.0), "DrawSize preserves options");
    native.Throw = true;
    try { texture.DrawRect(0, 0, new(1, 2, 3, 4)); } catch (InvalidOperationException) { }
    Check(texture.Rectangle == null, "draw exception cannot change texture rectangle");
    Check(texture.ScaledSize.Height == (int)(60 * Drawing.DefaultScale), "ScaledSize uses independent vertical scale");
    texture.Dispose(); texture.Dispose(); AstrumCore.ProcessPendingDisposals();
    Check(!texture.Enable && native.Disposals == 1, "texture disposal is idempotent and invalidates wrapper");
}
var font = new TestFont();
using (var sprite = new TextSprite("one", font))
{
    sprite.Draw(0, 0); var first = proxy.LastTexture!;
    sprite.Text = "two"; sprite.Draw(0, 0); AstrumCore.ProcessPendingDisposals();
    Check(first.Disposals == 1 && !ReferenceEquals(first, proxy.LastTexture), "same-size text replacement releases old texture");
    first = proxy.LastTexture!; sprite.Color = Color.Red; sprite.Draw(0, 0); AstrumCore.ProcessPendingDisposals();
    Check(first.Disposals == 1, "color change rebuilds and releases text texture");
}
using (var resource = new LoadProbe { TimeoutMs = 1 })
{
    Task.Run(resource.Start).GetAwaiter().GetResult(); Thread.Sleep(40); resource.Pump(); resource.Release.Set();
    Check(resource.StateValue == -2 && resource.Disposals == 1, "timeout invokes the resource-specific disposer");
}
bool invalidPixels = false;
try { _ = new AstrumLoom.RayLib.RayLibTexture(2, 2, new byte[3]); } catch (ArgumentException) { invalidPixels = true; }
Check(invalidPixels, "short RGBA buffer is rejected before native upload");
var skinTexture = new Texture("skin"); var skinNative = proxy.LastTexture!;
Skin.Textures["test"] = skinTexture; Skin.Unload(); AstrumCore.ProcessPendingDisposals();
Check(Skin.Textures.Count == 0 && skinNative.Disposals == 1, "skin unload releases registered textures");
SelfTest.Clear(); SelfTest.Enabled = true;
SelfTest.CheckWhen(() => false, "unavailable", () => throw new Exception("must not run")); SelfTest.Advance();
Check(SelfTest.Skipped == 1 && SelfTest.Passed == 0 && SelfTest.Failed == 0, "unavailable tests are SKIP and never PASS");
SelfTest.Enabled = false; SelfTest.Clear();
Console.WriteLine($"PASS {passed} / FAIL 0");

}
catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }

sealed class AlwaysDown : IInput
{
    public void Buffer() { } public void Update() { }
    public bool GetKey(Key k) => k == Key.A; public bool GetKeyDown(Key k) => k == Key.A; public bool GetKeyUp(Key k) => false;
}
sealed class ProbeGame : IGame
{
    public int Updates, Pushes; public bool Throw;
    public void Initialize() { } public void Draw() { }
    public void Update(float dt) { Updates++; if (Key.A.Push()) Pushes++; if (Throw && InputStep.EdgesSuppressed) throw new InvalidOperationException(); }
}
sealed class LoadProbe : AsyncLoadableBase, IResourse
{
    public readonly ManualResetEventSlim Entered = new(), Release = new(), Prepared = new();
    public int NativeLoads, Disposals;
    public bool Ready => LoadReady; public int StateValue => (int)State;
    public bool IsReady => Ready; public bool IsFailed => LoadFailed; public bool Loaded => LoadFinished; public bool Enable => Ready; public string Path => "probe";
    public void Start() => LoadAsync(this, () => { NativeLoads++; return true; }, () => { Entered.Set(); Release.Wait(); Prepared.Set(); return true; });
    public void Pump() => PumpAsync();
    public void Dispose() => DisposeAsync(() => { Disposals++; return true; });
}
sealed class TestMouse : BufferedMouse
{
    protected override void SetNativePosition(int x, int y) { }
    protected override void SetNativeVisible(bool visible) { }
    public override void Buffer() { }
    public void Feed(double x, double y, double wheel, int buttons) => Sample(x, y, wheel, buttons);
}
public class MouseProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? m, object?[]? a) => m!.Name switch
    { "Push" => (MouseButton)a![0]! == MouseButton.Left, "Hold" or "Left" => false, "get_X" or "get_Y" or "get_Wheel" or "get_WheelTotal" => 0.0, _ => null };
}
public class PlatformProxy : DispatchProxy
{
    public TestTexture? LastTexture;
    protected override object? Invoke(MethodInfo? m, object?[]? a) => m!.Name switch
    { "get_IsActive" => true, "LoadTexture" or "CreateTexture" => LastTexture = new TestTexture(), _ => null };
}
public sealed class TestTexture : ITexture
{
    public int Disposals; public bool Throw; public DrawOptions Last;
    public string Path => "fake"; public int Width => 10; public int Height => 20;
    public bool Enable => Disposals == 0; public bool IsReady => Enable; public bool IsFailed => false; public bool Loaded => true;
    public void Draw(double x, double y, DrawOptions option) { Last = option; if (Throw) throw new InvalidOperationException(); }
    public void Pump() { } public void Dispose() => Disposals++;
}
sealed class TestFont : IFont
{
    public bool Enable => true; public FontSpec Spec => new("fake", 12);
    public (int, int) Measure(string text) => (30, 12);
    public void Draw(double x, double y, string text, DrawOptions options) { }
    public void DrawEdge(double x, double y, string text, DrawOptions options) { }
    public void DrawGrad(double x, double y, string text, Gradation gradation, DrawOptions options) { }
    public void DrawTexture(double x, double y, string text, ITexture[] texture, DrawOptions options) { }
    public void Dispose() { }
}
