using static AstrumLoom.DXLib.DxLibGraphics;
using static AstrumLoom.LayoutUtil;
using static DxLibDLL.DX;
namespace AstrumLoom.DXLib;

/// <summary>DxLibバックエンドでのテクスチャ実装。ロード/破棄はAsyncLoadableBase経由でメインスレッドに委ねる。</summary>
internal sealed class DxLibTexture : AsyncLoadableBase, ITexture
{
    public string Path { get; private set; } = "";
    public int Handle { get; private set; } = -1;
    public int Width { get; private set; } = 0;
    public int Height { get; private set; } = 0;

    /// <summary>既に作成済みのDxLibグラフィックハンドル（MakeScreen等）をそのままラップするコンストラクタ。DxLibPlatform.CreateTextureのレンダーターゲット用途で使う。</summary>
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
        // WriteState(State_Success) を直接呼ぶだけだと Load() 経由と違って _obj が設定されず、
        // このコンストラクタ（DxLibPlatform.CreateTexture の描画ターゲット用テクスチャなど）も
        // Dispose が no-op になってしまう。LoadAsync(this) は _loadfunc が未設定なら
        // 呼び出し元がメインスレッドである限り WriteState(State_Success) を呼ぶだけなので、
        // 結果は今までと同じまま _obj だけ正しく登録できる
        // （DxLibPlatform.CreateTexture はメインスレッド以外からの呼び出しを事前に弾いているので安全）。
        LoadAsync(this);
    }
    /// <summary>ファイルパスからテクスチャをロードするコンストラクタ。</summary>
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
        DisposeAsync(DisposeTx);
        GC.SuppressFinalize(this);
    }
    /// <summary>実際にDeleteGraphを叩く破棄処理本体。メインスレッド以外から来た場合は自分自身をAstrumCore.RequestDisposeへ積み直す。</summary>
    private bool DisposeTx()
    {
        if (IsMainThread)
        {
            try
            {
                if (Handle > 0)
                    DeleteGraph(Handle);
                Handle = -1;
                return true;
            }
            catch { Log.Error($"Failed to unload texture: {Path}"); }
        }
        else
        {
            //Log.Debug($"Texture dispose skipped: not main thread : {Path}");
            AstrumCore.RequestDispose(this);
            return true;
        }
        return false;
    }

    #region 読み込み
    // 第1引数に this（IDisposable）を渡さないと AsyncLoadableBase._obj が null のままになり、
    // Dispose() → DisposeAsync が Host.cs の「_obj == null なら即 return」ガードに引っかかって
    // DisposeTx が一度も呼ばれない＝DeleteGraph に到達せずハンドルが解放されない。
    public void Load() => LoadAsync(this, LoadTx);
    /// <summary>LoadGraphでテクスチャをロードし、サイズを取得する。非同期ロード中ならState_Loadingのまま返す。</summary>
    private bool LoadTx()
    {
        bool file = FileCheck(Path);
        if (!file) return false;

        SetUseTransColor(FALSE);                 // 色キー透過は使わない
        SetUsePremulAlphaConvertLoad(TRUE);      // 重要！アルファ縁のにじみ対策（プリマルチ化）

        int handle = LoadGraph(Path);
        if (handle < 0)
        {
            Log.Debug($"Texture: Load failed: {Path}");
            Handle = -1;
            return false;
        }
        SetDrawBlendMode(DX_BLENDMODE_ALPHA, 255);   // 念のため標準ブレンドに戻す
        SetDrawBright(255, 255, 255);
        SetDrawAddColor(0, 0, 0);
        Handle = handle;
        // サイズ取得
        if (GetGraphSize(handle, out int w, out int h) != 0)
        {
            // 失敗してもとりあえず 0 のまま返す
            w = h = 0;
        }
        Width = w;
        Height = h;
        // 非同期かどうかを即チェック（ここはメインスレッド想定）
        WriteState((CheckHandleASyncLoad(Handle) == 0) ? State_Success : State_Loading);
        return true;
    }

    public bool Enable => LoadFinished && Handle > 0;
    public bool IsReady => LoadReady;
    public bool IsFailed => LoadFailed;
    public bool Loaded => LoadFinished;

    /// <summary>非同期ロード完了の確認と、まだ取れていなかったサイズの遅延取得を行う。毎フレーム呼ばれる想定。</summary>
    public void Pump()
    {
        PumpAsync();
        if (!IsMainThread) return; // メインスレッドでのみ触る

        // 非同期ロードの完了待ち
        if (Loading && CheckHandleASyncLoad(Handle) == 0)
        {
            WriteState(State_Success);
            return;
        }

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
    }
    #endregion

    /// <summary>
    /// DrawOptionsをDxLibのDrawRotaGraph3F/DrawRectRotaGraph3Fの引数へ変換して描画する。
    /// DxLibの回転描画APIは回転中心を「原点からの相対座標」で受け取るため、ReferencePointをPoint()で
    /// 座標へ変換したうえで絶対値化している。RectangleがあればDrawRectRotaGraph3F（切り出し矩形指定）を、
    /// なければ全体を描くDrawRotaGraph3Fを使う。角度はAngle(0.0〜1.0の周期)をラジアンへ変換して渡す。
    /// </summary>
    public void Draw(double x, double y, DrawOptions option)
    {
        if (!Enable) return;
        var use = option;
        SetOptions(use);
        (double width, double height) = use.Rectangle.HasValue
            ? (use.Rectangle.Value.Width, use.Rectangle.Value.Height)
            : (Width, Height);

        var point = use.Position ?? Point(use.Point, use.Rectangle);// (GetAnchorOffset(use.Point, width, height) * -1);
        point = new(Math.Abs(point.X),
                 Math.Abs(point.Y));
        float defscale = (float)Drawing.DefaultScale;
        float fx = (float)(x * defscale);
        float fy = (float)(y * defscale);
        (double w, double h) = use.Scale;
        w *= defscale; h *= defscale;
        double angle = use.Angle * 2 * Math.PI;
        (int tx, int ty) = (use.Flip.X ? 1 : 0, use.Flip.Y ? 1 : 0);
        if (use.Rectangle.HasValue)
        {
            var rect = use.Rectangle.Value;
            DrawRectRotaGraph3F(fx, fy,
                (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height,
                (float)point.X, (float)point.Y, w, h,
                angle, Handle, TRUE, tx, ty);
        }
        else
        {
            DrawRotaGraph3F(fx, fy, (float)point.X, (float)point.Y, w, h,
                angle, Handle, TRUE, tx, ty);
        }
        ResetOptions(use);
    }
    /// <summary>ReferencePoint（基準点の種類）を、テクスチャ左上原点からの座標オフセットへ変換する。DxLibの回転中心APIに渡すための下準備。</summary>
    private Point Point(ReferencePoint point, Rect? rectangle = null)
    {
        if (!rectangle.HasValue) rectangle = new(0, 0, Width, Height);
        return point switch
        {
            ReferencePoint.TopCenter => new(rectangle.Value.Width / 2, 0),
            ReferencePoint.TopRight => new(rectangle.Value.Width, 0),
            ReferencePoint.CenterLeft => new(0, rectangle.Value.Height / 2),
            ReferencePoint.Center => new(rectangle.Value.Width / 2, rectangle.Value.Height / 2),
            ReferencePoint.CenterRight => new(rectangle.Value.Width, rectangle.Value.Height / 2),
            ReferencePoint.BottomLeft => new(0, rectangle.Value.Height),
            ReferencePoint.BottomCenter => new(rectangle.Value.Width / 2, rectangle.Value.Height),
            ReferencePoint.BottomRight => new(rectangle.Value.Width, rectangle.Value.Height),
            _ => new(0, 0),
        };
    }
}
