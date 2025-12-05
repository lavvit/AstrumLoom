using Raylib_cs;

using static AstrumLoom.LayoutUtil;
using static AstrumLoom.RayLib.RayLibGraphics;
using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

internal sealed class RayLibTexture : ITexture
{
    public string Path { get; private set; } = "";
    public Texture2D Native { get; private set; }
    public int Width { get; private set; } = 0;
    public int Height { get; private set; } = 0;

    // RenderTexture の所有を持つ場合に保持する
    private RenderTexture2D _renderTex;
    // CreateTexture 経由で作られた RenderTexture2D を包むコンストラクタ
    public RayLibTexture(RenderTexture2D renderTexture)
    {
        _renderTex = renderTexture;
        Native = renderTexture.Texture;
        Width = Native.Width;
        Height = Native.Height;
        Volatile.Write(ref _asyncState, 1); // Ready
    }
    public RayLibTexture(string path)
    {
        Path = path;
        Load();
    }
    private bool _disposed;
    ~RayLibTexture() { Dispose(); }

    public void Dispose()
    {
        if (_disposed) return;
        if (!Raylib.IsWindowReady())
        {
            Log.Debug($"Texture dispose skipped: window not ready : {Path}");
            // ウィンドウ未準備でもマネージ側は終了扱いにしてファイナライザ再入を避ける
            _disposed = true;
            Native = default;
            _renderTex = default;
            GC.SuppressFinalize(this);
            return;
        }

        // ネイティブ解放はメインスレッドかつウィンドウが有効な時のみ
        if (_renderTex.Id != 0)
        {
            if (IsMainThread)
            {
                try
                {
                    // RenderTexture の解放は内部の Texture も同時に解放される
                    Raylib.UnloadRenderTexture(_renderTex);
                    _disposed = true;
                    Native = default;
                    _renderTex = default;
                    GC.SuppressFinalize(this);
                }
                catch { Log.Error($"Failed to unload render texture: {Path}"); }
            }
            else
            {
                AstrumLoom.AstrumCore.RequestDispose(this);
            }
            return;
        }

        if (Native.Id != 0)
        {
            if (IsMainThread)
            {
                try
                {
                    Raylib.UnloadTexture(Native);
                    _disposed = true;
                    Native = default;
                    GC.SuppressFinalize(this);
                }
                catch { Log.Error($"Failed to unload texture: {Path}"); }
            }
            else
            {
                //Log.Debug($"Texture dispose skipped: not main thread : {Path}");
                AstrumLoom.AstrumCore.RequestDispose(this);
            }
        }
        else
        {
            _disposed = true;
            Native = default;
            GC.SuppressFinalize(this);
        }
    }
    #region 読み込み
    public void Load()
    {
        if (!File.Exists(Path))
        {
            Log.Debug($"Texture: not found: {Path}");
            Volatile.Write(ref _asyncState, -1);
            return;
        }

        if (!IsMainThread)
        {
            Task.Run(() =>
            {
                try
                {
                    _startTicks = Environment.TickCount64;
                    _pendingBytes = File.ReadAllBytes(Path);
                    _pendingExt = System.IO.Path.GetExtension(Path).ToLowerInvariant();
                }
                catch
                {
                    _pendingBytes = null;
                    _asyncState = -1;   // Failed
                }
            });
            _deferred = true;
            _asyncState = 0;   // Loading扱い
            return;
        }
        else
        {
            // PNG/JPG/BMP等そのままOK
            Native = Raylib.LoadTexture(Path);
            _startTicks = Environment.TickCount64;

            // 初期状態をセット
            if (Native.Id == 0)
            {
                _asyncState = -1;
                return;
            }

            // サイズ取得
            int w = Native.Width, h = Native.Height;
            Width = w;
            Height = h;

            Volatile.Write(ref _asyncState, 1); // Ready
        }
    }

