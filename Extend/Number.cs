namespace AstrumLoom;

/// <summary>
/// 数字を描画するクラス
/// </summary>
public class Number
{
    /// <summary>
    /// 数字のパーツ
    /// </summary>
    public NumPart[] Nums = [];
    private char[] _chars =
        [
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9'
        ];
    public char[] Chars
    {
        get => _chars;
        set
        {
            _chars = value;
            Nums = new NumPart[_chars.Length];
            for (int i = 0; i < _chars.Length; i++)
            {
                Nums[i] = new NumPart(_chars[i], i, 0, Width, Height);
            }
        }
    }
    /// <summary>
    /// 数字のテクスチャ
    /// </summary>
    public Texture Texture = new();

    /// <summary>
    /// 数字の幅、高さ、間隔
    /// </summary>
    public int Width, Height, Space;

    /// <summary>
    /// 数字の開始位置
    /// </summary>
    public int StartX, StartY;

    public Number() { }
    /// <summary>
    /// 数字を描画するクラス
    /// </summary>
    /// <param name="path">数字の画像のパス</param>
    /// <param name="parts">数字のパーツ</param>
    public Number(string path, char[]? parts = null)
        : this(path, 0, 0, parts) { }

    /// <summary>
    /// 数字を描画するクラス
    /// </summary>
    /// <param name="path">数字の画像のパス</param>
    /// <param name="width">数字の幅</param>
    /// <param name="height">数字の高さ</param>
    /// <param name="parts">数字のパーツ</param>
    /// <param name="startx">数字の開始位置X</param>
    /// <param name="starty">数字の開始位置Y</param>
    public Number(string path, int width, int height, char[]? parts = null, int startx = 0, int starty = 0)
    {
        Texture = new Texture(path);

        Width = width;
        Height = height;
        Space = 0;
        StartX = startx;
        StartY = starty;
        _chars = parts ??
        [
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9'
        ];

        InitSize();
    }

    /// <summary>テクスチャを解放し、パーツ情報をクリアします。</summary>
    public void Dispose()
    {
        Texture.Dispose();
        Nums = [];
    }

    /// <summary>
    /// テクスチャのサイズと文字リストから、各文字が画像上のどこにあるか（NumPart）を計算します。
    /// Width/Height が未指定（0）ならテクスチャのサイズから自動算出します。
    /// </summary>
    public void InitSize()
    {
        Width = Width > 0 ? Width : Texture.Width / _chars.Length;
        Height = Height > 0 ? Height : Texture.Height;
        Nums = new NumPart[_chars.Length];
        int x = 0;
        int y = 0;
        for (int i = 0; i < _chars.Length; i++)
        {
            Nums[i] = new NumPart(_chars[i], x, y, Width, Height);
            Nums[i].x += Width * StartX;
            Nums[i].y += Height * StartY;
            // 1行に収まる文字数を超えたら次の行へ折り返す
            if (Nums[i].x + Width >= Width * (_chars.Length + StartX))
            {
                x = 0;
                y++;
            }
            else x++;
        }
        ;
    }

    /// <summary>
    /// テクスチャの読み込みとサイズ計算が完了しているか。未完了ならここで InitSize を試みる。
    /// </summary>
    public bool Loaded
    {
        get
        {
            bool loaded = Texture.Loaded;
            bool sizeok = Width > 0 && Height > 0 && Nums.Length > 0;
            if (loaded && !sizeok)
            {
                InitSize();
            }
            return loaded && sizeok;
        }
    }

