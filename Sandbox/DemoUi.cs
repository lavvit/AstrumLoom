using AstrumLoom;

namespace Sandbox;

/// <summary>
/// 見本帳シーン（TextureDemoScene / SoundDemoScene）で共有する小物。
///
/// 説明文が枠から溢れるのを防ぐため、折り返しは自前で行う。
/// Drawing.DefaultText は 16px 固定で、日本語だと 1 文字 16px 使うので、
/// 400px 幅のカードには 20 数文字しか入らない。説明を削るより小さい字で折り返した方が読める。
/// </summary>
internal static class DemoUi
{
    private static IFont? _note;

    /// <summary>
    /// 前フレームからの経過秒。演出は必ずこれで進めること。
    ///
    /// Sandbox の既定は TargetFps = 0（無制限）＋ UseMultiThreadUpdate = true なので、
    /// Update は実機で毎秒 10 万回近く回る。フレーム数で数える演出やクールダウンは
    /// そのぶんだけ速く進んでしまう。--selftest はロックステップ 60Hz に固定されるため、
    /// この違いはセルフテストでは絶対に見つからない（実機で撮った証跡でしか分からない）。
    /// </summary>
    public static double Delta
        => Math.Min(AstrumCore.Platform?.UTime.DeltaTime ?? (1.0 / 60.0), 0.05);

    /// <summary>説明文用の小さいフォント。生成はグラフィックス側を触るので、必ず Draw から呼ぶこと。</summary>
    public static IFont NoteFont
    {
        get
        {
            // FontHandle.Create は AstrumCore.Graphic に委譲する。描画スレッド（＝メインスレッド）
            // から初めて触れたときに 1 度だけ作り、以降は使い回す。
            // どれも作れなかったときは既定フォント（16px）で描く。読みにくいだけで落ちはしない。
            _note ??= FontHandle.Create("Yu Gothic UI", 13)
                   ?? FontHandle.Create("Meiryo UI", 13)
                   ?? FontHandle.Create(FontHandle.SystemFont, 13);
            return _note ?? Drawing.DefaultFont;
        }
    }

    public const double LineHeight = 17;

    /// <summary>
    /// 幅 maxW に収まるよう折り返しながら説明文を描く。段落ごとに 1 要素で渡す。
    /// 戻り値は「次に描き始められる y」。
    /// </summary>
    public static double Notes(double x, double y, double maxW, Color color, params string[] paragraphs)
    {
        var font = NoteFont;
        foreach (var para in paragraphs)
        {
            foreach (var line in Wrap(font, para, maxW))
            {
                font.Draw(x, y, line, color);
                y += LineHeight;
            }
        }
        return y;
    }

    /// <summary>1 行だけ描く（折り返しあり）。</summary>
    public static double Note(double x, double y, double maxW, string text, Color? color = null)
        => Notes(x, y, maxW, color ?? new Color(152, 170, 202), text);

    /// <summary>文字単位の素朴な折り返し。日本語はどこで切れても読めるので単語境界は見ない。</summary>
    private static List<string> Wrap(IFont font, string text, double maxW)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) { result.Add(""); return result; }

        int start = 0;
        int len = 1;
        while (start < text.Length)
        {
            // 入るところまで伸ばす。
            while (start + len <= text.Length &&
                   font.Measure(text.Substring(start, len)).width <= maxW)
            {
                len++;
            }
            len = Math.Max(1, len - 1);
            result.Add(text.Substring(start, Math.Min(len, text.Length - start)));
            start += len;
            len = 1;
        }
        return result;
    }

    /// <summary>枠つきのカード。中身は「左上を (0,0) とみなした座標」で描けるよう、本文の原点を渡す。</summary>
    public static void Card(double x, double y, double w, double h, string title, Action<double, double, double> body)
    {
        Drawing.Box(x, y, w, h, new Color(18, 24, 44, 214));
        Drawing.Box(x, y, w, h, new Color(70, 96, 150), thickness: 2);
        Drawing.Box(x, y, w, 26, new Color(38, 52, 92, 235));

        // 見出しも溢れうるので、入らなければ小さいフォントに落とす。
        var big = Drawing.DefaultFont;
        if (big.Measure(title).width <= w - 16)
            Drawing.Text(x + 8, y + 3, title, new Color(212, 226, 250));
        else
            NoteFont.Draw(x + 8, y + 6, title, new Color(212, 226, 250));

        body(x, y + 26, w);
    }
}
