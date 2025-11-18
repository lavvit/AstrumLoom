using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace AstrumLoom;

public class Text
{
    public static List<string> Read(string path, bool allsplit = true, bool removeempty = true)
    {
        List<string> list = [];
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;

        using (var sr = new StreamReader(path, GetEncoding(path)))
        {
            string[] split = allsplit ? ["\r\n", "\r", "\n"] : ["\r\n"];
            string read = "";
            try
            {
                read = sr.ReadToEnd();

            }
            catch (Exception)
            {
                using StreamReader sr2 = new(path, Encoding.GetEncoding("shift_jis"));
                read = sr2.ReadToEnd();
            }
            string[] rd = read.Split(split, removeempty ? StringSplitOptions.RemoveEmptyEntries : StringSplitOptions.None);
            list.AddRange(rd);
        }
        return list;
    }
    public static void Save(string path, List<string> list, string encode = "utf-8", bool append = false) =>
        Save(list, path, encode, append);
    public static void Save(List<string> list, string path, string encode = "utf-8", bool append = false)
    {
        // エンコーディングの取得（例外は呼び出し元で処理）
        var encoding = Encoding.GetEncoding(encode);

        // append == true なら追記、false なら上書き（Create）にする
        var mode = append ? System.IO.FileMode.Append : System.IO.FileMode.Create;

        // directory が存在しない場合は作成する
        string? directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            System.IO.Directory.CreateDirectory(directory);

        // FileStream を使うことで明示的にファイルモードを制御する（上書きを確実にする）
        using var fs = new System.IO.FileStream(path, mode, System.IO.FileAccess.Write, System.IO.FileShare.Read);
        using var sw = new System.IO.StreamWriter(fs, encoding);
        int last = list.LastIndexOf(list.LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? "");
        for (int i = 0; i <= last; i++)
            sw.WriteLine(list[i]);
    }

    /// <summary>
    /// エンコードを読み込みます。
    /// </summary>
    /// <param name="path">ファイル名</param>
    public static Encoding GetEncoding(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // memo: Shift-JISを扱うためのおまじない
        var encode = GetJpEncoding(path);
        encode ??= Encoding.GetEncoding("shift_jis");
        return encode;
    }
    #region Encoding
    public static Encoding? GetJpEncoding(string file, long maxSize = 50 * 1024)//ファイルパス、最大読み取りバイト数
    {
        try
        {
            if (!File.Exists(file))//ファイルが存在しない場合
            {
                return null;
            }
            else if (new FileInfo(file).Length == 0)//ファイルサイズが0の場合
            {
                return null;
            }
            else//ファイルが存在しファイルサイズが0でない場合
            {
                //バイナリ読み込み
                byte[]? bytes = null;
                bool readAll = false;
                using (FileStream fs = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long size = fs.Length;

                    if (size <= maxSize)
                    {
                        bytes = new byte[size];
                        fs.ReadExactly(bytes, 0, (int)size);
                        readAll = true;
                    }
                    else
                    {
                        bytes = new byte[maxSize];
                        fs.ReadExactly(bytes, 0, (int)maxSize);
                    }
                }

                //判定
                return GetJpEncoding(bytes, readAll);
            }
        }
        catch
        {
            return null;
        }
    }
    public static Encoding? GetJpEncoding(byte[] bytes, bool readAll = false)
    {
        //BOM判定
        var enc = checkBOM(bytes);
        if (enc != null) return enc;

        //簡易ISO-2022-JP判定
        if (checkISO_2022_JP(bytes)) return Encoding.GetEncoding(50220);//iso-2022-jp

        //簡易文字コード判定(再変換確認)
        var enc_sjis = Encoding.GetEncoding(932);//ShiftJIS
        var enc_euc = Encoding.GetEncoding(51932);//EUC-JP
        Encoding enc_utf8_check = new UTF8Encoding(false, false);//utf8
        Encoding enc_utf8 = new UTF8Encoding(false, true);//utf8

        int sjis = checkReconversion(bytes, enc_sjis);
        int euc = checkReconversion(bytes, enc_euc);
        int utf8 = checkReconversion(bytes, enc_utf8_check);

        //末尾以外は同一の場合は同一とみなす
        if (utf8 >= bytes.Length - 3 && !readAll) utf8 = -1;
        if (sjis >= bytes.Length - 1 && !readAll) sjis = -1;
        if (euc >= bytes.Length - 1 && !readAll) euc = -1;

        //同一のものが1つもない場合
        if (sjis >= 0 && utf8 >= 0 && euc >= 0) return null;

        //再変換で同一のものが1個だけの場合
        if (sjis < 0 && utf8 >= 0 && euc >= 0) return enc_sjis;
        if (utf8 < 0 && sjis >= 0 && euc >= 0) return enc_utf8;
        if (euc < 0 && utf8 >= 0 && sjis >= 0) return enc_euc;

        //同一のものが複数ある場合は日本語らしさ判定
        double like_sjis = likeJapanese_ShiftJIS(bytes);
        double like_euc = likeJapanese_EUC_JP(bytes);
        double like_utf8 = likeJapanese_UTF8(bytes);

        if (utf8 < 0 && sjis < 0 && euc < 0)
        {
            return like_utf8 >= like_sjis && like_utf8 >= like_euc ? enc_utf8 : like_sjis >= like_euc ? enc_sjis : enc_euc;
        }
        else if (utf8 < 0 && sjis < 0)
        {
            return like_utf8 >= like_sjis ? enc_utf8 : enc_sjis;
        }
        else if (utf8 < 0 && euc < 0)
        {
            return like_utf8 >= like_euc ? enc_utf8 : enc_euc;
        }
        else if (euc < 0 && sjis < 0)
        {
            return like_sjis >= like_euc ? enc_sjis : enc_euc;
        }

        return null;
    }