    // 0=Loading, 1=Ready, -1=Failed
    private int _asyncState = -1;
    public bool IsReady => Volatile.Read(ref _asyncState) == 1;
    public bool IsFailed => Volatile.Read(ref _asyncState) == -1;
    public bool Loaded
    {
        get
        {
            Pump(); // 毎フレーム呼ぶのを忘れた場合に備えてここでも呼ぶ
            return Volatile.Read(ref _asyncState) != 0;
        }
    }
    public bool Enable => Loaded && Native.Id > 0 && !_disposed;
    private static bool IsMainThread => Environment.CurrentManagedThreadId == AstrumCore.MainThreadId;
    private bool _deferred;
    private long _startTicks;
    private const int DefaultTimeoutMs = 15000;
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    public void Pump()
    {
        // メインスレッドでのみ触る
        if (!IsMainThread) return;

        if (Native.Id > 0 && Width + Height == 0)
        {
            int w = Native.Width, h = Native.Height;
            Width = w;
            Height = h;
        }

        // 保留中ならメインスレッドでロード開始
        if (_deferred)
        {
            _deferred = false;
            Load();
            return;
        }
        if (Volatile.Read(ref _asyncState) != 0) return; // Loading 以外は何もしない

        // 非同期ロードの完了待ち
        if (_pendingBytes != null)
        {
            try
            {
                // すべてメインで：バイト列 → Image → Texture2D
                var img = Raylib.LoadImageFromMemory(_pendingExt ?? ".png", _pendingBytes);
                Native = Raylib.LoadTextureFromImage(img);
                Raylib.UnloadImage(img);

                Volatile.Write(ref _asyncState, 1);
            }
            catch { _asyncState = -1; }
            finally { _pendingBytes = null; _pendingExt = null; }
            return;
        }
        // タイムアウト判定
        long elapsed = Environment.TickCount64 - _startTicks;
        if (TimeoutMs > 0 && elapsed >= TimeoutMs)
        {
            Log.Debug($"Texture: Load timeout: {Path}");
            Dispose();
            return;
        }
    }
    private byte[]? _pendingBytes;
    private string? _pendingExt; // ".png" ".ogg" など
    #endregion
    public DrawOptions? Option { get; set; } = new DrawOptions();
    public void Draw(double x, double y, DrawOptions? options)
    {
        if (!Enable) return;
        var use = options ?? Option ?? new DrawOptions();
        SetOptions(use);
        (double width, double height) = use.Rectangle.HasValue
            ? (use.Rectangle.Value.Width, use.Rectangle.Value.Height)
            : (Width, Height);

        var point = use.Position ?? (GetAnchorOffset(use.Point, width, height) * -1);
        double opacity = Math.Clamp(use.Opacity, 0.0, 1.0);
        var color = use.Color ?? Color.White;
        float defscale = (float)Drawing.DefaultScale;
        float fx = (float)(x * defscale);
        float fy = (float)(y * defscale);
        (double w, double h) = use.Scale;
        double angle = use.Angle;
        (int tx, int ty) = (use.Flip.X ? -1 : 1, use.Flip.Y ? -1 : 1);

        // ★ 宛先座標系での origin（拡大後の量に変換）
        var origin = new System.Numerics.Vector2(
            x: (float)(point.X * Math.Abs(w)),
            y: (float)(point.Y * Math.Abs(h))
        );

        var rect = use.Rectangle ?? new(0, 0, Width, Height);
        // src は TurnX/TurnY で反転（幅/高さを負にする）
        var srcRect = new Rectangle(
            (float)rect.X, (float)rect.Y,
            (float)rect.Width * tx,
            (float)rect.Height * ty
        );
        // 宛先サイズ（常に正）※拡大後の大きさ
        float destW = (float)(rect.Width * Math.Abs(w));
        float destH = (float)(rect.Height * Math.Abs(h));
        // dst は (x,y) を「アンカー位置」として渡す
        var dstRect = new Rectangle(fx, fy, destW, destH);

        DrawTexturePro(Native, srcRect, dstRect, origin,
            360 * (float)angle, ToRayColor(color, opacity));

        ResetOptions(use);
    }
}
