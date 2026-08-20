using System.Diagnostics;

using Raylib_cs;

using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

public sealed class RayLibPlatform : IGamePlatform
{
    public GraphicsBackendKind BackendKind => GraphicsBackendKind.RayLib;

    public IGraphics Graphics { get; }
    public IInput Input { get; }
    public ITime Time { get; }
    public ITime UTime { get; }
    public TextEnter TextInput { get; }
    public IMouse Mouse { get; }
    public IController Controller { get; }

    public bool ShouldClose { get; private set; }

    public RayLibPlatform(GameConfig config)
    {
        InitWindow(config.Width, config.Height, config.Title);

        if (!config.Resizable)
        {
            SetWindowState(ConfigFlags.UndecoratedWindow); // 例：必要なら調整
        }

        // AstrumLoom 側で FPS を管理するので、Raylib 側のターゲットFPSは 0 にしておく
        _targetFps = config.TargetFps;
        SetTargetFPS(_targetFps);
        SetExitKey(0); // ESC キーで終了しないようにする
        if (!IsAudioDeviceReady())
        {
            InitAudioDevice();
        }

        _multiThreadUpdate = config.UseMultiThreadUpdate;

        // TargetFps は Host.cs（GameHost コンストラクタ）が Time/UTime それぞれに
        // 正しい値（シングルスレッドなら UTime は 0＝無制限）を設定し直すので、ここでは
        // DxLibPlatform と同じく無設定（既定 0f）のまま渡す。以前はここで両方に _targetFps を
        // 入れていたため、シングルスレッド時に Update と Draw が毎ループそれぞれ待ち、
        // 実効FPSが半分になるバグがあった。
        Time = new SimpleTime();
        UTime = new SimpleTime();
        Graphics = new RayLibGraphics();
        Input = new RayLibInput();
        TextInput = new(new RayLibTextInput(), Time);
        Mouse = new RayLibMouse();
        Controller = new RayLibController();
    }

    public void PollEvents()
    {
        if (ShouldClose) return;

        if (WindowShouldClose())
        {
            ShouldClose = true;
            return;
        }
        // キー状態の更新
        Input.Buffer();
        Controller.Buffer();
    }

    public void Close() => ShouldClose = true;
    public bool IsActive => IsWindowFocused();
    public double? SystemFPS => GetFPS();

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // ウィンドウが初期化済みのときだけ閉じる
        if (_ready)
        {
            CloseWindow();
        }
    }

    private bool _ready => IsWindowReady();
    public ITexture LoadTexture(string path) =>
        new RayLibTexture(path);
    public ISound LoadSound(string path, bool streaming = false) =>
        new RayLibSound(path, streaming);
    public IMovie LoadMovie(string path) =>
        new RayLibMovie(path);

    public ITexture CreateTexture(int width, int height, Action callback)
        => new RayLibTexture(width, height, callback);

    private bool VSync;
    private readonly int _targetFps;
    // シングルスレッド構成では 1 ループの中で UTime.EndFrame → Time.EndFrame が連続で呼ばれるため、
    // UTime にも目標FPSを持たせると 1 ループで 2 回待って実効FPSが半分に落ちる。
    // ここ（VSync 切替時）でも Host.cs の初期設定と同じ判断基準を保つ。
    private readonly bool _multiThreadUpdate;
    public void SetVSync(bool enabled)
    {
        if (!_ready || VSync == enabled) return;
        Log.Debug("VSync切替: " + enabled);
        VSync = enabled;
        // 途中切替は SetWindowState / ClearWindowState を使う。
        if (enabled)
        {
            SetWindowState(ConfigFlags.VSyncHint); // スワップ間引き（プラットフォーム依存）
            int monitorFps = GetMonitorRefreshRate(GetCurrentMonitor());
            int targetFps = _targetFps == 0 ? monitorFps : Math.Min(_targetFps, monitorFps);
            SetTargetFPS(targetFps);
            Time.TargetFps = targetFps;
            if (_multiThreadUpdate) UTime.TargetFps = targetFps;
        }
        else
        {
            ClearWindowState(ConfigFlags.VSyncHint);
            SetTargetFPS(_targetFps);
            Time.TargetFps = _targetFps;
            if (_multiThreadUpdate) UTime.TargetFps = _targetFps;
        }
    }
    private bool dragDrop = false;
    public void SetDragDrop(bool enabled)
    {
        if (!_ready || dragDrop == enabled) return;
        Log.Debug("Drag&Drop切替: " + enabled);
        dragDrop = enabled;
    }
    public string[] DropFiles => dragDrop && IsFileDropped() ? GetDroppedFiles() : [];

    // ================================
    //  時間管理（DxLib版と同じノリ）
    // ================================

    private sealed class SimpleTime : ITime
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private long _lastTicks;

        public float DeltaTime { get; private set; }
        public float TotalTime => (float)_sw.Elapsed.TotalSeconds;
        public float CurrentFps { get; private set; }
        // DxLibPlatform 側の SimpleTime と揃えて既定 0f（無制限）にする。以前はここが 60f で
        // DxLib 側の 0f と食い違っており、TargetFps を明示的に設定し忘れた場合の挙動が
        // バックエンドごとに変わる原因の一つになっていた。
        public float TargetFps { get; set; } = 0f;

        public void BeginFrame()
        {
            long now = _sw.ElapsedTicks;
            if (_lastTicks == 0)
            {
                DeltaTime = 0f;
            }
            else
            {
                long dtTicks = now - _lastTicks;
                DeltaTime = (float)dtTicks / Stopwatch.Frequency;
                if (DeltaTime > 0f)
                {
                    CurrentFps = 1f / DeltaTime;
                }
            }
            _lastTicks = now;
        }

        public void EndFrame()
        {
            if (TargetFps <= 0) return;

            double ideal = 1.0 / TargetFps;
            long now = _sw.ElapsedTicks;
            long dtTicks = now - _lastTicks;
            double delta = (float)dtTicks / Stopwatch.Frequency;
            double remain = ideal - delta;
            if (remain > 0)
            {
                double ms = remain * 1000.0;
                if (ms > 0)
                    HiResDelay.Delay(TimeSpan.FromMilliseconds(ms));
            }
        }

        private static class HiResDelay
        {
            // 目安: sub-ms の仕上げに
            public static void Delay(TimeSpan duration)
            {
                var sw = Stopwatch.StartNew();
                // まずは大雑把に（1ms残すくらいまで）寝る
                var sleepUntil = duration - TimeSpan.FromMilliseconds(1);
                if (sleepUntil > TimeSpan.Zero)
                    Thread.Sleep(sleepUntil);

                // 仕上げはスピンで追い込む
                while (sw.Elapsed < duration)
                {
                    /* busy wait */
                    var span = TimeSpan.FromMicroseconds(1);
                    Thread.Sleep(span);
                }
                double actualMs = sw.Elapsed.TotalMilliseconds;
                //Log.Debug($"HiResDelay actual: {actualMs} ms");
            }
        }
    }
}