    /// <summary>
    /// 数字を描画する
    /// </summary>
    /// <param name="x"> X座標 </param>
    /// <param name="y"> Y座標 </param>
    /// <param name="num"> 数字 </param>
    /// <param name="type"> 数字の種類 </param>
    /// <param name="opacity"> 透明度 </param>
    /// <param name="point"> 基準点 </param>
    /// <param name="left"> 左詰め </param>
    /// <param name="scaleadd"> X拡大率を加算するか </param>
    public void Draw(double x, double y, object num,
        int type = 0, double opacity = 1, ReferencePoint point = ReferencePoint.TopLeft, int left = 0, bool scaleadd = true) => Draw(x, y, num, 1.0, type, opacity, point, left, scaleadd);
    /// <summary>
    /// 数字を描画する
    /// </summary>
    /// <param name="x"> X座標 </param>
    /// <param name="y"> Y座標 </param>
    /// <param name="num"> 数字 </param>
    /// <param name="size"> 拡大率 </param>
    /// <param name="type"> 数字の種類 </param>
    /// <param name="opacity"> 透明度 </param>
    /// <param name="point"> 基準点 </param>
    /// <param name="left"> 左詰め </param>
    /// <param name="scaleadd"> X拡大率を加算するか </param>
    public void Draw(double x, double y, object num, double size,
        int type = 0, double opacity = 1, ReferencePoint point = ReferencePoint.TopLeft, int left = 0, bool scaleadd = true) => Draw(x, y, num, size, size, type, opacity, point, left, scaleadd);
    /// <summary>
    /// 数字を描画する
    /// </summary>
    /// <param name="x"> X座標 </param>
    /// <param name="y"> Y座標 </param>
    /// <param name="num"> 数字 </param>
    /// <param name="scaleX"> X方向の拡大率 </param>
    /// <param name="scaleY"> Y方向の拡大率 </param>
    /// <param name="type"> 数字の種類 </param>
    /// <param name="opacity"> 透明度 </param>
    /// <param name="point"> 基準点 </param>
    /// <param name="left"> 左詰め </param>
    /// <param name="scaleadd"> X拡大率を加算するか </param>
    public void Draw(double x, double y, object num, double scaleX, double scaleY,
        int type = 0, double opacity = 1, ReferencePoint point = ReferencePoint.TopLeft, int left = 0, bool scaleadd = true)
    {
        if (!Loaded) return;
        if (left > 0) x -= (Size(num) - Width) / (left > 1 ? 1 : 2);
        foreach (char ch in $"{num}")
        {
            for (int i = 0; i < Nums.Length; i++)
            {
                if (ch == ' ')
                {
                    break;
                }
                if (Nums[i].ch == ch)
                {
                    if (Texture.Loaded)
                    {
                        Texture.Rectangle = new(Nums[i].x, Nums[i].y + Height * type, Width, Height);
                        Texture.XYScale = (scaleX, scaleY);
                        Texture.Opacity = opacity;
                        Texture.Point = point;
                        Texture.Draw(x, y);
                    }
                    else Drawing.Text(x, y, ch.ToString());
                    break;
                }
            }
            x += (Width + Space) * (scaleadd ? scaleX : 1);
        }
    }
    /// <summary>
    /// 数字の幅を取得する
    /// </summary>
    /// <param name="num"> 数字 </param>
    /// <param name="size"> 拡大率 </param>
    /// <param name="comprate"> 圧縮率 </param>
    public double Size(object num, double size = 1, double comprate = 1)
    {
        double x = 0;
        foreach (char ch in $"{num}")
        {
            x += (Width + Space) * size * comprate;
        }
        return x;
    }
    /// <summary>
    /// 数字の桁数を取得する
    /// </summary>
    /// <param name="num"> 数字 </param>
    public static double Amount(object num)
    {
        double x = 0;
        foreach (char ch in $"{num}")
        {
            x++;
        }
        return x;
    }

    public override string ToString() => $"{Path.GetFileName(Texture.Path)},{Width}*{Height},{Nums} ({Texture})";
}

/// <summary>
/// 1文字分の数字パーツ情報。テクスチャ上での位置（グリッド座標×セルサイズ）と対応する文字を保持する。
/// </summary>
public struct NumPart
{
    public char ch;
    public int x;
    public int y;

    /// <summary>グリッド座標 (x,y) をセルサイズ (width,height) 倍してピクセル座標に変換して保持します。</summary>
    public NumPart(char ch, int x, int y, int width, int height)
    {
        this.ch = ch;
        this.x = x * width;
        this.y = y * height;
    }
    /// <summary>"文字,X,Y" 形式の文字列から復元します（先頭1文字が文字、以降がカンマ区切りの座標）。</summary>
    public NumPart(string str)
    {
        ch = str[0];
        string[] split = str[2..].Split(',');
        int.TryParse(split[0], out x);
        int.TryParse(split[1], out y);
    }
    public override string ToString() => $"{ch},{x},{y}";
}