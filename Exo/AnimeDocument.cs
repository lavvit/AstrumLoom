namespace AstrumLoom.Exo;

/// <summary>
/// exo/aup2 のローダが返す中間表現。
/// ローダは「ファイルを読んでこの形にする」ところまでを担当し、
/// 画像オブジェクトとグループ制御の関連付けや再生・描画は <see cref="ExoAnimation"/> 側の責務にする。
/// </summary>
public class AnimeDocument
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Rate { get; set; }
    public int Scale { get; set; }
    /// <summary>プロジェクトの尺（フレーム数）。</summary>
    public int Length { get; set; }

    public List<ImageObject> ImageObjects { get; } = [];
    public List<GroupObject> GroupObjects { get; } = [];
    public List<SoundObject> SoundObjects { get; } = [];
}
