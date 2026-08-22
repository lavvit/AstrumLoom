namespace AstrumLoom.Exo.Loaders;

/// <summary>
/// exo/aup2 の「合成モード=」の値を <see cref="AstrumLoom.BlendMode"/> に変換する共有ヘルパー。
/// </summary>
internal static class BlendModeMap
{
    public static BlendMode Parse(string value)
    {
        // 中間点付き（"通常,0"等）の可能性があるのでカンマ以前だけを見る
        string name = value.Split(',')[0];
        switch (name)
        {
            case "通常": return BlendMode.None;
            case "加算": return BlendMode.Add;
            case "減算": return BlendMode.Subtract;
            case "乗算": return BlendMode.Multiply;
            case "スクリーン": return BlendMode.Screen;
            default:
                Log.Warning($"未知の合成モードです: '{name}' -> Noneにフォールバックします");
                return BlendMode.None;
        }
    }
}
