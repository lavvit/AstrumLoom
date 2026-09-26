using AstrumLoom;
using AstrumLoom.RayLib;
using AstrumLoom.DXLib;
using System.Reflection;

static class NativeChecks
{
    public static void Run(string backend)
    {
        int passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
        AstrumCore.MainThreadId = Environment.CurrentManagedThreadId;
        var config = new GameConfig { Width = 320, Height = 240, VSync = false, Title = "AstrumLoom native regression" };
        typeof(AstrumCore).GetProperty(nameof(AstrumCore.WindowConfig))!.SetValue(null, config);
        using IGamePlatform platform = backend == "raylib" ? new RayLibPlatform(config) : new DxLibPlatform(config);
        AstrumCore.Platform = platform;
        var file = Path.Combine(Path.GetTempPath(), "AstrumLoom-silence-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            CheckTexturePixels(platform, Check);
            using (var output = new BinaryWriter(File.Create(file)))
            {
                const int bytes = 48000 * 2;
                output.Write("RIFF"u8); output.Write(36 + bytes); output.Write("WAVEfmt "u8);
                output.Write(16); output.Write((short)1); output.Write((short)1); output.Write(48000);
                output.Write(96000); output.Write((short)2); output.Write((short)16);
                output.Write("data"u8); output.Write(bytes); output.Write(new byte[bytes]);
            }
            using (var sound = new Sound(file, stream: true))
            {
                Check(sound.IsReady && sound.Length is >= 990 and <= 1010, "stream opens with correct duration");
                sound.Volume = 0; sound.Play();
                long start = Environment.TickCount64;
                while (Environment.TickCount64 - start < 350) { platform.PollEvents(); AstrumCore.ServiceMainFrame(); Thread.Sleep(5); }
                Check(sound.Playing && sound.Time > 50, "stream advances without Draw or explicit Sound.Pump");
                sound.Play();
                Check(sound.Time < 1, "repeated Play restarts at the beginning");
                sound.Time = 100; sound.Time = 105;
                Check(Math.Abs(sound.Time - 105) < 1, "five millisecond seek is not discarded");
                sound.Stop(); Check(!sound.Playing, "Stop stops the native stream");
            }
            AstrumCore.ProcessPendingDisposals();
            if (backend == "raylib")
            {
                using (var stream = new RayLibSound(file, true))
                    Check(stream.Music.FrameCount > 0 && stream.Sfx.FrameCount == 0, "stream owns Music only");
                RayLibSound? effect = null;
                Task.Run(() => effect = new RayLibSound(file, false)).GetAwaiter().GetResult();
                try
                {
                    long deadline = Environment.TickCount64 + 3000;
                    while (!effect!.IsReady && Environment.TickCount64 < deadline) { AstrumCore.ServiceMainFrame(); Thread.Sleep(1); }
                    Check(effect!.IsReady && effect.Sfx.FrameCount > 0 && effect.Music.FrameCount == 0, "background effect creates Sound only");
                    var handle = effect.Sfx.Stream.Buffer;
                    for (int i = 0; i < 10; i++) effect.Pump();
                    Check(effect.Sfx.Stream.Buffer == handle, "pumping preserves the native Sound handle");
                }
                finally { effect?.Dispose(); }
            }
            platform.Graphics.BeginFrame();
            try
            {
                using var outer = platform.CreateTexture(16, 16, () =>
                {
                    try { using var failed = platform.CreateTexture(4, 4, () => throw new InvalidOperationException("expected callback failure")); }
                    catch (InvalidOperationException) { }
                    using (var inner = platform.CreateTexture(4, 4, () => Drawing.Box(0, 0, 4, 4, Color.White))) { }
                    Drawing.Box(0, 0, 16, 16, Color.Red);
                    Drawing.Box(0, 0, 8, 8, Color.Blue, blend: BlendMode.Add);
                });
                Check(outer.IsReady, "nested render callback failure preserves outer target");
                if (outer is RayLibTexture rt)
                {
                    var pixels = Raylib_cs.Raylib.LoadImageFromTexture(rt.Native);
                    try { var color = Raylib_cs.Raylib.GetImageColor(pixels, 4, 12); Console.WriteLine($"PIXEL {color.R},{color.G},{color.B},{color.A}; top {Raylib_cs.Raylib.GetImageColor(pixels, 4, 4)}"); Check(color.R == 255 && color.G == 0 && color.B == 255, "outer pixels preserve nested target and additive primitive blend"); }
                    finally { Raylib_cs.Raylib.UnloadImage(pixels); }
                }
                else if (outer is DxLibTexture dt)
                {
                    int previous = DxLibDLL.DX.GetDrawScreen();
                    try { DxLibDLL.DX.SetDrawScreen(dt.Handle); Check(DxLibDLL.DX.GetPixel(0, 0) == DxLibDLL.DX.GetColor(255, 0, 255), "outer pixels preserve nested target and additive primitive blend"); }
                    finally { DxLibDLL.DX.SetDrawScreen(previous); }
                }
                outer.Draw(0, 0);
            }
            finally { platform.Graphics.EndFrame(); }
            AstrumCore.ProcessPendingDisposals();
            Console.WriteLine($"NATIVE {backend} PASS {passed} / FAIL 0");
        }
        finally { Sound.DisposeAll(); AsyncLoadableBase.CancelPending(); AstrumCore.ProcessPendingDisposals(); File.Delete(file); }
    }

