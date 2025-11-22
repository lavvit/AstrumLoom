using static AstrumLoom.DXLib.DxLibGraphics;
using static AstrumLoom.LayoutUtil;
using static DxLibDLL.DX;
namespace AstrumLoom.DXLib;

internal sealed class DxLibTexture : ITexture
{
    public string Path { get; private set; } = "";
    public int Handle { get; private set; } = -1;
    public int Width { get; private set; } = 0;
    public int Height { get; private set; } = 0;

    public DxLibTexture(int handle)
    {
        // サイズ取得
        if (GetGraphSize(handle, out int w, out int h) != 0)
        {
            // 失敗してもとりあえず 0 のまま返す
            w = h = 0;
        }
        Handle = handle;
        Width = w;
        Height = h;
        Volatile.Write(ref _asyncState, 1); // Ready
    }
    public DxLibTexture(string path)
    {
        Path = path;
        Load();
    }
    ~DxLibTexture()
    {
        Dispose();
    }
    public void Dispose()
    {
        if (Handle > 0)
        {
            DeleteGraph(Handle);
        }
        Handle = -1;
        _asyncState = -1;
    }

    #region 読み込み
    public void Load()
    {
        if (!File.Exists(Path))
        {
            Log.Debug($"Texture: not found: {Path}");
            Volatile.Write(ref _asyncState, -1);
            Handle = -1;
            return;
        }
        // メインスレッドでのみ触る
        if (!IsMainThread)
        {
            _deferred = true;
            _asyncState = 0;   // Loading扱い
            return;
        }
        else
        {
            int handle = LoadGraph(Path);
            if (handle < 0)
            {
                Log.Debug($"Texture: Load failed: {Path}");
                Volatile.Write(ref _asyncState, -1);
                Handle = -1;
                return;
            }
            SetUseTransColor(FALSE);                 // 色キー透過は使わない
            SetUsePremulAlphaConvertLoad(TRUE);      // 重要！アルファ縁のにじみ対策（プリマルチ化）
            SetDrawBlendMode(DX_BLENDMODE_ALPHA, 255);   // 念のため標準ブレンドに戻す
            SetDrawBright(255, 255, 255);
            SetDrawAddColor(0, 0, 0);
            Handle = handle;
            _startTicks = Environment.TickCount64;
            // サイズ取得
            if (GetGraphSize(handle, out int w, out int h) != 0)
            {
                // 失敗してもとりあえず 0 のまま返す
                w = h = 0;
            }
            Width = w;
            Height = h;
            // 非同期かどうかを即チェック（ここはメインスレッド想定）
            Volatile.Write(ref _asyncState, (CheckHandleASyncLoad(Handle) == 0) ? 1 : 0);
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
    public bool Enable => Handle > 0 && Loaded;
    private static bool IsMainThread => Environment.CurrentManagedThreadId == AstrumCore.MainThreadId;
    private bool _deferred;
    private long _startTicks;
    private const int DefaultTimeoutMs = 15000;
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    public void Pump()
    {
        // メインスレッドでのみ触る
        if (!IsMainThread) return;

        if (Handle > 0 && Width + Height == 0)
        {
            // サイズ取得
            if (GetGraphSize(Handle, out int w, out int h) != 0)
            {
                // 失敗してもとりあえず 0 のまま返す
                w = h = 0;
            }
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
        if (CheckHandleASyncLoad(Handle) == 0)
        {
            Volatile.Write(ref _asyncState, 1); // Ready
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
    #endregion

    public DrawOptions? Option { get; set; } = new DrawOptions();
    public void Draw(double x, double y, DrawOptions? options)
    {
        if (!Enable) return;
        var use = options ?? Option ?? new DrawOptions();
        SetOptions(use);

        var point = use.Position ?? (GetAnchorOffset(use.Point, Width, Height) * -1);
        float defscale = (float)Drawing.DefaultScale;
        float fx = (float)(x * defscale);
        float fy = (float)(y * defscale);
        (double w, double h) = use.Scale;
        double angle = use.Angle * Math.PI;
        (int tx, int ty) = (use.Flip.X ? 1 : 0, use.Flip.Y ? 1 : 0);
        if (use.Rectangle.HasValue)
        {
            var rect = use.Rectangle.Value;
            DrawRectRotaGraph3F(fx, fy,
                (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height,
                (float)point.X, (float)point.Y, w * defscale, h * defscale,
                angle, Handle, TRUE, tx, ty);
        }
        else
        {
            DrawRotaGraph3F(fx, fy, (float)point.X, (float)point.Y, w * defscale, h * defscale,
                angle, Handle, TRUE, tx, ty);
        }
        ResetOptions(use);
    }
    private void SetOptions(DrawOptions? options)
    {
        var use = options ?? Option ?? new DrawOptions();
        double opacity = Math.Clamp(use.Opacity, 0.0, 1.0);
        var color = use.Color ?? Color.White;
        opacity *= color.A / 255.0;

        if (use.Blend != BlendMode.None)
            SetDrawBlendMode(GetBlendMode(use.Blend), (int)(255.0 * opacity));
        if (color != Color.White)
            SetDrawBright(color.R, color.G, color.B);
    }
    private void ResetOptions(DrawOptions? options)
    {
        var use = options ?? Option ?? new DrawOptions();
        var color = use.Color ?? Color.White;
        if (use.Blend != BlendMode.None)
            SetDrawBlendMode(DX_BLENDMODE_ALPHA, 255);
        if (color != Color.White)
            SetDrawBright(255, 255, 255);
    }
}
