using PointVector = AstrumLoom.LayoutUtil.Point;
using SizeVector = AstrumLoom.LayoutUtil.Size;

namespace AstrumLoom.Exo;

#region Base Classes
public class Opacity
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
public class Position
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
public class Rotation
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
public class Scale
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
public class GroupObject : Object
{
    /// <summary>
    /// 元となる <see cref="Object"/> からフレーム・レイヤー情報を引き継いで生成します。
    /// </summary>
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
public class ImageObject : Object
{
    /// <summary>
    /// 元となる <see cref="Object"/> からフレーム・レイヤー情報を引き継いで生成します。
    /// </summary>
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
    /// ブレンドモード（合成モード）
    /// </summary>
    public BlendMode BlendMode { get; set; } = BlendMode.None;

    /// <summary>
    /// グループ制御のリスト
    /// </summary>
    public List<GroupObject> GroupObjects { get; set; } = [];

    /// <summary>
    /// 上位グループ制御の影響を受けるかどうかのフラグ
    /// </summary>
    public bool IsAffectUpperGroup { get; set; }

    /// <summary>
    /// 指定座標・スケールを基準に、Transfrom の内容を反映して画像を描画します。
    /// 合成モードは描画後に必ず None へ戻し、後続の描画に漏れないようにする。
    /// </summary>
    public void Draw(float x, float y, float scale)
    {
        if (Texture == null) return;
        try
        {
            Texture.Point = ReferencePoint.Center;
            Texture.XYScale = (Transfrom.Scale.Width * scale, Transfrom.Scale.Height * scale);
            Texture.Angle = Transfrom.Rotation;
            Texture.Opacity = Transfrom.Opacity;
            Texture.BlendMode = BlendMode;
            Texture.Draw(x + Transfrom.Position.X * scale, y + Transfrom.Position.Y * scale);
        }
        finally
        {
            Texture.BlendMode = AstrumLoom.BlendMode.None;
        }
    }

    /// <summary>浅いコピーを作成します（Texture などの参照は共有されます）。</summary>
    public ImageObject Clone() => (ImageObject)this.MemberwiseClone();
}

/// <summary>
/// サウンド再生オブジェクト
/// </summary>
public class SoundObject : Object
{
    public SoundObject(Object Object)
    {
        StartFrame = Object.StartFrame;
        EndFrame = Object.EndFrame;
        Layer = Object.Layer;
        // SoundObject はトランスフォームを使わないが、基底に合わせてインスタンス化しておく
        Transfrom = new Transfrom();
    }

    /// <summary>
    /// 実際のサウンドリソース（プロジェクト内のサウンド型を利用）
    /// </summary>
    public Sound? Sound { get; set; }

    /// <summary>
    /// 開始ボリューム（0.0 - 1.0）
    /// </summary>
    public float StartVolume { get; set; } = 1.0f;

    /// <summary>
    /// 終了ボリューム（0.0 - 1.0）
    /// </summary>
    public float EndVolume { get; set; } = 1.0f;

    /// <summary>
    /// 左右バランス（-1.0 左 〜 1.0 右）
    /// </summary>
    public float Pan { get; set; } = 0.0f;

    /// <summary>
    /// ループ再生するか
    /// </summary>
    public bool Loop { get; set; } = false;

    /// <summary>
    /// 再生中フラグ（簡易状態管理）
    /// </summary>
    public bool IsPlaying { get; private set; } = false;

    /// <summary>
    /// 再生を開始する
    /// </summary>
    public void Play()
    {
        if (Sound == null) return;
        Sound.Volume = StartVolume;
        Sound.Pan = Pan;
        Sound.Play();
        IsPlaying = true;
    }

    /// <summary>
    /// 再生を停止する
    /// </summary>
    public void Stop()
    {
        if (Sound == null) return;
        Sound.Stop();
        IsPlaying = false;
    }

    /// <summary>
    /// 指定フレームにおけるボリュームを計算して適用する
    /// - フレームが範囲外の場合は自動的に停止（または範囲外であれば開始/停止の振る舞いを変更可能）
    /// </summary>
    public void UpdateVolume(int frame)
    {
        if (Sound == null) return;

        if (frame < StartFrame || frame > EndFrame)
        {
            // 範囲外なら停止して終了
            if (IsPlaying)
            {
                Stop();
            }
            return;
        }

        // フレーム比率を計算
        int duration = EndFrame - StartFrame;
        float t = duration <= 0 ? 0f : (frame - StartFrame) / (float)duration;
        t = Math.Clamp(t, 0f, 1f);

        // イージングに従った補間
        float eased = AnimationEasing.Get(UfEasing.Linear, t);
        float vol = StartVolume + (EndVolume - StartVolume) * eased;

        // 適用
        Sound.Volume = Math.Clamp(vol, 0f, 1f);
        Sound.Pan = Pan;

        // 必要なら再生開始
        if (!IsPlaying)
        {
            Sound.Loop = Loop;
            Sound.Play();
            IsPlaying = true;
        }
    }

    /// <summary>浅いコピーを作成します（Sound などの参照は共有されます）。</summary>
    public SoundObject Clone() => (SoundObject)this.MemberwiseClone();
}

#endregion
#region Filter

/// <summary>
/// フィルターの基底クラス
/// </summary>
public class Filter
{
    /// <summary>このフィルターが表す効果の種類。</summary>
    public FilterType FilterType { get; set; }
}

/// <summary>フィルターが表す効果の種類。</summary>
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
internal class ReverseFilter : Filter
{
    /// <summary>X方向に反転するか</summary>
    public bool ReverseX { get; set; }
    /// <summary>Y方向に反転するか</summary>
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
    /// <summary>開始時点の基準拡大率</summary>
    public float StartBaseScale { get; set; }
    /// <summary>終了時点の基準拡大率</summary>
    public float EndBaseScale { get; set; }

    /// <summary>開始時点の縦横個別拡大率</summary>
    public SizeVector StartScale { get; set; } = new(1.0f, 1.0f);
    /// <summary>終了時点の縦横個別拡大率</summary>
    public SizeVector EndScale { get; set; } = new(1.0f, 1.0f);
}

/// <summary>
/// 音量フィルター（Filtersリストでボリューム変化を表現する場合に使用）
/// </summary>
internal class SoundFilter : Filter
{
    /// <summary>
    /// 開始ボリューム
    /// </summary>
    public float StartVolume { get; set; } = 1.0f;

    /// <summary>
    /// 終了ボリューム
    /// </summary>
    public float EndVolume { get; set; } = 1.0f;

    /// <summary>
    /// イージング
    /// </summary>
    public UfEasing Easing { get; set; } = UfEasing.Linear;
}
#endregion
#region Easing
/// <summary>
/// UfEasing の列挙値を実際の補間係数へ変換するユーティリティ。
/// </summary>
internal class AnimationEasing
{
    /// <summary>
    /// 進行度 <paramref name="t"/>（0〜1）を、指定イージングで補間した値に変換します。
    /// </summary>
    public static float Get(UfEasing easing, float t)
    {
        if (easing == UfEasing.Linear) return t;

        // UfEasing の並び（4個ごとにIn/Out/InOut/OutInの1セット）から
        // 対応する EEasing 種別と EInOut 方向を逆算する
        var ease = (EEasing)(((int)easing + 2) / 4);
        var inout = (EInOut)(((int)easing + 2) % 4);

        return (int)inout <= 2
            ? (float)Easing.Ease(t, 1, 0, 1, ease, inout, 8)
            // OutIn系は前半をOut・後半をInとして繋ぎ合わせる
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
