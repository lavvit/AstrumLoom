using Microsoft.Win32;

namespace AstrumLoom;

public readonly record struct FontSpec(
    string NameOrPath,
    int Size,
    int Edge = 0,
    bool Bold = false,
    bool Italic = false
);
public interface IFont : IDisposable
{
    bool Enable { get; }
    FontSpec Spec { get; }

    // テキストサイズ計測
    (int width, int height) Measure(string text);

    // そのフォントで描画
    void Draw(IGraphics g, double x, double y, string text,
        DrawOptions options);   // ★ 基準点・色・不透明度などをここで受ける
    void DrawEdge(IGraphics g, double x, double y, string text,
        DrawOptions options);  // ★ エッジのみ描画（必要なら実装）
}

public static class FontHandle
{
    private static IGraphics Graph => AstrumCore.Graphic;
    public static string SystemFont => GetSystemFontName();

    // フォント作成
    public static IFont? Create(FontSpec spec)
        => Graph?.CreateFont(spec) ?? null;
    public static IFont? Create(string nameOrPath, int size = 16, bool bold = false, bool italic = false, int edge = 0)
        => Create(new FontSpec(nameOrPath, size, edge, bold, italic));

    public static void Draw(this IFont? font, double x, double y, object text,
        Color? color = null,
        ReferencePoint point = ReferencePoint.TopLeft,
        Color? edgecolor = null,
        BlendMode blend = BlendMode.None,
        double opacity = 1)
        => (font ?? Drawing.DefaultFont).Draw(Graph, x, y, text?.ToString() ?? "",
            new DrawOptions
            {
                Color = color,
                Point = point,
                Blend = blend,
                Opacity = opacity,
                EdgeColor = edgecolor,
            });
    public static void DrawEdge(this IFont? font, double x, double y, object text,
        Color? edgecolor = null,
        ReferencePoint point = ReferencePoint.TopLeft,
        BlendMode blend = BlendMode.None,
        double opacity = 1)
        => (font ?? Drawing.DefaultFont).DrawEdge(Graph, x, y, text?.ToString() ?? "",
            new DrawOptions
            {
                EdgeColor = edgecolor,
                Point = point,
                Blend = blend,
                Opacity = opacity,
            });

    public static (int width, int height) Measure(this IFont? font, object text)
        => (font ?? Drawing.DefaultFont).Measure(text?.ToString() ?? "");

    private static string GetSystemFontName()
    {
        try
        {
            //if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            //    return SystemFonts.DefaultFont.FontFamily.Name;

            // Windows: 既存の SystemFonts 候補を優先
            if (OperatingSystem.IsWindows())
                return "Yu Gothic";
            // macOS
            if (OperatingSystem.IsMacOS())
                return "Helvetica";
            // iOS
            if (OperatingSystem.IsIOS())
                return "Helvetica";
            // Linux
            if (OperatingSystem.IsLinux())
                return "DejaVu Sans";
            // Android
            if (OperatingSystem.IsAndroid())
                return "Roboto";
        }
        catch
        {
            // 例外が発生した場合はフォールバックを返す
        }

        // 不明なプラットフォームまたはフォント取得失敗時の最終フォールバック
        return Environment.OSVersion.Platform == PlatformID.Win32NT ? "Segoe UI" : "Arial";
    }
}

/// <summary>フォントの“表示名”から TTF/OTF の実ファイルパスを解決する</summary>
public static class SystemFontResolver
{
    // Windows のレジストリに登録されているフォント名 → ファイル名の対応を引く
    // HKCU / HKLM の両方を見る。戻り値は優先候補の列挙（Bold/Italic考慮用）
    public static IEnumerable<(string displayName, string path)> EnumerateWindowsFonts()
    {
        if (!OperatingSystem.IsWindows())
            yield break;

        static IEnumerable<(string, string)> ReadKey(RegistryKey root)
        {
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
            if (key == null) yield break;
            foreach (string nameObj in key.GetValueNames())
            {
                string? val = key.GetValue(nameObj) as string;
                if (string.IsNullOrWhiteSpace(val)) continue;

                string displayName = nameObj; // 例: "MS UI Gothic (TrueType)"
                string file = val;            // 例: "msgothic.ttc" / "YuGothM.ttc" / "meiryo.ttc" など
                                              // 絶対パスでなければ Windows\Fonts を付ける
                if (!Path.IsPathFullyQualified(file))
                {
                    string fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                    file = Path.Combine(fontsDir, file);
                }
                yield return (displayName, file);
            }
        }

        foreach (var it in ReadKey(Registry.CurrentUser)) yield return it;
        foreach (var it in ReadKey(Registry.LocalMachine)) yield return it;
    }

    /// <summary>
    /// フォント名（例: "MS UI Gothic"）とスタイルヒントから最適なファイルを返す。
    /// 成功で絶対パス、失敗で null。
    /// </summary>
    public static string? Resolve(string familyOrFaceName, bool bold = false, bool italic = false)
    {
        if (string.IsNullOrWhiteSpace(familyOrFaceName)) return null;
        string q = familyOrFaceName.Trim().ToLowerInvariant();

        // 1) 名前マッチ（"(TrueType)"等は無視）
        var all = EnumerateWindowsFonts()
            .Where(t => File.Exists(t.path))
            .Select(t => (name: t.displayName.Replace("(TrueType)", "", StringComparison.OrdinalIgnoreCase)
                                        .Replace("(OpenType)", "", StringComparison.OrdinalIgnoreCase)
                                        .Trim(), t.path))
            .ToList();

        // Face名/Family名のどちらでも拾えるよう、部分一致も許容
        var cand = all.Where(t => t.name.Equals(q, StringComparison.OrdinalIgnoreCase)
                               || t.name.Contains(q, StringComparison.InvariantCultureIgnoreCase)).ToList();
        if (cand.Count == 0) return null;

        // 2) 拡張子絞り（TTF/OTF優先。TTCはRaylibで直接は非推奨）
        static int ExtScore(string p)
        {
            string ext = Path.GetExtension(p).ToLowerInvariant();
            return ext switch
            {
                ".ttf" => 3,
                ".otf" => 3,
                ".otc" => 2, // 互換微妙（避けたい）
                ".ttc" => 2, // 同上
                _ => 1
            };
        }

        // 3) スタイル（Bold/Italic）に寄せる
        static int StyleScore(string name, bool b, bool i)
        {
            name = name.ToLowerInvariant();
            int score = 0;
            if (b)
            {
                if (name.Contains("bold")) score += 2;
                if (name.Contains("demi") || name.Contains("medium")) score += 1;
            }
            if (i)
            {
                if (name.Contains("italic") || name.Contains("oblique")) score += 2;
            }
            // 標準体を好むときは minus をつけない（等価でOK）
            return score;
        }

        var chosen = cand
            .OrderByDescending(t => StyleScore(t.name, bold, italic))
            .ThenByDescending(t => ExtScore(t.path))
            .First();

        // TTC/OTCはサブフェイス選択が必要になることがあり、Raylibだと扱いづらいので注意喚起
        string extChosen = Path.GetExtension(chosen.path).ToLowerInvariant();
        if (extChosen is ".ttc" or ".otc")
        {
            // できれば TTF/OTF の別実体を使うのが吉
            // 見つからなければそのまま返す（ロードは呼び側で try-catch 推奨）
        }

        return chosen.path;
    }
}