using System.Security.Cryptography;
using System.Text;

using Raylib_cs;

namespace AstrumLoom.RayLib;

/// <summary>
/// 焼き上がったグリフアトラス（テクスチャの元画像＋字ごとの位置と送り幅）を
/// <c>&lt;AppPath&gt;\.fontcache\&lt;hash&gt;.atlas</c> へ保存し、2 回目以降の起動では焼かずに読み込むキャッシュ層。
/// </summary>
/// <remarks>
/// 焼く処理（stb_truetype によるラスタライズ＋詰め込み）は 1 本あたり 60〜75 ms かかり、
/// raylib バックエンドの起動時間で最も大きな「こちらで削れる」部分だった。
/// 出来上がりは同じフォント・同じ大きさ・同じ字の集合なら毎回同じなので、焼いた結果を置いておける。
/// 考え方は <see cref="AstrumLoom.Audio.AudioCache"/>（合成した音を WAV に焼く）と同じ。
///
/// ディレクトリを作れない・読めない・壊れている場合は黙って「キャッシュ無し」として扱い、
/// その場で焼き直す。キャッシュが理由でゲームが落ちることはない。
/// </remarks>
internal static unsafe class FontAtlasCache
{
    /// <summary>ファイル形式の版。作りを変えたら上げる（古いファイルは読まれずに捨てられる）。</summary>
    private const int FormatVersion = 1;
    private const uint Magic = 0x41464C41; // 'ALFA'

    /// <summary>raylib が LoadFontEx で使う既定値に合わせる。ここを変えると見た目が変わる。</summary>
    public const int GlyphPadding = 4;
    /// <summary>アトラスの詰め込み方。0 = skyline（raylib の既定）。</summary>
    public const int PackMethod = 0;

    private static string? _cacheDir;
    private static bool _dirResolutionFailed;

