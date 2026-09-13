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
}
