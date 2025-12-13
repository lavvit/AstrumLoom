using System.Diagnostics;
using System.Text;

using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

public sealed class DxLibPlatform : IGamePlatform
{
    public GraphicsBackendKind BackendKind => GraphicsBackendKind.DxLib;

    public IGraphics Graphics { get; }
    public IInput Input { get; }
    public ITime UTime { get; }
    public ITime Time { get; }
    public TextEnter TextInput { get; }
    public IMouse Mouse { get; }
    public IController Controller { get; }

    public bool ShouldClose { get; private set; }

    public bool VSync { get; private set; }

    public DxLibPlatform(GameConfig config)
    {
        SetOutApplicationLogValidFlag(0); // ログファイル無効化
        ChangeWindowMode(TRUE); // ウィンドウモード
        SetWindowStyleMode(7); // 通常のウィンドウスタイル
        SetGraphMode(config.Width, config.Height, 32); // 解像度
        SetBackgroundColor(0, 0, 0); // デフォルト背景
        SetWindowText(config.Title); // ウィンドウタイトル
        SetWindowSizeExtendRate(config.Scale); // ウィンドウ拡大率
        SetAlwaysRunFlag(1); // 非アクティブでも動かす
        SetWaitVSyncFlag(0); // VSync 無効
        VSync = config.VSync;
        _targetFps = config.TargetFps;

        SetMultiThreadFlag(1); // マルチスレッド
        SetDoubleStartValidFlag(1); // 複数起動
        SetUseDirectInputFlag(0); // DirectInputコントローラー(重いため一時無効化)

        SetUseDirect3DVersion(DX_DIRECT3D_11);   // 11 を指定
                                                 // ソフトウェアレンダにしてないか確認
        SetUseSoftwareRenderModeFlag(0);

        // 必要な設定いろいろ…
        if (DxLib_Init() < 0)
            throw new Exception("DxLib_Init failed");

        Time = new SimpleTime();
        UTime = new SimpleTime();
        Graphics = new DxLibGraphics(); // DummyGraphics の代わり
        Input = new DxLibInput();
        TextInput = new(new DxLibTextInput(), Time);
        Mouse = new DxLibMouse();
        Controller = new DxLibController();
    }

    public void PollEvents()
    {
        if (ShouldClose) return;

        // ウィンドウの×が押されたら != 0 になるので終了
        if (ProcessMessage() != 0)
        {
            ShouldClose = true;
            return;
        }
        // キー状態の更新
        Input.Buffer();
        Controller.Buffer();
    }

    public void Close() => ShouldClose = true;
    public bool IsActive => GetWindowActiveFlag() > 0;
    public double? SystemFPS => GetFPS();

    public void Dispose() => DxLib_End();

    public ITexture LoadTexture(string path) =>
        new DxLibTexture(path);
    public ISound LoadSound(string path, bool streaming) =>
        new DxLibSound(path, streaming);
    public IMovie LoadMovie(string path) =>
        new DxLibMovie(path);

    public ITexture CreateTexture(int width, int height, Action callback)
    {
        if (Environment.CurrentManagedThreadId != AstrumCore.MainThreadId)
        {
            Log.Warning("CreateTexture はメインスレッドで呼び出してください。");
            return new DxLibTexture("");
        }
        if (width <= 0 || height <= 0) return new DxLibTexture("");
        int scr = MakeScreen(width, height, TRUE);
        if (scr < 0) return new DxLibTexture("");

        int oldScreen = GetDrawScreen();
        SetDrawScreen(scr);
        SetBackgroundColor(0, 0, 0);
        ClearDrawScreen();

        // execute the provided draw actions onto the temporary screen
        callback?.Invoke();

        SetDrawScreen(oldScreen);

        return new DxLibTexture(scr);
    }

    private readonly int _targetFps;
    public void SetVSync(bool enabled)
    {
        if (VSync == enabled)
        {
            WaitVSync(enabled ? 1 : 0);
            return;
        }
        Log.Debug("VSync切替: " + enabled);
        VSync = enabled;
        SetWaitVSyncFlag(enabled ? 1 : 0);
        if (enabled)
        {
            int display = 0;
            GetDisplayInfo(display, out _, out _, out _, out _, out _,
                out int monitorFps);
            int targetFps = _targetFps == 0 ? monitorFps : Math.Max(0, monitorFps);
            Time.TargetFps = targetFps;
            UTime.TargetFps = targetFps;
        }
        else
        {
            Time.TargetFps = _targetFps;
            UTime.TargetFps = _targetFps;
        }
    }
    private bool dragDrop = false;
    public void SetDragDrop(bool enabled)
    {
        if (dragDrop == enabled) return;
        Log.Debug("DragDrop切替: " + enabled);
        dragDrop = enabled;
        //SetDragFileValidFlag(enabled ? 1 : 0);
    }
    public string[] DropFiles
    {
        get
        {
            int count = GetDragFileNum();
            if (count <= 0 || !dragDrop) return [];
            string[] files = new string[count];
            for (int i = 0; i < count; i++)
            {
                var sb = new StringBuilder(512);
                GetDragFilePath(sb);
                files[i] = sb.ToString();
            }
            return files;
        }
    }

    // --- 以下 stub 実装たち ---

    private sealed class SimpleTime : ITime
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private long _lastTicks;

        public float DeltaTime { get; private set; }
        public float TotalTime => (float)_sw.Elapsed.TotalSeconds;
        public float CurrentFps { get; private set; }
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
                if (DeltaTime > 0)
                    CurrentFps = 1f / DeltaTime;
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
