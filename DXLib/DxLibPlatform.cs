using System.Diagnostics;
using System.Text;

using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

/// <summary>
/// DxLibバックエンドのIGamePlatform実装。DxLib_Initによるネイティブ初期化と、
/// Graphics/Input/Time/Mouse/Controller/TextInputなど各サブシステムのDxLib実装を束ねて提供する。
/// </summary>
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

    /// <summary>DxLibのウィンドウ・レンダリング設定を行ってDxLib_Initし、各サブシステム実装を生成する。失敗時は例外を投げる。</summary>
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
        // VSync フィールドはここでは触らず、ネイティブ側の実状態（SetWaitVSyncFlag(0)=無効）と
        // 同じ既定値 false のままにしておく。以前は config.VSync を直接代入していたが、
        // config.VSync=true のとき SetVSync(true) が「VSync==enabled で値が同じ」の早期 return
        // 枝に落ちてしまい、SetWaitVSyncFlag(1) にもモニタ Hz の反映（下の SetVSync 内）にも
        // 一切到達しなくなる。config.VSync の反映はコンストラクタ末尾の SetVSync(config.VSync)
        // に任せる。
        _targetFps = config.TargetFps;
        _multiThreadUpdate = config.UseMultiThreadUpdate;

        SetDragFileValidFlag(1);
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

        // ここで初めて config.VSync をネイティブ側へ反映する。VSync フィールドは false の
        // ままなので、config.VSync=false ならそのまま早期 return（WaitVSync(0) の空振りのみ）、
        // config.VSync=true なら SetWaitVSyncFlag(1) とモニタ Hz に基づく TargetFps 設定まで
        // ちゃんと通る。
        SetVSync(config.VSync);
    }

    /// <summary>DxLibのウィンドウメッセージを処理し、×ボタンでの終了要求を検知する。あわせてキー・パッドの生状態を毎フレームBufferする。</summary>
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

    /// <summary>callbackの描画内容を焼き込んだレンダーターゲット用テクスチャを作る。DxLibのMakeScreen/SetDrawScreenで一時的な描画先へ切り替えてから元に戻す。メインスレッド専用。</summary>
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
    // シングルスレッド構成では 1 ループの中で UTime.EndFrame → Time.EndFrame が連続で呼ばれるため、
    // UTime にも目標FPSを持たせると 1 ループで 2 回待って実効FPSが半分に落ちる。
    // ここ（VSync 切替時）でも Host.cs の初期設定と同じ判断基準を保つ。
    private readonly bool _multiThreadUpdate;
    /// <summary>
    /// VSyncのON/OFFをDxLibへ反映する。ONにする際はモニタのリフレッシュレートを取得し、
    /// config.TargetFpsとの小さい方をTargetFpsとして採用する（TargetFpsが0＝無制限ならモニタFPSに合わせる）。
    /// </summary>
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
            // 両分岐とも monitorFps になっていた書き間違い（Math.Max(0, monitorFps) は
            // monitorFps を 0 でクランプするだけで _targetFps を全く見ていない）。
            // RayLibPlatform.SetVSync と同じ Math.Min に合わせ、config.TargetFps がモニタの
            // リフレッシュレートより低いときはその値を優先する。
            int targetFps = _targetFps == 0 ? monitorFps : Math.Min(_targetFps, monitorFps);
            Time.TargetFps = targetFps;
            if (_multiThreadUpdate) UTime.TargetFps = targetFps;
        }
        else
        {
            Time.TargetFps = _targetFps;
            if (_multiThreadUpdate) UTime.TargetFps = _targetFps;
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

    /// <summary>Stopwatchベースの簡易ITime実装。DeltaTime計測とTargetFpsに基づくフレーム待機(EndFrame)を行う。</summary>
    private sealed class SimpleTime : ITime
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private long _lastTicks;

        public float DeltaTime { get; private set; }
        public float TotalTime => (float)_sw.Elapsed.TotalSeconds;
        public float CurrentFps { get; private set; }
        public float TargetFps { get; set; } = 0f;

        /// <summary>前回のBeginFrameからの経過時間をDeltaTimeとして記録する。初回呼び出し時はDeltaTime=0。</summary>
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

        /// <summary>TargetFpsが設定されていれば、理想フレーム時間に足りない分だけHiResDelayで待機してフレームレートを一定に保つ。</summary>
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

                // 仕上げはスピンで追い込む（Thread.Sleepはミリ秒未満の指定でもOS既定の
                // スケジューラ量子(数ms)分眠ってしまい、sub-ms精度を狙う意味が無くなるため、
                // ここは本当にSleepを挟まないビジーウェイトにする）
                while (sw.Elapsed < duration)
                {
                    Thread.SpinWait(50);
                }
                double actualMs = sw.Elapsed.TotalMilliseconds;
                //Log.Debug($"HiResDelay actual: {actualMs} ms");
            }
        }
    }
}
