using static AstrumLoom.LayoutUtil;

namespace AstrumLoom;

/// <summary>プラットフォームが実装するテクスチャ1枚分の実体。Textureクラスがこれをラップする。</summary>
public interface ITexture : IResourse
{
    int Width { get; }
    int Height { get; }

    void Draw(double x, double y, DrawOptions option);
}

public static class TextureExtensions
{
    public static void Draw(this ITexture texture, double x = 0, double y = 0)
        => texture.Draw(x, y, new());
}
/// <summary>
/// テクスチャのラッパー。実体（ITexture）がnull（未ロード）でも安全に振る舞い、
/// Color/Scale/Angle等の描画オプションをDrawOptionsとして内部に保持してDraw時に反映する（プロキシプロパティ群）。
/// </summary>
public class Texture : IDisposable
{
    private ITexture? _texture { get; set; } = null;
    public Texture() { }

    /// <summary>drawAction（Action method）を焼き込むレンダーターゲットとしてテクスチャを作る。</summary>
    public Texture(Size size, Action method)
        => _texture = AstrumCore.Platform?.CreateTexture((int)size.Width, (int)size.Height, method);

    public Texture(string path)
        => _texture = AstrumCore.Platform?.LoadTexture(path);

    public ITexture Interface => _texture!;

    public void Draw(double x = 0, double y = 0)
        => _texture?.Draw(x, y, Option);
    public void Draw(double x, double y, DrawOption? option)
        => _texture?.Draw(x, y, Option.Temp(option));
    // option が渡された場合に黙って捨てて既定の Option をそのまま使っていた。
    // Draw(double, double, DrawOption?) と同じく Option.Temp(option) を経由させる。
    public void Draw(Point point, DrawOption? option = null) => _texture?.Draw(point.X, point.Y, Option.Temp(option));

    public void Draw(double x, double y,
        txScale? s = null,
        ReferencePoint? point = null,
        double? opacity = null,
        Color? color = null,
        BlendMode? blend = null)
    {
        var opt = new DrawOption()
        {
            Point = point,
            Color = color,
            Blend = blend,
            Opacity = opacity,
            Scale = s,
        };
        Draw(x, y, opt);
    }
    /// <summary>X/Yスケールを表す小さな値型。double単体やタプルから暗黙変換できるので、等倍・非等倍のどちらも書きやすい。</summary>
    public struct txScale
    {
        public double X;
        public double Y;
        public static implicit operator txScale(double value)
            => new() { X = value, Y = value };
        public static implicit operator txScale((double x, double y) value)
            => new() { X = value.x, Y = value.y };
        public static implicit operator (double W, double H)(txScale value)
            => (value.X, value.Y);
    }

    public void Draw(double x, double y, Rect rectangle)
        => DrawRect(x, y, rectangle);
    /// <summary>指定サイズにフィットするようScaleを一時的に変更して描画し、直後に元の値へ戻す。</summary>
    public void DrawSize(double x, double y, Size size)
    {
        // 変更前の Scale を退避し、描画後に必ず戻す。DrawRect が Rectangle を
        // 一時的に差し替えて戻しているのと同じやり方。ここを戻さないと、この呼び出し以降の
        // 通常 Draw() まで縮尺が汚染されたままになってしまう。
        var before = XYScale;
        XYScale = (size.Width / Width, size.Height / Height);
        // Draw(x, y) の 2 引数オーバーロード（TextureExtensions）は既定の DrawOptions を
        // 生成して呼ぶだけなので、ここで設定した Scale を含め Color/Opacity/Point/Angle/Flip/
        // Rectangle 等の既存設定が全部無視されていた。Option を渡す Draw(x, y, Option) 経由にする。
        _texture?.Draw(x, y, Option);
        XYScale = before;
    }
    /// <summary>テクスチャの一部矩形（切り出し範囲）だけを描画する。Rectangleを一時的に差し替えて元に戻す。</summary>
    public void DrawRect(double x, double y, Rect rectangle)
    {
        Rect? before = Rectangle != null ? new Rect(Rectangle.Value.X, Rectangle.Value.Y, Rectangle.Value.Width, Rectangle.Value.Height) : null;
        Rectangle = rectangle;
        _texture?.Draw(x, y, Option);
        Rectangle = before;
    }
    /// <summary>デバッグ用。テクスチャの外形と20px間隔のグリッド線を描く。</summary>
    public void Grid(double x, double y)
    {
        var tplt = LayoutUtil.GetAnchorOffset(Point, Width * Scale, Height * Scale);
        x += tplt.X; y += tplt.Y;
        Drawing.Box(x, y, Width, Height, Color.DarkGray, 1);
        for (int i = 0; i < Width; i += 20)
        {
            Drawing.Line(x + i, y, 0, Height, Color.Gray);
        }
        for (int j = 0; j < Height; j += 20)
        {
            Drawing.Line(x, y + j, Width, 0, Color.Gray);
        }
    }

    public void Pump() => _texture?.Pump();

    ~Texture() => Dispose();

