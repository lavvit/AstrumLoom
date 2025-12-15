using PointVector = AstrumLoom.LayoutUtil.Point;
using SizeVector = AstrumLoom.LayoutUtil.Size;

namespace AstrumLoom.Extend;

#region Base Classes
internal class Opacity
{
    /// <summary>
    /// Start透明度
    /// </summary>
    public float StartOpacity { get; set; }

    /// <summary>
    /// End透明度
    /// </summary>
    public float EndOpacity { get; set; }

    public UfEasing Easing { get; set; } = UfEasing.Linear;
}
internal class Position
{
    /// <summary>
    /// Start座標
    /// </summary>
    public PointVector StartPosition { get; set; }

    /// <summary>
    /// End座標
    /// </summary>
    public PointVector EndPosition { get; set; }
    public UfEasing Easing { get; set; } = UfEasing.Linear;
}
internal class Rotation
{
    /// <summary>
    /// Start回転角度
    /// </summary>
    public float StartRotation { get; set; }

    /// <summary>
    /// End回転角度
    /// </summary>
    public float EndRotation { get; set; }
    public UfEasing Easing { get; set; } = UfEasing.Linear;
}
internal class Scale
{
    /// <summary>
    /// Start拡大率
    /// </summary>
    public float StartScale { get; set; }

    /// <summary>
    /// End拡大率
    /// </summary>
    public float EndScale { get; set; }
    public UfEasing Easing { get; set; } = UfEasing.Linear;
}

/// <summary>
/// 位置、拡大率、角度、透明度を表すクラス
/// </summary>
public class Transfrom
{
    /// <summary>
    /// 座標
    /// </summary>
    public PointVector Position { get; set; } = new(0.0f, 0.0f);

    /// <summary>
    /// 拡大率
    /// </summary>
    public SizeVector Scale { get; set; } = new(1.0f, 1.0f);

    /// <summary>
    /// 回転角度
    /// </summary>
    public float Rotation { get; set; } = 0.0f;

    /// <summary>
    /// 透明度
    /// </summary>
    public float Opacity { get; set; } = 1.0f;

    /// <summary>
    /// 反転するか X
    /// </summary>
    public bool ReverseX { get; set; } = false;

    /// <summary>
    /// 反転するか Y
    /// </summary>
    public bool ReverseY { get; set; } = false;
}
#endregion
#region Object

/// <summary>
/// オブジェクトの基底クラス
/// </summary>
public class Object
{
    /// <summary>
    /// 開始フレーム
    /// </summary>
    public int StartFrame { get; set; }

    /// <summary>
    /// 終了フレーム
    /// </summary>
    public int EndFrame { get; set; }

    /// <summary>
    /// レイヤー
    /// </summary>
    public int Layer { get; set; }

    /// <summary>
    /// フィルターのリスト
    /// </summary>
    public List<Filter> Filters { get; set; } = [];

    /// <summary>
    /// 位置、拡大率、角度
    /// </summary>
    public Transfrom Transfrom { get; set; } = new();
}

/// <summary>
/// グループ制御オブジェクト
/// </summary>
internal class GroupObject : Object
{
    public GroupObject(Object Object)
    {
        StartFrame = Object.StartFrame;
        EndFrame = Object.EndFrame;
        Layer = Object.Layer;
        Transfrom = new Transfrom();
    }
    /// <summary>
    /// 位置
    /// </summary>
    public Position Position { get; set; } = new Position();

    /// <summary>
    /// 拡大率
    /// </summary>
    public Scale Scale { get; set; } = new Scale();

    /// <summary>
    /// 回転
    /// </summary>
    public Rotation Rotation { get; set; } = new Rotation();

    /// <summary>
    /// 上位グループ制御の影響を受けるか
    /// </summary>
    public bool AffectUpperGroup { get; set; }

    /// <summary>
    /// グループ制御の適応範囲
    /// </summary>
    public int Range { get; set; }
}

/// <summary>
/// 画像オブジェクト
/// </summary>
internal class ImageObject : Object
{
    public ImageObject(Object Object)
    {
        StartFrame = Object.StartFrame;
        EndFrame = Object.EndFrame;
        Layer = Object.Layer;
        Transfrom = new Transfrom();
    }

    /// <summary>
    /// 画像
    /// </summary>
    public Texture? Texture { get; set; }

    /// <summary>
    /// 位置
    /// </summary>
    public Position Position { get; set; } = new Position();

    /// <summary>
    /// 拡大率
    /// </summary>
    public Scale Scale { get; set; } = new Scale();