    /// <summary>
    /// キャッシュの鍵。フォントの中身・大きさ・焼く字の集合・詰め方のどれかが変われば別の鍵になります。
    /// </summary>
    /// <remarks>
    /// フォントファイルの更新時刻と長さを混ぜてあるので、Windows Update でフォントが差し替わっても
    /// 古いアトラスを掴み続けることはありません。
    /// </remarks>
    public static string KeyFor(string path, int pixelSize, int[] codePoints)
    {
        var sb = new StringBuilder();
        sb.Append("v").Append(FormatVersion).Append('|');
        sb.Append(path).Append('|');
        try
        {
            var info = new FileInfo(path);
            sb.Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('|');
        }
        catch
        {
            // 情報が取れないなら、その旨を鍵に混ぜておく（取れた場合と衝突させない）。
            sb.Append("nostat|");
        }
        sb.Append(pixelSize).Append('|').Append(GlyphPadding).Append('|').Append(PackMethod).Append('|');
        sb.Append(codePoints.Length).Append(':');
        // 字の集合は順番も含めて同一でなければならない（Recs の並びがこの順に対応するため）。
        foreach (int cp in codePoints) sb.Append(cp).Append(',');

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        var hex = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash) hex.Append(b.ToString("x2"));
        return hex.ToString();
    }

    /// <summary>キャッシュからフォントを組み立てます。無い・壊れている・読めないときは false。</summary>
    public static bool TryLoad(string key, out Font font)
    {
        font = default;
        string? dir = EnsureDir();
        if (dir == null) return false;

        string path = Path.Combine(dir, key + ".atlas");
        if (!File.Exists(path)) return false;

        byte[] blob;
        try
        {
            using (BootTimer.Measure("アトラスをキャッシュから読む"))
                blob = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            Log.Warning($"font: アトラスのキャッシュを読めませんでした（焼き直します）: {ex.Message}");
            return false;
        }

        try
        {
            return Deserialize(blob, out font);
        }
        catch (Exception ex)
        {
            // 壊れたファイルを掴んだら、次回以降そこで止まらないよう消してから焼き直しへ回す。
            Log.Warning($"font: アトラスのキャッシュが壊れていました（作り直します）: {ex.Message}");
            TryDelete(path);
            return false;
        }
    }

    /// <summary>焼き上がったアトラスを保存します。失敗しても呼び出し側には影響しません。</summary>
    public static void Save(string key, Font font, Image atlas)
    {
        string? dir = EnsureDir();
        if (dir == null) return;
        if (!BytesPerPixel(atlas.Format, out int bpp)) return; // 知らない形式は保存しない

        try
        {
            byte[] blob = Serialize(font, atlas, bpp);
            string path = Path.Combine(dir, key + ".atlas");
            // 途中で落ちても中途半端なファイルを掴まないよう、一時ファイル経由で置き換える。
            string tmp = path + $".tmp{Environment.CurrentManagedThreadId}_{Environment.TickCount64}";
            File.WriteAllBytes(tmp, blob);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warning($"font: アトラスのキャッシュを保存できませんでした（次回も焼きます）: {ex.Message}");
        }
    }

    // --- 書き出し / 読み込み -------------------------------------------------

    private static byte[] Serialize(Font font, Image atlas, int bpp)
    {
        int count = font.GlyphCount;
        int pixelBytes = atlas.Width * atlas.Height * bpp;

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write(font.BaseSize);
            w.Write(font.GlyphPadding);
            w.Write(count);
            w.Write(atlas.Width);
            w.Write(atlas.Height);
            w.Write((int)atlas.Format);
            w.Write(pixelBytes);

            for (int i = 0; i < count; i++)
            {
                GlyphInfo g = font.Glyphs[i];
                w.Write(g.Value);
                w.Write(g.OffsetX);
                w.Write(g.OffsetY);
                w.Write(g.AdvanceX);
            }
            for (int i = 0; i < count; i++)
            {
                Rectangle r = font.Recs[i];
                w.Write(r.X); w.Write(r.Y); w.Write(r.Width); w.Write(r.Height);
            }
            w.Write(new ReadOnlySpan<byte>(atlas.Data, pixelBytes));
        }
        return ms.ToArray();
    }

    private static bool Deserialize(byte[] blob, out Font font)
    {
        font = default;

        using var ms = new MemoryStream(blob, writable: false);
        using var r = new BinaryReader(ms, Encoding.UTF8);

        if (blob.Length < 36) return false;
        if (r.ReadUInt32() != Magic) return false;
        if (r.ReadInt32() != FormatVersion) return false;

        int baseSize = r.ReadInt32();
        int padding = r.ReadInt32();
        int count = r.ReadInt32();
        int width = r.ReadInt32();
        int height = r.ReadInt32();
        var format = (PixelFormat)r.ReadInt32();
        int pixelBytes = r.ReadInt32();

        if (count <= 0 || width <= 0 || height <= 0 || pixelBytes <= 0) return false;
        if (!BytesPerPixel(format, out int bpp) || width * height * bpp != pixelBytes) return false;
        // ヘッダの数字とファイルの実長が合わなければ、途中で切れているか別物。
        long need = ms.Position + (long)count * 16 + (long)count * 16 + pixelBytes;
        if (need != blob.Length) return false;

        // raylib 側の確保器で取る。こうしておけば UnloadFont がそのまま解放できる
        // （Marshal で取ると解放器が食い違ってヒープを壊す）。
        var glyphs = (GlyphInfo*)Raylib.MemAlloc((uint)(count * sizeof(GlyphInfo)));
        var recs = (Rectangle*)Raylib.MemAlloc((uint)(count * sizeof(Rectangle)));
        if (glyphs == null || recs == null)
        {
            if (glyphs != null) Raylib.MemFree(glyphs);
            if (recs != null) Raylib.MemFree(recs);
            return false;
        }

        // 字ごとの小さな画像（GlyphInfo.Image）は CPU 側へ文字を描くとき専用で、
        // 画面への描画（DrawTextEx）はアトラスのテクスチャしか見ない。ここでは持たず、
        // 0 のままにしておく。UnloadFont はその null をそのまま解放するので問題ない。
        new Span<byte>(glyphs, count * sizeof(GlyphInfo)).Clear();

        for (int i = 0; i < count; i++)
        {
            glyphs[i].Value = r.ReadInt32();
            glyphs[i].OffsetX = r.ReadInt32();
            glyphs[i].OffsetY = r.ReadInt32();
            glyphs[i].AdvanceX = r.ReadInt32();
        }
        for (int i = 0; i < count; i++)
        {
            recs[i].X = r.ReadSingle();
            recs[i].Y = r.ReadSingle();
            recs[i].Width = r.ReadSingle();
            recs[i].Height = r.ReadSingle();
        }

        int pixelOffset = (int)ms.Position;
        Texture2D texture;
        fixed (byte* pixels = &blob[pixelOffset])
        {
            var image = new Image
            {
                Data = pixels,
                Width = width,
                Height = height,
                Mipmaps = 1,
                Format = format,
            };
            // GPU へ送る間だけ画素を指していればよい（送った後は中で複製されている）。
            using (BootTimer.Measure("アトラスを GPU へ載せる"))
                texture = Raylib.LoadTextureFromImage(image);
        }

        if (texture.Id <= 0)
        {
            Raylib.MemFree(glyphs);
            Raylib.MemFree(recs);
            return false;
        }

        font = new Font
        {
            BaseSize = baseSize,
            GlyphCount = count,
            GlyphPadding = padding,
            Texture = texture,
            Recs = recs,
            Glyphs = glyphs,
        };
        return true;
    }

    private static bool BytesPerPixel(PixelFormat format, out int bpp)
    {
        // アトラスは GenImageFontAtlas が作るので実際には GrayAlpha だが、
        // 将来 raylib 側が変えても壊れないよう、扱える形式だけを明示的に許す。
        bpp = format switch
        {
            PixelFormat.UncompressedGrayscale => 1,
            PixelFormat.UncompressedGrayAlpha => 2,
            PixelFormat.UncompressedR8G8B8 => 3,
            PixelFormat.UncompressedR8G8B8A8 => 4,
            _ => 0,
        };
        return bpp > 0;
    }

    // --- 置き場所 -----------------------------------------------------------

    private static string? EnsureDir()
    {
        if (_cacheDir != null) return _cacheDir;
        if (_dirResolutionFailed) return null;

        try
        {
            string dir = Path.Combine(AstrumCore.AppPath, ".fontcache");
            Directory.CreateDirectory(dir);
            _cacheDir = dir;
            return dir;
        }
        catch (Exception ex1)
        {
            Log.Warning($"font: キャッシュ置き場を作れませんでした（AppPath 配下）。一時ディレクトリへ逃がします: {ex1.Message}");
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "AstrumLoom_FontCache");
                Directory.CreateDirectory(dir);
                _cacheDir = dir;
                return dir;
            }
            catch (Exception ex2)
            {
                _dirResolutionFailed = true;
                Log.Warning($"font: キャッシュ置き場を一時ディレクトリにも作れませんでした。以後は毎回焼きます: {ex2.Message}");
                return null;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* 消せなくても焼き直せるので困らない */ }
    }
}