    //BOM判定
    private static Encoding? checkBOM(byte[] bytes)
    {
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xfe && bytes[1] == 0xff)//UTF-16BE
            {
                return Encoding.BigEndianUnicode;
            }
            else if (bytes[0] == 0xff && bytes[1] == 0xfe)//UTF-16LE
            {
                return Encoding.Unicode;
            }
        }
        if (bytes.Length >= 3)
        {
            if (bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)//UTF-8
            {
                return new UTF8Encoding(true, true);
            }
            /*else if (bytes[0] == 0x2b && bytes[1] == 0x2f && bytes[2] == 0x76)//UTF-7
            {
                return Encoding.UTF7;
            }*/
        }
        if (bytes.Length >= 4)
        {
            if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xfe && bytes[3] == 0xff)//UTF-32BE
            {
                return new UTF32Encoding(true, true);
            }
            if (bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0x00 && bytes[3] == 0x00)//UTF-32LE
            {
                return new UTF32Encoding(false, true);
            }
        }

        return null;
    }

    //簡易ISO-2022-JP判定
    private static bool checkISO_2022_JP(byte[] bytes)
    {
        string str = BitConverter.ToString(bytes);

        return str.Contains("1B-24-40")
        || str.Contains("1B-24-42")
        || str.Contains("1B-26-40-1B-24-42")
        || str.Contains("1B-24-28-44")
        || str.Contains("1B-24-28-4F")
        || str.Contains("1B-24-28-51")
        || str.Contains("1B-24-28-50")
        || str.Contains("1B-28-4A")
        || str.Contains("1B-28-49")
        || str.Contains("1B-28-42");
    }

    //簡易文字コード判定
    //バイトを文字に変換し再度バイト変換したとき同一かどうか
    private static int checkReconversion(byte[] bytes, Encoding enc)
    {
        try
        {
            //文字列に変換
            string str = enc.GetString(bytes);

            //バイトに再変換
            byte[] rebytes = enc.GetBytes(str);

            if (BitConverter.ToString(bytes) == BitConverter.ToString(rebytes))
            {
                return -1;//同一
            }
            else
            {
                int len = bytes.Length <= rebytes.Length ? rebytes.Length : bytes.Length;
                for (int i = 0; i < len; i++)
                {
                    if (bytes[i] != rebytes[i])
                    {
                        return i == 0 ? 0 : i - 1;//一致バイト数
                    }
                }
            }
        }
        catch
        {
            ;
        }

        return 0;
    }

    //簡易日本語らしさ判定
    //日本語の文章と仮定したときShiftJISらしいか
    private static double likeJapanese_ShiftJIS(byte[] bytes)
    {
        int counter = 0;
        bool judgeSecondByte = false; //次回の判定がShiftJISの2バイト目の判定かどうか

        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];

            if (!judgeSecondByte)
            {
                if (b is 0x0D  //CR
                or 0x0A //LF
                or 0x09 //tab
                or >= 0x20 and <= 0x7E)//半角カナ除く1バイト
                {
                    counter++;
                }
                else if (b is >= 0x81 and <= 0x9F or >= 0xE0 and <= 0xFC)//ShiftJISの2バイト文字の1バイト目の場合
                {
                    //2バイト目の判定を行う
                    judgeSecondByte = true;
                }
                else if (b is >= 0xA1 and <= 0xDF)//ShiftJISの1バイト文字の場合(半角カナ)
                {
                    ;
                }
                else if (b is >= 0x00 and <= 0x7F)//ShiftJISの1バイト文字の場合
                {
                    ;
                }
                else
                {
                    //ShiftJISでない
                    return 0;
                }
            }
            else
            {
                if (b is >= 0x40 and <= 0x7E or >= 0x80 and <= 0xFC) //ShiftJISの2バイト文字の2バイト目の場合
                {
                    counter += 2;
                    judgeSecondByte = false;
                }
                else
                {
                    //ShiftJISでない
                    return 0;
                }
            }
        }

        return counter / (double)bytes.Length;
    }

    //日本語の文章と仮定したときUTF-8らしいか
    private static double likeJapanese_UTF8(byte[] bytes)
    {
        string str = BitConverter.ToString(bytes) + "-";
        int len = str.Length;

        //日本語らしいものを削除

        //制御文字
        str = str.Replace("0D-", "");//CR
        str = str.Replace("0A-", "");//LF
        str = str.Replace("09-", "");//tab

        //英数字記号
        for (byte b = 0x20; b <= 0x7E; b++)
        {
            str = str.Replace(string.Format("{0:X2}-", b), "");
        }

        //ひらがなカタカナ
        for (byte b1 = 0x81; b1 <= 0x83; b1++)
        {
            for (byte b2 = 0x80; b2 <= 0xBF; b2++)
            {
                str = str.Replace(string.Format("E3-{0:X2}-{1:X2}-", b1, b2), "");
            }
        }

        //常用漢字
        for (byte b1 = 0x80; b1 <= 0xBF; b1++)
        {
            for (byte b2 = 0x80; b2 <= 0xBF; b2++)
            {
                str = str.Replace(string.Format("E4-{0:X2}-{1:X2}-", b1, b2), "");
                str = str.Replace(string.Format("E5-{0:X2}-{1:X2}-", b1, b2), "");
                str = str.Replace(string.Format("E6-{0:X2}-{1:X2}-", b1, b2), "");
                str = str.Replace(string.Format("E7-{0:X2}-{1:X2}-", b1, b2), "");
                str = str.Replace(string.Format("E8-{0:X2}-{1:X2}-", b1, b2), "");
                str = str.Replace(string.Format("E9-{0:X2}-{1:X2}-", b1, b2), "");
            }
        }

        return (len - (double)str.Length) / len;
    }

    //日本語の文章と仮定したときEUC-JPらしいか
    private static double likeJapanese_EUC_JP(byte[] bytes)
    {
        string str = BitConverter.ToString(bytes) + "-";
        int len = str.Length;

        //日本語らしいものを削除

        //制御文字
        str = str.Replace("0D-", "");//CR
        str = str.Replace("0A-", "");//LF
        str = str.Replace("09-", "");//tab

        //英数字記号
        for (byte b = 0x20; b <= 0x7E; b++)
        {
            str = str.Replace(string.Format("{0:X2}-", b), "");
        }

        //ひらがなカタカナ記号
        for (byte b1 = 0xA1; b1 <= 0xA5; b1++)
        {
            for (byte b2 = 0xA1; b2 <= 0xFE; b2++)
            {
                str = str.Replace(string.Format("{0:X2}-{1:X2}-", b1, b2), "");
            }
        }

        //常用漢字
        for (byte b1 = 0xB0; b1 <= 0xEE; b1++)
        {
            for (byte b2 = 0xA1; b2 <= 0xFE; b2++)
            {
                str = str.Replace(string.Format("{0:X2}-{1:X2}-", b1, b2), "");
            }
        }

        return (len - (double)str.Length) / len;
    }
    #endregion

    #region Json
    private static readonly JsonSerializerSettings Settings =
        new()
        {
            ObjectCreationHandling = ObjectCreationHandling.Auto,
            DefaultValueHandling = DefaultValueHandling.Include,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = new StringEnumConverter[] { new() }
        };
    /// <summary>
    /// Jsonファイルの読み込みを行います。ファイルが存在しなかった場合、そのクラスの新規インスタンスを返します。
    /// </summary>
    /// <typeparam name="T">シリアライズしたクラス。</typeparam>
    /// <param name="filePath">ファイル名。</param>
    /// <returns>デシリアライズ結果。</returns>
    public static T ReadJson<T>(string filePath) where T : new()
    {
        if (!File.Exists(filePath))
        {
            // ファイルが存在しないので
            return new T();
        }
        using var stream = new StreamReader(filePath, Encoding.UTF8);
        string? json = stream.ReadToEnd();
        return JsonConvert.DeserializeObject<T>(json, Settings) ?? new T();
    }

    /// <summary>
    /// Jsonファイルの書き込みを行います。
    /// </summary>
    /// <param name="obj">シリアライズするインスタンス。</param>
    /// <param name="filePath">ファイル名。</param>
    public static void SaveJson(object obj, string filePath)
    {
        // directory が存在しない場合は作成する
        string? directory = System.IO.Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            System.IO.Directory.CreateDirectory(directory);

        try
        {
            using var stream = new StreamWriter(filePath, false, Encoding.UTF8);
            stream.Write(JsonConvert.SerializeObject(obj, Formatting.Indented, Settings));
        }
        catch (Exception e)
        {
            Log.Error(filePath + "を保存出来ませんでした。");
            Log.Write(e);
        }
    }
    #endregion
}
