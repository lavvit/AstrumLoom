namespace AstrumLoom.Exo.Loaders;

/// <summary>
/// ファイルの拡張子から適切な <see cref="IAnimeLoader"/> を選ぶ。
/// </summary>
internal static class AnimeFormat
{
    public static IAnimeLoader CreateLoader(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".aup2" => new Aup2Loader(),
            // .exo に限らず、拡張子が不明な場合も従来通り exo として読む
            _ => new ExoLoader(),
        };
    }
}
