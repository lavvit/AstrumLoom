using System.Diagnostics;

using Raylib_cs;

using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

/// <summary>
/// IGamePlatform の raylib 実装。ウィンドウ/オーディオデバイスの初期化、各サブシステム（Graphics/Input/Mouse/Controller等）の
/// 生成、フレーム開始時のイベントポーリング、VSync・ドラッグ＆ドロップの切替、リソースのロードを一手に引き受ける。
/// </summary>
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

    private volatile bool _shouldClose;
    public bool ShouldClose { get => _shouldClose; private set => _shouldClose = value; }
    public unsafe nint WindowHandle => OperatingSystem.IsWindows() ? (nint)GetWindowHandle() : 0;

    /// <summary>raylibウィンドウ/オーディオデバイスを初期化し、各サブシステムを構築します。</summary>
    public RayLibPlatform(GameConfig config)
    {
        // ウィンドウのリサイズ可否は ConfigFlags.ResizableWindow に対応させる。
        // 以前は !Resizable のときに UndecoratedWindow（装飾なし＝タイトルバー等が消える）を
        // 誤って立てており、Resizable=false が「装飾が消える」という意図しない挙動になっていた。
        // 起動直後は白い空ウィンドウが数百ms映るだけなので、隠したまま作り、
        // 最初のフレームを描き終えた時点（RayLibGraphics.EndFrame）で表示する。
        var flags = ConfigFlags.HiddenWindow;
        if (config.Resizable)
        {
            flags |= ConfigFlags.ResizableWindow;
        }
        SetConfigFlags(flags);
        InitWindow(config.Width, config.Height, config.Title);

        // AstrumLoom 側で FPS を管理するので、Raylib 側のターゲットFPSは 0 にしておく
        _targetFps = config.TargetFps;
        SetTargetFPS(0);
        SetExitKey(0); // ESC キーで終了しないようにする
        if (!IsAudioDeviceReady())
        {
            InitAudioDevice();
        }

        // Hostが描画と更新それぞれの上限を設定する。
        Time = new SimpleTime();
        UTime = new SimpleTime();
        Graphics = new RayLibGraphics();
        var rayInput = new RayLibInput();
        Input = rayInput;
        // Windows では IMM32 実装を使う（raylib 自身は日本語入力＝未確定文字列を扱えないため）。
        // フォールバックの RayLibTextInput はネイティブAPIを直接叩かず、rayInput が Buffer() で
        // 確定させたバッファ済み状態（キー/文字キュー）経由で読むため、同一インスタンスを共有する。
        TextInput = TextInputFactory.Create(WindowHandle, new RayLibTextInput(rayInput), Time, config.UseSystemIme);
        Mouse = new RayLibMouse();
        Controller = new RayLibController();
    }

    /// <summary>毎フレーム冒頭で呼び出し、ウィンドウの閉じる要求を確認したうえでキー/パッドの生入力バッファを更新します。</summary>
    public void PollEvents()
    {
        if (ShouldClose) return;

        PollInputEvents();

        if (WindowShouldClose())
        {
            ShouldClose = true;
            return;
        }
        // キー状態の更新
        Input.Buffer();
        Mouse.Buffer();
        Controller.Buffer();
    }

    public void Close() => ShouldClose = true;
    public bool IsActive => IsWindowFocused();
    public double? SystemFPS => Time.CurrentFps;

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (IsAudioDeviceReady()) CloseAudioDevice();
        // ウィンドウが初期化済みのときだけ閉じる
        if (_ready)
        {
            CloseWindow();
        }
    }

    private bool _ready => IsWindowReady();
    public ITexture LoadTexture(string path) =>
        new RayLibTexture(path);
    /// <summary>メモリ上のエンコード済み画像バイト列（SkiaSharp等の焼き出し結果）からテクスチャを作る。</summary>
    public ITexture LoadTextureFromMemory(byte[] data, string ext) =>
        new RayLibTexture(data, ext);
    /// <summary>生のRGBA32ピクセル列から直接テクスチャを作る（PNG等のエンコード/デコード無し）。</summary>
    public ITexture LoadTextureFromPixels(int width, int height, byte[] rgba) =>
        new RayLibTexture(width, height, rgba);
    public ISound LoadSound(string path, bool streaming = false) =>
        new RayLibSound(path, streaming);
    public IMovie LoadMovie(string path) =>
        new RayLibMovie(path);

    /// <summary>Action(drawAction) を焼き込むレンダーターゲットとしてテクスチャを生成する。</summary>
    public ITexture CreateTexture(int width, int height, Action callback)
        => new RayLibTexture(width, height, callback);

    private bool VSync;
    private readonly int _targetFps;
    /// <summary>
    /// 描画のソフトウェア上限を切り替える。更新レートは変更しない。
    /// </summary>
    public void SetVSync(bool enabled)
    {
        if (!_ready || VSync == enabled) return;
        Log.Debug("VSync切替: " + enabled);
        VSync = enabled;
        if (enabled) SetWindowState(ConfigFlags.VSyncHint);
        else ClearWindowState(ConfigFlags.VSyncHint);
        SetTargetFPS(0);
        Time.TargetFps = _targetFps;
    }
    private bool dragDrop = false;
    /// <summary>ドラッグ＆ドロップの受付フラグを切り替える。実際のraylib側APIは常時有効なので、DropFilesの読み出しを許可/禁止するだけの内部フラグ。</summary>
    public void SetDragDrop(bool enabled)
    {
        if (!_ready || dragDrop == enabled) return;
        Log.Debug("Drag&Drop切替: " + enabled);
        dragDrop = enabled;
    }
    /// <summary>ドラッグ＆ドロップが有効かつ今フレームでファイルがドロップされていれば、そのパス一覧を返す。</summary>
    public string[] DropFiles => dragDrop && IsFileDropped() ? GetDroppedFiles() : [];

    // ================================
    //  時間管理（DxLib版と同じノリ）
    // ================================

    /// <summary>Stopwatchベースの ITime 実装。TargetFpsが正のときだけ EndFrame でフレーム末尾の余り時間をディレイして揃える。</summary>
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

        /// <summary>フレーム冒頭で経過時間を測り、DeltaTime/CurrentFpsを更新する。初回呼び出しはDeltaTime=0とする。</summary>
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

        /// <summary>目標FPSに対して余った時間だけ HiResDelay で待って、フレームレートを目標値に揃える。TargetFps<=0なら無制限で何もしない。</summary>
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

        /// <summary>Thread.Sleepだけでは精度が粗いため、大まかにSleepしたあとスピン待ちで仕上げる高精度ディレイ。</summary>
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