    public void Dispose()
    {
        AstrumCore.RequestDispose(_texture!);
        GC.SuppressFinalize(this);
    }

    public override string ToString()
    {
        string name = Enable && string.IsNullOrEmpty(Path) ? "Manual draw" : System.IO.Path.GetFileName(Path);
        return !Loaded
            ? "Loading Texture\n" + name
            : !Enable
            ? "Disabled Texture\n" + name
            : $"Texture : {name}\n" +
               $"Size: {Width} x {Height}\n" +
               $"Opacity: {Opacity:F2}, Scale: {Scale:F2}\n" +
               $"Color: {Color}, BlendMode: {BlendMode}";
    }

    public string Path => _texture?.Path ?? "";
    public int Width => _texture?.Width ?? 0;
    public int Height => _texture?.Height ?? 0;

    public bool IsReady => _texture?.IsReady ?? false;
    public bool IsFailed => _texture?.IsFailed ?? false;
    public bool Loaded => _texture?.Loaded ?? false;
    public bool Enable => _texture?.Enable ?? false;

    #region DrawOptions Proxy
    private DrawOptions Option = new();
    public double Opacity
    {
        get => Option.Opacity;
        set => Option.Opacity = value;
    }
    public double Scale
    {
        get => Option.Scale.W;
        set => Option.Scale = (value, value);
    }
    public (double X, double Y)? XYScale
    {
        get => Option.Scale;
        set => Option.Scale = value != null ? value.Value : (1.0, 1.0);
    }
    public Point? Position
    {
        get => Option.Position;
        set => Option.Position = value;
    }
    public ReferencePoint Point
    {
        get => Option.Point;
        set => Option.Point = value;
    }
    public Rect? Rectangle
    {
        get => Option.Rectangle;
        set => Option.Rectangle = value;
    }
    public Color Color
    {
        get => Option.Color ?? Color.White;
        set => Option.Color = value;
    }
    public BlendMode BlendMode
    {
        get => Option.Blend;
        set => Option.Blend = value;
    }
    public double Angle
    {
        get => Option.Angle;
        set => Option.Angle = value;
    }
    public (bool X, bool Y) Flip
    {
        get => Option.Flip;
        set => Option.Flip = value;
    }
    #endregion

    public Size Size => new(Width, Height);
    public Size ScaledSize => new(
        (int)(Width * Scale * Drawing.DefaultScale),
        (int)(Height * Scale * Drawing.DefaultScale)
    );

    public void SetColor(Color color, Color? add = null)
    {
        Color = color;
        if (add != null)
        {
            //AddColor = add.Value;
        }
    }

    public void Expand(double width, double height)
        => XYScale = (width / Width, height / Height);

    /// <summary>同じパスから新たにロードし直し、現在の描画オプション（色・スケール等）だけをコピーした別インスタンスを作る。</summary>
    public Texture Clone()
    {
        var tex = new Texture(Path);
        tex.Import(Export());
        return tex;
    }

    /// <summary>現在の描画オプション一式をDrawOptionsとして取り出す。</summary>
    public DrawOptions Export() => new()
    {
        Color = Color,
        //AddColor = AddColor,
        Blend = BlendMode,
        Opacity = Opacity,
        Scale = XYScale ?? (Scale, Scale),
        Angle = Angle,
        Position = Position,
        Point = Point,
        Rectangle = Rectangle,
        Flip = Flip,
    };
    /// <summary>DrawOptions一式をこのテクスチャのプロキシプロパティへ反映する。ScaleはW/Hが等しければ等倍のScale、違えばXYScaleに割り振る。</summary>
    public void Import(DrawOptions opt)
    {
        Color = opt.Color ?? Color.White;
        //AddColor = opt.AddColor;
        BlendMode = opt.Blend;
        Opacity = opt.Opacity;
        Angle = opt.Angle;
        if (opt.Scale.W == opt.Scale.H)
            Scale = opt.Scale.W;
        else XYScale = opt.Scale;
        Position = opt.Position;
        Point = opt.Point;
        Rectangle = opt.Rectangle;
        Flip = opt.Flip;
    }
    public void ResetOption() => Import(new());

    public void Draw(double x, double y, Rect rectangle, Point? point = null)
    {
        var options = new DrawOptions()
        {
            Color = Color,
            Blend = BlendMode,
            Opacity = Opacity,
            Scale = XYScale ?? (Scale, Scale),
            Angle = Angle,
            Position = point ?? Position,
            Point = Point,
            Rectangle = rectangle,
            Flip = Flip,
            EdgeColor = Option.EdgeColor,
            Font = Option.Font,
            Thickness = Option.Thickness,
        };
        _texture?.Draw(x, y, options);
    }
}

/// <summary>Action(drawAction)を焼き込んだテクスチャを1枚だけ保持し、2回目以降のGetは再生成せず使い回すキャッシュ。</summary>
public class TextureCathe
{
    private Texture? Cathe;
    public Texture Get(Action method, Size size)
    {
        if (Cathe != null) return Cathe;
        Cathe = new Texture(size, method);
        return Cathe;
    }
}