    /// <summary>
    /// 回転
    /// </summary>
    public Rotation Rotation { get; set; } = new Rotation();

    /// <summary>
    /// 透明度
    /// </summary>
    public Opacity Opacity { get; set; } = new Opacity();

    /// <summary>
    /// ブレンドモード
    /// </summary>
    public int BlendMode { get; set; }

    /// <summary>
    /// グループ制御のリスト
    /// </summary>
    public List<GroupObject> GroupObjects { get; set; } = [];

    /// <summary>
    /// 上位グループ制御の影響を受けるかどうかのフラグ
    /// </summary>
    public bool IsAffectUpperGroup { get; set; }

    public void Draw(float x, float y, float scale)
    {
        if (Texture == null) return;
        Texture.Point = ReferencePoint.Center;
        Texture.XYScale = (Transfrom.Scale.Width * scale, Transfrom.Scale.Height * scale);
        Texture.Angle = Transfrom.Rotation;
        Texture.Opacity = Transfrom.Opacity;
        Texture.Draw(x + Transfrom.Position.X * scale, y + Transfrom.Position.Y * scale);
    }

    public ImageObject Clone() => (ImageObject)this.MemberwiseClone();
}
#endregion
#region Filter

/// <summary>
/// フィルターの基底クラス
/// </summary>
public class Filter
{
    public FilterType FilterType { get; set; }
}

public enum FilterType
{
    None,
    Scale,
    Rotation,
    Opacity,
    Reverse
}

/// <summary>
/// 透明度フィルター
/// </summary>
internal class OpacityFilter : Filter
{
    /// <summary>
    /// 透明度
    /// </summary>
    public Opacity Opacity { get; set; } = new();
}

/// <summary>
/// 反転フィルター
/// </summary>
public class ReverseFilter : Filter
{
    public bool ReverseX { get; set; }
    public bool ReverseY { get; set; }
}

/// <summary>
/// 回転フィルター
/// </summary>
internal class RotationFilter : Filter
{
    /// <summary>
    /// 回転
    /// </summary>
    public Rotation Rotation { get; set; } = new();

}

/// <summary>
/// 拡大縮小フィルター
/// </summary>
internal class ScaleFilter : Filter
{
    public float StartBaseScale { get; set; }
    public float EndBaseScale { get; set; }

    public SizeVector StartScale { get; set; } = new(1.0f, 1.0f);
    public SizeVector EndScale { get; set; } = new(1.0f, 1.0f);
}
#endregion
#region Easing
public class AnimationEasing
{
    public static float Get(UfEasing easing, float t)
    {
        if (easing == UfEasing.Linear) return t;

        var ease = (EEasing)(((int)easing + 2) / 4);
        var inout = (EInOut)(((int)easing + 2) % 4);

        return (int)inout <= 2
            ? (float)Easing.Ease(t, 1, 0, 1, ease, inout, 8)
            : t < 0.5
                ? (float)Easing.Ease(t * 2, 1, 0, 1, ease, EInOut.Out, 8) / 2
                : (float)Easing.Ease((t - 0.5) * 2, 1, 0, 1, ease, EInOut.In, 8) / 2 + 0.5f;
    }
}

/// <summary>
/// UndoFishイージング
/// https://www.nicovideo.jp/watch/sm20813281
/// </summary>
public enum UfEasing
{
    Linear = 1,
    InSine = 2,
    OutSine = 3,
    InOutSine = 4,
    OutInSine = 5,
    InQuad = 6,
    OutQuad = 7,
    InOutQuad = 8,
    OutInQuad = 9,
    InCubic = 10,
    OutCubic = 11,
    InOutCubic = 12,
    OutInCubic = 13,
    InQuart = 14,
    OutQuart = 15,
    InOutQuart = 16,
    OutInQuart = 17,
    InQuint = 18,
    OutQuint = 19,
    InOutQuint = 20,
    OutInQuint = 21,
    InExpo = 22,
    OutExpo = 23,
    InOutExpo = 24,
    OutInExpo = 25,
    InCirc = 26,
    OutCirc = 27,
    InOutCirc = 28,
    OutInCirc = 29,
    InElastic = 30,
    OutElastic = 31,
    InOutElastic = 32,
    OutInElastic = 33,
    InBack = 34,
    OutBack = 35,
    InOutBack = 36,
    OutInBack = 37,
    InBounce = 38,
    OutBounce = 39,
    InOutBounce = 40,
    OutInBounce = 41
}
#endregion