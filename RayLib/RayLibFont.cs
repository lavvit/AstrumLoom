using System.Numerics;
using System.Runtime.InteropServices;

using Raylib_cs;

using static AstrumLoom.LayoutUtil;
using static AstrumLoom.RayLib.RayLibGraphics;
using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

/// <summary>
/// IFont の raylib 実装。TTF/OTF/TTC を stb_truetype 経由で読み込み、日本語の常用コードポイントに
/// 絞ったグリフアトラスを焼く。ふち取り・グラデーション・テクスチャ合成文字など通常のraylib APIには
/// 無い描画を、オフスクリーンレンダーテクスチャを介して実現する。
/// </summary>
internal sealed class RayLibFont : IFont
{
    public FontSpec Spec { get; }
    private readonly Font _font;
    /// <summary>フォントの読み込みに成功しているか（失敗時は既定フォントへフォールバック済みでも false のまま）。</summary>
    public bool Enable => _font.Texture.Id > 0;

    private int _edgeThickness = 0;
    private int _spacing = 0;
    /// <summary>
    /// 実際に raylib へ渡すピクセルサイズ。FontSpec.Size は「em の高さ（＝GDI/DxLib のフォントサイズ）」の意味だが、
    /// raylib(stb_truetype) の baseSize は「ascent-descent がその値になる高さ」なので、そのまま渡すと DxLib より
    /// 小さく描かれる。フォントの縦メトリクスから換算した値をここに持ち、焼き込み・計測・描画すべてで使う。
    /// </summary>
    private int _pixelSize;
    /// <summary>
    /// 1行あたりの送り高さ。DxLib 側は GDI の tmHeight（＝OS/2 の usWinAscent+usWinDescent）で行を送るので、
    /// こちらも同じ換算で持っておき、改行送りと Measure の高さに使う。em の高さより一回り大きい。
    /// </summary>
    private int _lineHeight;
    /// <summary>
    /// フォント指定からファイルを解決してグリフアトラスを焼きます。
    /// パス解決や読み込みに失敗した場合は、その旨をログに残したうえで raylib の内蔵フォントにフォールバックします。
    /// </summary>
    public RayLibFont(FontSpec spec)
    {
        Spec = spec;
        _pixelSize = spec.Size;
        _lineHeight = spec.Size;

        string path = GetFont(spec.NameOrPath, spec);
        if (string.IsNullOrEmpty(path))
        {
            // 何も見つからなかったらデフォルトフォント
            _font = GetFontDefault();
            return;
        }
        _edgeThickness = spec.Edge;
        _spacing = spec.Spacing;

        // Raylib: size は "baseSize" として渡す
        // 焼くグリフは ASCII + 日本語の常用範囲（CommonJpCodePoints）に絞る。
        // 0x20〜0xFFFF を丸ごと（65504個、サロゲート単体まで含む）焼こうとすると、
        // サイズ24でもアトラスが8192四方級になり、確保に失敗して豆腐落ちする。
        int[] cps = BuildCodePoints(spec.ExtraGlyphs);

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            // TTF/OTF どちらも stb_truetype 経由でOK
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".font" || ext is ".ttf" or ".otf" or ".ttc" or ".otc")
            {
                byte[] bytes = File.ReadAllBytes(path);
                string hint = ext == ".font" ? GuessFontHint(bytes) : ext;

                // 'ttcf'（TrueType/OpenType Collection）は raylib 内部の stb_truetype が
                // 常に offset 0 から解釈するため、生のバイト列をそのまま渡すと豆腐になる。
                // 拡張子ではなく中身のマジックで判定し、中の1本を単体sfntに組み直してから渡す。
                if (IsTtcHeader(bytes))
                {
                    byte[]? extracted = ExtractSubFontFromCollection(bytes, 0, out int chosen, out int total);
                    if (extracted == null)
                    {
                        Log.Warning($"font: '{path}' は TrueType/OpenType Collection ですが分解に失敗しました。内蔵フォントにフォールバックします。");
                        _font = Raylib.GetFontDefault();
                        return;
                    }
                    if (total > 1)
                        Log.Write($"font: '{path}' は {total} 本入りの Collection です。{chosen} 番目のサブフォントを使用します。");
                    bytes = extracted;
                    hint = ".ttf"; // 単体sfntに組み直したのでTTFとして渡す
                }

                _pixelSize = EmHeightToPixelSize(bytes, spec.Size);
                _lineHeight = EmHeightToLineHeight(bytes, spec.Size, _pixelSize);
                _font = Raylib.LoadFontFromMemory(hint, bytes, _pixelSize, cps, cps.Length);
                Raylib.SetTextureFilter(_font.Texture, TextureFilter.Bilinear);

                if (_font.Texture.Id <= 0)
                {
                    // LoadFontFromMemory 自体は例外を投げず、失敗すると黙って豆腐になる。
                    // ここで気づけるようにログへ残し、明示的に内蔵フォントへ切り替える。
                    Log.Warning($"font: '{path}' の読み込みに失敗しました（アトラスを確保できません）。内蔵フォントにフォールバックします。");
                    _font = Raylib.GetFontDefault();
                }
            }
            else // 未対応拡張子 → 内蔵にフォールバック
            {
                Log.Warning($"font: '{path}' は未対応の拡張子 '{ext}' です。内蔵フォントにフォールバックします。");
                _font = Raylib.GetFontDefault();
            }
        }
        else // パス無し → 内蔵フォント
        {
            Log.Warning($"font: 解決されたパス '{path}' が見つかりません。内蔵フォントにフォールバックします。");
            _font = Raylib.GetFontDefault();
        }
    }

    // 拡張子だけでは中身が分からない ".font" ファイル用に、先頭バイトからフォーマットを推測する。
    private static string GuessFontHint(byte[] b)
    {
        if (b.Length >= 4)
        {
            // 00 01 00 00 → TrueType
            if (b[0] == 0x00 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00) return ".ttf";
            // 'OTTO' → OpenType(CFF)
            if (b[0] == (byte)'O' && b[1] == (byte)'T' && b[2] == (byte)'T' && b[3] == (byte)'O') return ".otf";
            // 'ttcf' → TrueType/OpenType Collection
            if (IsTtcHeader(b)) return ".ttc";
        }
        return ".ttf"; // わからなければ TTF とみなす
    }

    private static bool IsTtcHeader(byte[] b)
        => b.Length >= 4 && b[0] == (byte)'t' && b[1] == (byte)'t' && b[2] == (byte)'c' && b[3] == (byte)'f';

    // .ttc/.otc（TrueType/OpenType Collection）から index 番目のサブフォントを取り出し、
    // 単体の sfnt としてメモリ上に組み直す。
    //
    // 構造（すべてビッグエンディアン）:
    //   'ttcf'(4) / version(4) / numFonts(4) / offset[numFonts](4バイトずつ)
    //   各offsetの先に sfntVersion(4) / numTables(2) / searchRange(2) / entrySelector(2) / rangeShift(2)
    //   そのあとに numTables 個のテーブルディレクトリ tag(4) / checkSum(4) / offset(4) / length(4)
    //   テーブル本体の offset はコレクション全体（ファイル先頭）からの絶対位置。
    //
    // raylib(stb_truetype) はコレクションのオフセット一覧を知らず常に offset 0 から読むので、
    // 選んだ1本のテーブルだけを新しい配置に詰め直し、独立した sfnt のバイト列として返す。
    // checkSum は再計算しない（stb_truetype 含め多くのパーサは検証しない値なので、
    // 位置がずれても実害はない。ここで正しさが要るのはテーブルディレクトリの offset/length だけ）。
    private static byte[]? ExtractSubFontFromCollection(byte[] data, int index, out int chosenIndex, out int totalFonts)
    {
        chosenIndex = index;
        totalFonts = 0;

        if (!IsTtcHeader(data) || data.Length < 12) return null;

        uint numFonts = ReadU32(data, 8);
        totalFonts = (int)numFonts;
        if (numFonts == 0) return null;
        if (index < 0 || index >= numFonts) index = 0; // 範囲外なら0番にフォールバック
        chosenIndex = index;

        int offsetPos = 12 + index * 4;
        if (offsetPos + 4 > data.Length) return null;
        long sfntOffset = ReadU32(data, offsetPos);
        if (sfntOffset + 12 > data.Length) return null;

        uint sfntVersion = ReadU32(data, (int)sfntOffset);
        int numTables = ReadU16(data, (int)sfntOffset + 4);
        if (numTables <= 0) return null;

        long dirPos = sfntOffset + 12;
        var tags = new uint[numTables];
        var checkSums = new uint[numTables];
        var srcOffsets = new long[numTables];
        var lengths = new uint[numTables];

        for (int i = 0; i < numTables; i++)
        {
            long p = dirPos + i * 16L;
            if (p + 16 > data.Length) return null;
            tags[i] = ReadU32(data, (int)p);
            checkSums[i] = ReadU32(data, (int)(p + 4));
            srcOffsets[i] = ReadU32(data, (int)(p + 8)); // ファイル先頭からの絶対位置
            lengths[i] = ReadU32(data, (int)(p + 12));
        }

        // 新しい配置: ヘッダ(12) + テーブルディレクトリ(numTables*16) の直後から、
        // 各テーブル本体を4バイト境界に揃えて詰めて並べる。
        const int newDirPos = 12;
        long cursor = newDirPos + numTables * 16L;
        var newOffsets = new long[numTables];
        for (int i = 0; i < numTables; i++)
        {
            newOffsets[i] = cursor;
            cursor += lengths[i];
            cursor = (cursor + 3) & ~3L; // 4バイト境界へ切り上げ
        }
        if (cursor > int.MaxValue) return null; // 現実的にはあり得ないサイズに対する保険

        var outBytes = new byte[cursor];
        WriteU32(outBytes, 0, sfntVersion);
        WriteU16(outBytes, 4, (ushort)numTables);

        // searchRange/entrySelector/rangeShift はテーブル数から機械的に決まる値。
        // stb_truetype 自体は線形探索でここを見ないが、仕様上正しい値を書いておく。
        int entrySelector = 0;
        while ((1 << (entrySelector + 1)) <= numTables) entrySelector++;
        int searchRange = (1 << entrySelector) * 16;
        WriteU16(outBytes, 6, (ushort)searchRange);
        WriteU16(outBytes, 8, (ushort)entrySelector);
        WriteU16(outBytes, 10, (ushort)(numTables * 16 - searchRange));

        for (int i = 0; i < numTables; i++)
        {
            long p = newDirPos + i * 16L;
            WriteU32(outBytes, (int)p, tags[i]);
            WriteU32(outBytes, (int)(p + 4), checkSums[i]);
            WriteU32(outBytes, (int)(p + 8), (uint)newOffsets[i]);
            WriteU32(outBytes, (int)(p + 12), lengths[i]);

            if (srcOffsets[i] + lengths[i] <= data.Length)
                Array.Copy(data, (int)srcOffsets[i], outBytes, (int)newOffsets[i], (int)lengths[i]);
            // 範囲外を指すテーブルは0埋めのまま残す（壊れたコレクション対策。
            // 致命的な破損なら呼び出し側の Texture.Id チェックで拾われる）
        }

        return outBytes;
    }

    // FontSpec.Size（em の高さ = GDI の lfHeight 相当。DxLib バックエンドはこの意味で使う）を、
    // raylib へ渡す baseSize に換算する。
    //
    // raylib の LoadFont* は内部で stbtt_ScaleForPixelHeight(baseSize) を呼ぶ。これは
    // 「hhea の ascent-descent が baseSize ピクセルになる」倍率で、em の高さではない。
    // 日本語フォントは ascent-descent が em の 1.3〜1.5 倍あるのが普通なので、Size をそのまま
    // 渡すと DxLib より 3 割ほど小さい文字になる。ここで (ascent-descent)/unitsPerEm を掛け戻すと、
    // em の高さがちょうど Size ピクセルになり、両バックエンドで同じ大きさに揃う。
    //
    // メトリクスを読めないフォント（壊れている・CFF で hhea が無い等）は Size をそのまま返す。
    // 見た目は今までどおり小さいままだが、豆腐や 0 サイズにはならない。
    private static int EmHeightToPixelSize(byte[] sfnt, int emSize)
    {
        if (emSize <= 0) return 1;

        int unitsPerEm = ReadUnitsPerEm(sfnt);
        if (unitsPerEm <= 0) return emSize;

        if (!TryReadHheaVMetrics(sfnt, out int ascent, out int descent)) return emSize;

        int span = ascent - descent; // descent は負の値
        if (span <= 0) return emSize;

        int px = (int)Math.Round(emSize * (double)span / unitsPerEm);
        return px > 0 ? px : emSize;
    }

    // 1行の送り高さ。GDI(DxLib) の tmHeight は TrueType では usWinAscent+usWinDescent なので、
    // それを em サイズ基準でピクセルに直す。OS/2 を読めないときは em の高さ（baseSize）で代用する。
    private static int EmHeightToLineHeight(byte[] sfnt, int emSize, int fallback)
    {
        if (emSize <= 0) return fallback;

        int unitsPerEm = ReadUnitsPerEm(sfnt);
        if (unitsPerEm <= 0) return fallback;

        long os2 = FindTable(sfnt, "OS/2");
        // OS/2: ... usWinAscent(2) usWinDescent(2) が先頭から 74/76 バイト目（version 0 でも存在する）
        if (os2 < 0 || os2 + 78 > sfnt.Length) return fallback;

        int winAscent = ReadU16(sfnt, (int)os2 + 74);
        int winDescent = ReadU16(sfnt, (int)os2 + 76);
        int span = winAscent + winDescent;
        if (span <= 0) return fallback;

        int px = (int)Math.Round(emSize * (double)span / unitsPerEm);
        return px > 0 ? px : fallback;
    }

    // sfnt のテーブルディレクトリから指定タグのテーブル位置を引く。見つからなければ -1。
    private static long FindTable(byte[] d, string tag)
    {
        if (d.Length < 12) return -1;
        uint want = ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];
        int numTables = ReadU16(d, 4);
        for (int i = 0; i < numTables; i++)
        {
            long p = 12 + i * 16L;
            if (p + 16 > d.Length) return -1;
            if (ReadU32(d, (int)p) == want) return ReadU32(d, (int)(p + 8));
        }
        return -1;
    }

    private static int ReadUnitsPerEm(byte[] d)
    {
        long head = FindTable(d, "head");
        // head: version(4) fontRevision(4) checkSumAdjustment(4) magicNumber(4) flags(2) unitsPerEm(2)
        if (head < 0 || head + 20 > d.Length) return 0;
        return ReadU16(d, (int)head + 18);
    }

    private static bool TryReadHheaVMetrics(byte[] d, out int ascent, out int descent)
    {
        ascent = descent = 0;
        long hhea = FindTable(d, "hhea");
        // hhea: version(4) ascender(2, int16) descender(2, int16) lineGap(2)
        if (hhea < 0 || hhea + 8 > d.Length) return false;
        ascent = ReadS16(d, (int)hhea + 4);
        descent = ReadS16(d, (int)hhea + 6);
        return true;
    }

    private static int ReadS16(byte[] b, int o) => (short)((b[o] << 8) | b[o + 1]);

    // DrawTextEx / MeasureTextEx の改行送りは raylib のグローバル設定（既定 15px 固定）で、
    // フォントサイズに追従しない。DxLib 側は行高＝フォントの高さで送っているので、
    // 描画・計測の直前に毎回そろえておく。
    private void ApplyLineSpacing() => Raylib.SetTextLineSpacing(_lineHeight);

    private static uint ReadU32(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
    private static int ReadU16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
    private static void WriteU32(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }
    private static void WriteU16(byte[] b, int o, ushort v)
    {
        b[o] = (byte)(v >> 8); b[o + 1] = (byte)v;
    }

    /// <summary>指定テキストをこのフォントで描画したときの幅・高さを計測します。</summary>
    public (int width, int height) Measure(string text)
    {
        ApplyLineSpacing();
        var size = MeasureTextEx(_font, text, _pixelSize, _spacing);
        float width = size.X;
        if (width <= 0) width = MeasureText(text, _pixelSize);

        // 高さは MeasureTextEx の戻り（1行目だけ baseSize、以降は行送り）ではなく行数×行高で出す。
        // DxLib の GetDrawStringSizeToHandle が「行数×tmHeight」を返すのに合わせるため。
        int lines = 1;
        foreach (char ch in text) if (ch == '\n') lines++;
        return ((int)width, lines * _lineHeight);
    }
    private const float sin45 = 0.70710678f;
    private static readonly Vector2[] EdgeDirs =
[
    new( 1,  0),
    new(-1,  0),
    new( 0,  1),
    new( 0, -1),
    new( sin45,  sin45),
    new(-sin45,  sin45),
    new( sin45, -sin45),
    new(-sin45, -sin45),
];
    /// <summary>テキストを描画します。EdgeThicknessが設定されていれば、本体の下に8方向へずらしたふちを先に描きます。</summary>
    public void Draw(double x, double y, string text, DrawOptions options)
    {
        if (!Enable)
        {
            Drawing.DefaultText(x, y, text);
            return;
        }
        SetOptions(options);

        int drawX = (int)x;
        int drawY = (int)y;
        if (options.Point != ReferencePoint.TopLeft)
        {
            var (w, h) = Measure(text);
            var off = GetAnchorOffset(options.Point, w, h);
            drawX = (int)(x + off.X);
            drawY = (int)(y + off.Y);
        }

        var color = options.Color ?? Color.White;
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var pos = new Point(drawX, drawY);

        // Edge（ふち）: オフセット描画
        if (_edgeThickness > 0)
        {
            // 8方向の単位ベクトル（円形に均一配置）
            int r = _edgeThickness;
            for (r = 1; r <= _edgeThickness; r++)
            {
                foreach (var v in EdgeDirs)
                {
                    // 端数でにじまないように整数へ
                    var p = new Point(MathF.Round((float)pos.X + v.X * r),
                    MathF.Round((float)pos.Y + v.Y * r));
                    DrawEx(text, p, options.EdgeColor ?? Color.VisibleColor(color), opacity);
                }
            }
        }
        DrawEx(text, pos, color, opacity);
        ResetOptions(options);
    }

    private void DrawEx(string s, Point pos, Color color, double opacity = 1, int spacing = 0)
    {
        var p = new Vector2((float)pos.X, (float)pos.Y);
        var c = ToRayColor(color, color.A / 255.0 * opacity);
        ApplyLineSpacing();
        DrawTextEx(_font, s, p,
                   _pixelSize, _spacing + spacing, c);
    }

    /// <summary>ふちだけを単独で描画します（Draw()内から本体描画の前に呼ばれるほか、DrawGrad/DrawTextureからも共用されます）。</summary>
    public void DrawEdge(double x, double y, string text, DrawOptions options)
    {
        if (!Enable || _edgeThickness <= 0)
        {
            return;
        }
        SetOptions(options);

        int drawX = (int)x;
        int drawY = (int)y;
        if (options.Point != ReferencePoint.TopLeft)
        {
            var (w, h) = Measure(text);
            var off = GetAnchorOffset(options.Point, w, h);
            drawX = (int)(x + off.X);
            drawY = (int)(y + off.Y);
        }
        var ec = options.EdgeColor ?? options.Color ?? Color.Black;
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var pos = new Point(drawX, drawY);
        // Edge（ふち）: オフセット描画
        int r = _edgeThickness;
        for (r = 1; r <= _edgeThickness; r++)
        {
            foreach (var v in EdgeDirs)
            {
                // 端数でにじまないように整数へ
                var p = new Point(
                    MathF.Round((float)pos.X + v.X * r),
                    MathF.Round((float)pos.Y + v.Y * r));
                DrawEx(text, p, ec, opacity);
            }
        }
        ResetOptions(options);
    }
    /// <summary>フォントアトラスと、キャッシュ済みのレンダーテクスチャ一式を解放します。</summary>
    public void Dispose()
    {
        UnloadFont(_font);

        lock (_texcacheLock)
        {
            foreach (var rt in _texcache)
            {
                try { UnloadRenderTexture(rt); } catch { }
            }
            _texcache.Clear();
        }
    }

    /// <summary>フォント名/パス指定からフォントファイルの実パスを解決します（既存ファイルパスならそのまま、名前ならシステムフォント検索）。</summary>
    private static string GetFont(string? font, FontSpec spec)
    {
        if (string.IsNullOrEmpty(font)) return "";
        if (File.Exists(font)) return font;

        string? path = SystemFontResolver.Resolve(
                    spec.NameOrPath, spec.Bold, spec.Italic);
        if (string.IsNullOrEmpty(path))
            Log.Warning($"font: {spec.NameOrPath} is not found.");
        return path ?? "";
    }
    // 焼くグリフの既定集合 + FontSpec.ExtraGlyphs で追加指定された文字を合成する。
    private static int[] BuildCodePoints(string? extra)
    {
        int[] baseSet = CommonJpCodePoints();
        if (string.IsNullOrEmpty(extra)) return baseSet;

        // サロゲートペア（絵文字など）も1文字として正しく拾うため EnumerateRunes を使う。
        var extraCps = extra.EnumerateRunes().Select(r => r.Value);
        return [.. baseSet.Concat(extraCps).Distinct()];
    }

    // よく使うコードポイント（ASCII + ひらがな + カタカナ + 全角記号 + JIS第一水準漢字あたり）。
    // 昔は CJK統合漢字ブロック(0x4E00-0x9FFF, 20992字)を丸ごと足していたが、
    // サイズ24でもアトラスが8192四方級に膨らんで確保できなくなるため、
    // 実用上ほぼ足りるJIS第一水準漢字(約2965字)相当まで絞ってある。
    private static int[] CommonJpCodePoints()
    {
        // ASCII
        int[] ascii = EnumRange(0x20, 0x7E);

        // ひらがな・カタカナ
        int[] hira = EnumRange(0x3040, 0x309F);
        int[] kata = EnumRange(0x30A0, 0x30FF);

        // CJK 記号・句読点／全角記号など
        int[] cjkSym = EnumRange(0x3000, 0x303F);   // 、。・〜（　）他
        int[] fullwd = EnumRange(0xFF00, 0xFFEF);   // 全角英数・半角カナなど

        // JIS第一水準漢字あたり
        int[] kanji = GetJisLevel1Kanji();

        return [.. ascii.Concat(hira).Concat(kata).Concat(cjkSym).Concat(fullwd).Concat(kanji).Distinct()];
    }
    private static int[] EnumRange(int start, int end) => [.. Enumerable.Range(start, end - start + 1)];

    // JIS第一水準漢字のコードポイント一覧（初回だけ計算してキャッシュする）。
    //
    // 区点⇔Shift_JISの変換式を自前で書くとオフバイワンを踏みやすいので、
    // Windows自身の変換テーブル（MultiByteToWideChar / CP932）にそのまま引かせる。
    // Shift_JISでは第一水準漢字が 0x889F〜0x9872 の範囲に連続して並んでいることが
    // 知られているので、その範囲の2バイト値を1つずつ変換し、実在する字だけを拾えば、
    // 区点計算をせずにJIS第一水準相当のセットが手に入る。
    private static int[]? _jisLevel1Cache;
    private static readonly object _jisLevel1Lock = new();
    private static int[] GetJisLevel1Kanji()
    {
        if (_jisLevel1Cache != null) return _jisLevel1Cache;
        lock (_jisLevel1Lock)
        {
            // Windows以外（あるいはCP932テーブルが無い環境）では黙って空集合にする。
            // その場合ASCII/かな/記号は焼けるので、漢字だけ豆腐になる形で緩やかに劣化する。
            _jisLevel1Cache ??= OperatingSystem.IsWindows() ? [.. EnumerateJisLevel1Kanji()] : [];
            return _jisLevel1Cache;
        }
    }

    private static IEnumerable<int> EnumerateJisLevel1Kanji()
    {
        var bytes = new byte[2];
        var chars = new char[2];
        for (int code = 0x889F; code <= 0x9872; code++)
        {
            byte trail = (byte)(code & 0xFF);
            if (trail < 0x40 || trail == 0x7F || trail > 0xFC) continue; // Shift_JIS後続バイトとして無効な値

            bytes[0] = (byte)(code >> 8);
            bytes[1] = trail;
            int n = MultiByteToWideChar(CpShiftJis, MbErrInvalidChars, bytes, 2, chars, chars.Length);
            if (n <= 0) continue; // その区点には字が割り当てられていない

            int cp = chars[0];
            if (cp is >= 0x4E00 and <= 0x9FFF) // 念のため漢字ブロック以外（記号の混入）は弾く
                yield return cp;
        }
    }

    // CharSet.Unicode を明示しないと lpWideCharStr(char[]) が既定のANSI規則で
    // マーシャリングされて中身が化ける（呼び出し自体は成功扱いになるので気づきにくい）。
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int MultiByteToWideChar(uint codePage, uint dwFlags, byte[] lpMultiByteStr, int cbMultiByte, char[] lpWideCharStr, int cchWideChar);

    private const uint CpShiftJis = 932;
    private const uint MbErrInvalidChars = 0x00000008;

    #region Gradation Text
    /// <summary>
    /// 行ごとに色が変化するグラデーション文字を描画します。raylibには直接の機能が無いため、
    /// 一旦白文字をオフスクリーンに焼いてマスクとして使い、行単位でグラデーション色を乗せて画面に転写します。
    /// </summary>
    public void DrawGrad(double x, double y, string text, Gradation gradation, DrawOptions options)
    {

        if (!Enable) { Drawing.DefaultText(x, y, text); return; }

        // 4. ふちが欲しければ、options.EdgeColor を使って別途 DrawEdge みたいに描画
        DrawEdge(x, y, text, options);

        var (w, h) = Measure(text);
        if (w <= 0 || h <= 0) return;

        var rt = AcquireRenderTexture(w, h);
        try
        {
            // 1. オフスクリーンに白文字を描画
            Raylib.BeginTextureMode(rt);
            Raylib.ClearBackground(new Raylib_cs.Color(0, 0, 0, 0));
            DrawEx(text, new(0, 0), Color.White, 1.0);
            Raylib.EndTextureMode();
            if (RayLibTexture.RenderTexture2D.Id != 0)
                Raylib.BeginTextureMode(RayLibTexture.RenderTexture2D);

            // 2. 基準点を考慮
            var off = LayoutUtil.GetAnchorOffset(options.Point, w, h);
            float x1 = (float)(x + off.X);
            float y1 = (float)(y + off.Y);

            // 3. 行ごとにグラデーション
            for (int row = 0; row < h; row++)
            {
                float t = h > 1 ? (float)row / (h - 1) : 0f;
                var c = gradation.GetColor(1 - t, gradation.UseColorSpace);
                var tint = ToRayColor(c, options.Opacity);

                var src = new Rectangle(0, row, w, 1);
                float dy = y1 + h - row - 1;
                var dest = new Vector2(x1, dy);
                Raylib.DrawTextureRec(rt.Texture, src, dest, tint);
            }
        }
        catch
        {
            // 失敗したら何もしない
            Log.Error("DrawGrad: failed to draw gradation text.");
        }
        finally
        {
            ReleaseRenderTexture(rt);
        }
    }

    // RenderTexture2D プール (キャッシュ)
    private readonly List<RenderTexture2D> _texcache = [];
    private readonly object _texcacheLock = new();
    private const int MaxTexCache = 16;
    /// <summary>指定サイズのレンダーテクスチャをキャッシュから探し、無ければ新規作成して取得します。</summary>
    private RenderTexture2D AcquireRenderTexture(int width, int height)
    {
        lock (_texcacheLock)
        {
            for (int i = _texcache.Count - 1; i >= 0; i--)
            {
                var rt = _texcache[i];
                try
                {
                    // サイズ一致で有効なものを返す
                    if (rt.Texture.Width == width && rt.Texture.Height == height && rt.Texture.Id != 0)
                    {
                        _texcache.RemoveAt(i);
                        return rt;
                    }
                }
                catch
                {
                    // 何か不正なら破棄
                    try { Raylib.UnloadRenderTexture(rt); } catch { }
                    _texcache.RemoveAt(i);
                }
            }
        }
        // 見つからなければ新規作成
        return Raylib.LoadRenderTexture(width, height);
    }
    /// <summary>使い終えたレンダーテクスチャをキャッシュへ返却します。キャッシュが上限に達していれば解放します。</summary>
    private void ReleaseRenderTexture(RenderTexture2D rtex)
    {
        if (rtex.Texture.Id == 0)
        {
            try { Raylib.UnloadRenderTexture(rtex); } catch { }
            return;
        }

        lock (_texcacheLock)
        {
            if (_texcache.Count >= MaxTexCache)
            {
                // 多すぎる場合は解放
                try { Raylib.UnloadRenderTexture(rtex); } catch { }
            }
            else
            {
                _texcache.Add(rtex);
            }
        }
    }
    #endregion

    #region Texture Text
    /// <summary>
    /// 文字の形をマスクにして、指定テクスチャを乗算合成した「模様入り文字」を描画します。
    /// 白文字をオフスクリーンに焼いた後、乗算ブレンドでテクスチャを重ねてからマスク済みの結果を画面へ転写します。
    /// </summary>
    public void DrawTexture(double x, double y, string text, ITexture[] textures, DrawOptions options)
    {
        if (!Enable)
        {
            Drawing.DefaultText(x, y, text);
            return;
        }
        SetOptions(options);

        // 4. ふちが欲しければ、options.EdgeColor を使って別途 DrawEdge みたいに描画
        DrawEdge(x, y, text, options);

        var (w, h) = Measure(text);
        if (w <= 0 || h <= 0)
        {
            ResetOptions(options);
            return;
        }

        int drawX = (int)x;
        int drawY = (int)y;
        if (options.Point != ReferencePoint.TopLeft)
        {
            var off = GetAnchorOffset(options.Point, w, h);
            drawX = (int)(x + off.X);
            drawY = (int)(y + off.Y);
        }

        var rt = AcquireRenderTexture(w, h);
        try
        {
            // 1. マスク作成（真っ白文字）
            BeginTextureMode(rt);
            ClearBackground(new Raylib_cs.Color(0, 0, 0, 0));
            DrawEx(text, new(0, 0), Color.White, 1.0);
            EndTextureMode();

            // 2. マスクの上にテクスチャを乗算で塗る
            BeginTextureMode(rt);

            // 文字の範囲いっぱいにテクスチャを敷く（タイリングしたければ for 文で）
            BeginBlendMode(Raylib_cs.BlendMode.Multiplied);
            foreach (var texture in textures)
            {
                var src = new Rectangle(0, 0, texture.Width, texture.Height);
                var dst = new Rectangle(0, 0, w, h);
                var tex = (texture as RayLibTexture)?.Native ?? default;
                DrawTexturePro(tex, src, dst, Vector2.Zero, 0f, Raylib_cs.Color.White);
            }
            EndBlendMode();

            EndTextureMode();

            // 3. 出来上がったものを画面に貼る
            double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
            var tint = ToRayColor(options.Color ?? Color.White, opacity);

            var fullSrc = new Rectangle(0, 0, rt.Texture.Width, -rt.Texture.Height);
            var destPos = new Vector2(drawX, drawY);

            DrawTextureRec(rt.Texture, fullSrc, destPos, tint);
        }
        finally
        {
            ReleaseRenderTexture(rt);
            ResetOptions(options);
        }
    }
    #endregion
}