    private static void CheckTexturePixels(IGamePlatform platform, Action<bool, string> check)
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAMAAAACCAYAAACddGYaAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAYSURBVBhXY/jPwPCf4T9DAwME/IcCBkYAf6QLd1+A/YYAAAAASUVORK5CYII=");
        var path = Path.Combine(Path.GetTempPath(), "AstrumLoom-pixels-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, png);
        try
        {
            using var texture = new Texture(path);
            check(texture.IsReady, "pixel fixture loads");
            check(texture.GetPixel(0, 0) == Color.Red && texture.GetPixel(0, 1) == Color.Blue, "file RGB and top-left orientation");
            var half = texture.GetPixel(1, 0);
            Console.WriteLine($"HALF RGBA {half.R},{half.G},{half.B},{half.A}");
            check(half.G == 255 && half.R == 0 && half.B == 0 && half.A == 128, "file retains half alpha and unmultiplied RGB");
            check(texture.GetPixel(2, 0).A == 0 && texture.GetPixel(2, 1).A == 1, "file retains transparent and low-alpha pixels");
            check(!texture.HitTest(2.5, .5) && texture.HitTest(2.5, 1.5), "native alpha hit testing distinguishes transparent pixels");
            check(Task.Run(() => texture.GetPixel(1, 0)).GetAwaiter().GetResult() == half, "cached pixel can be read on update thread");

            using var delayed = new Texture(path);
            check(!Task.Run(() => delayed.TryGetPixel(0, 0, out _)).GetAwaiter().GetResult(), "first update-thread read is deferred");
            var pump = typeof(GameRunner).GetMethod("PumpMainActions", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var runner = new GameRunner(platform, new PixelGame(), new GameConfig());
            pump.Invoke(runner, null);
            check(Task.Run(() => delayed.GetPixel(0, 0)).GetAwaiter().GetResult() == Color.Red, "queued pixel read completes on main thread");

            if (platform.BackendKind == GraphicsBackendKind.RayLib)
            {
                using var memory = new Texture(png);
                using var raw = new Texture(2, 1, new byte[] { 12, 34, 56, 128, 0, 0, 0, 0 });
                check(memory.GetPixel(0, 1) == Color.Blue && raw.GetPixel(0, 0) == new Color(12, 34, 56, 128), "encoded and raw memory textures expose pixels");
            }
            platform.Graphics.BeginFrame();
            try
            {
                using var baked = new Texture(new LayoutUtil.Size(8, 8), () => Drawing.Box(0, 0, 4, 4, Color.Red));
                check(baked.GetPixel(1, 1) == Color.Red && baked.GetPixel(6, 6).A == 0, "baked pixel orientation and transparent background");
                // ReadPixels must leave the current render target intact, including nested callbacks.
                using var nested = new Texture(new LayoutUtil.Size(8, 8), () =>
                {
                    using var readDuringDraw = new Texture(path);
                    check(readDuringDraw.GetPixel(0, 0) == Color.Red, "pixel read works inside render callback");
                    Drawing.Box(0, 0, 8, 8, Color.Blue);
                });
                check(nested.GetPixel(4, 4) == Color.Blue, "pixel read restores render target");
                foreach (var opt in new[]
                {
                    new DrawOption { Scale = (4, 6), Point = ReferencePoint.Center, Angle = .25 },
                    new DrawOption { Scale = (4, 6), Point = ReferencePoint.TopLeft, Flip = (true, true) },
                    new DrawOption { Scale = (4, 6), Position = (-1, -2), Rectangle = new(1, 0, 2, 2), Flip = (true, false) },
                })
                {
                    using var drawn = new Texture(new LayoutUtil.Size(48, 48), () => texture.Draw(20, 20, opt));
                    bool matches = true;
                    // 整数倍率のピクセル中央を採取し、補間境界を避けて実際の描画と照合する。
                    for (int y = 1; y < 47; y += 2)
                        for (int x = 1; x < 47; x += 2)
                        {
                            bool hit = texture.HitTest(x + .5, y + .5, 20, 20, 200, opt);
                            bool visible = drawn.GetPixel(x, y).A > 200;
                            if (hit != visible) { matches = false; Console.WriteLine($"HIT MISMATCH {x},{y} hit={hit} A={drawn.GetPixel(x, y).A}"); }
                        }
                    check(matches, "transformed hit test agrees with rendered alpha");
                }
            }
            finally { platform.Graphics.EndFrame(); }
        }
        finally { File.Delete(path); AstrumCore.ProcessPendingDisposals(); }
    }

    private sealed class PixelGame : IGame
    {
        public void Initialize() { }
        public void Update(float deltaTime) { }
        public void Draw() { }
        public void Dispose() { }
    }
}
