namespace AstrumLoom.Exo.Loaders;

/// <summary>
/// exo/aup2 などのプロジェクトファイル形式を読み込んで <see cref="AnimeDocument"/> にするローダの共通インターフェース。
/// </summary>
internal interface IAnimeLoader
{
    /// <summary>ファイルを読み込み、中間表現を返す。</summary>
    AnimeDocument Load(string filePath);
}
