using System.Collections.Concurrent;

using static AstrumLoom.AstrumCore;

using FHandle = AstrumLoom.FontHandle;

namespace AstrumLoom;

public class Skin
{
    public class ExoDummy { }
    public static Dictionary<string, Texture> Textures { get; set; } = [];
    public static Dictionary<string, Sound> Sounds { get; set; } = [];
    public static Dictionary<string, Number> Numbers { get; set; } = [];
    public static Dictionary<string, IFont> Fonts { get; set; } = [];
    public static Dictionary<string, ExoDummy> ExoDatas { get; set; } = [];

    public static Dictionary<string, string> Configs { get; set; } = [];

    public static int Width, Height;

    private static Queue<(string key, string value)> SkinQue = [];
    private static List<string> PathList = [];

    private static readonly ConcurrentDictionary<string, Texture?> _textureCache = new();
    private static readonly ConcurrentDictionary<string, string> _configCache = new();

    public static string DefaultSkin { get; set; } = "";
    public static string DefaultFont { get; set; } = "";
    public static int? DefaultSize { get; set; } = null;
    public static string SkinPath
    {
        get
        {
            string skin = DefaultSkin ?? "";
            if (string.IsNullOrEmpty(skin)) skin = SkinList()[0];
            if (field != skin)
            {
                field = skin;
                _textureCache.Clear();
                _configCache.Clear();
            }
            return Path.Combine(FilePath("System"), field);
        }
    } = "";

    private static bool _loading = false;
    public static void Load(bool inque = false, bool? asyncload = null)
    {
        //if (asyncload.HasValue) AsyncLoad = asyncload.Value;
        var (names, nums) = LoadConfig();
        string skinPath = SkinPath;
        QueMax = 0;
        if (Directory.Exists(skinPath))
        {
            if (_loading)
            {
                Log.Warning($"Skin: 既にスキンの読み込みが行われています。");
                return;
            }
            _loading = true;

            Log.Write($"Skin: {FilePath(skinPath)} のスキンを読み込みます...", true);
            var namedic = names;
            var numdic = nums;
            Textures.Clear();
            Sounds.Clear();
            Numbers.Clear();
            Fonts.Clear();
            ExoDatas.Clear();
            SkinQue.Clear();
            PathList.Clear();
            //ResourceLoad();
            foreach (var name in namedic)
            {
                Add(name.name, name.path, inque);
                PathList.Add(Path.GetFullPath(name.path));
            }
            foreach (string file in Directory.GetFiles(skinPath, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(file).ToLower();
                Add(name, file, inque);
                PathList.Add(Path.GetFullPath(file));
            }

            // 既存のSkinQueに対して、key順に並び替える処理を追加
            SkinQue = new Queue<(string key, string value)>(SkinQue.OrderBy(item => item.key));
            foreach (var (name, data) in numdic)
            {
                // NumberとFontを識別
                if (data.Contains(".font") || data.Contains(".ttf") || data.Contains(".otf"))
                {
                    string[] set = data.Split(',');
                    string file = set[0].Trim();
                    int fontsize = 20, thick = 1, edge = 0, space = 0;
                    if (set.Length > 1)
                    {
                        int.TryParse(set[1], out fontsize);
                        if (set.Length > 2) int.TryParse(set[2], out thick);
                        if (set.Length > 3) int.TryParse(set[3], out edge);
                        if (set.Length > 4) int.TryParse(set[4], out space);
                    }
                    if (inque)
                        SkinQue.Enqueue(("fon" + name.ToLower(), string.Join(',', [Path.Combine(skinPath, file), fontsize, thick, edge, space])));
                    else
                    {
                        Fonts.TryAdd(name.ToLower(), FHandle.Create(
                            Path.Combine(skinPath, file), fontsize) ?? Drawing.DefaultFont);

                        if (name == "default")
                        {
                            var deffont = GetFont("Default");
                            //if (deffont.Enable)
                            Drawing.DefaultFont = deffont;
                        }
                    }
                }
                else if (Textures.ContainsKey(name.ToLower()) || SkinQue.Any(q => q.key == "tex" + name.ToLower()))
                {
                    string[] set = data.Split(',');
                    char[]? chars = null;
                    int width = 0, height = 0, space = 0, startx = 0, starty = 0;
                    if (set.Length > 0)
                    {
                        if (set.Length == 1)//char
                        {
                            chars = set[0].ToCharArray();
                        }
                        else if (set.Length >= 2)//width, height, space, char, startx, starty
                        {
                            int.TryParse(set[0], out width);
                            int.TryParse(set[1], out height);
                            int.TryParse(set[2], out space);
                            if (set.Length > 3) chars = set[3].ToCharArray();
                            if (set.Length > 4) int.TryParse(set[4], out startx);
                            if (set.Length > 5) int.TryParse(set[5], out starty);
                        }
                    }
                    string file = SkinQue.Where((a) => a.key == "tex" + name.ToLower()).FirstOrDefault().value;
                    if (inque)
                        SkinQue.Enqueue(("num" + name.ToLower(), string.Join(',', [file,
                        width, height, chars != null ? string.Join("", chars) : "", startx, starty, space])));
                    else Numbers.TryAdd(name.ToLower(), new(GetTexture(name).Path, width, height, chars, startx, starty)
                    {
                        Space = space
                    });
                }
            }
            QueMax = SkinQue.Count;
            string fontpath = Path.Combine(skinPath, "default.ttf");
            string defaultFont = DefaultFont ??
                (File.Exists(fontpath) ? fontpath : FHandle.SystemFont);
            SetFont(Path.Combine(skinPath, "default.ttf"));
            if (inque)
                Log.Write($"Skin: 読み込みキューに追加しました。({SkinQue.Count} items)", true);
            else FinishLoad();
        }
        var size = WindowSize(SkinConfig("SkinSize"));
        Width = size.width > 0 ? size.width : (int)SkinConfigValue("Width");
        Height = size.height > 0 ? size.height : (int)SkinConfigValue("Height");

        if (Width == 0 || Height == 0)
        {
            var tex = GetTexture("Title");
            Width = tex.Width;
            Height = tex.Height;
        }
        if (InitCompleted)
        {
            int w1 = Width > 0 ? Width : 1280;
            int h1 = Height > 0 ? Height : 720;
            int h2 = DefaultSize ?? w1;
            double s = (double)h2 / h1;
            if (Width == w1 && Height == h1 && Math.Abs(WindowScale - s) < 0.01)
            {
                Log.Write($"Skin: ウィンドウサイズは変更されていません。({w1}x{h1}, {s:0.00}倍)", true);
                return;
            }
            Log.Write($"Skin: ウィンドウサイズを {w1}x{h1} に変更します...", true);
            //SetSize(w1, h1, s);
            //Drawing.DefaultHandle = new();
            if (!inque) Load(); // 再読み込み
        }
        else
            Log.Write($"Skin: {Width}x{Height}でウィンドウを開始します...", true);
    }
    public static (List<(string name, string path)> names, List<(string name, string data)> nums) LoadConfig(string file = "Skin.ini")
    {
        _configCache.Clear();
        Configs = [];
        string skinPath = SkinPath;
        string inipath = Path.Combine(skinPath, file);
        if (!Directory.Exists(skinPath) || !File.Exists(inipath))
        {
            inipath = AstrumCore.SearchPath(file, ["", "Data"]);
        }

        List<(string name, string path)> namedic = [];
        List<(string name, string data)> numdic = [];
        if (File.Exists(inipath))
        {
            var list = Text.Read(inipath);
            foreach (string line in list)
            {
                // name, file
                if (line.StartsWith("Texture:") || line.StartsWith("Sound:") || line.StartsWith("Exo:"))
                {
                    string[] parts = line.Split(':', 2)[1].Split(',');
                    if (parts.Length == 2)
                    {
                        string name = parts[0].Trim().ToLower();
                        string filePath = parts[1].Trim();
                        string f = Path.Combine(skinPath, filePath);
                        if (File.Exists(f))
                            namedic.Add((name, f));
                        else if (Directory.Exists(f))
                        {
                            // ディレクトリ内のファイルを再帰的に取得
                            foreach (string subFile in Directory.GetFiles(f, "*", SearchOption.AllDirectories))
                            {
                                string subName = Path.GetFileNameWithoutExtension(subFile).ToLower();
                                namedic.Add((name + "_" + subName, subFile));
                            }
                        }
                    }
                }
                // Number: name, file, width, height, space, chars, startx, starty
                // Font: name, file, size, thick, edge, space
                else if (line.StartsWith("Number:") || line.StartsWith("Num:") || line.StartsWith("Font:"))
                {
                    string[] parts = line.Split(':', 2)[1].Split(',', 2);
                    if (parts.Length > 0)
                    {
                        string name = parts[0].Trim().ToLower();
                        string data = parts.Length > 1 ? parts[1].Trim() : "";
                        numdic.Add((name, data));
                    }
                }
                else if (line.Contains(':') && !line.StartsWith(';'))
                {
                    string[] parts = line.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim().ToLower();
                        string value = parts[1].Trim();
                        if (!Configs.ContainsKey(key))
                        {
                            Configs[key] = value;
                        }
                    }
                }
            }
        }
        Log.Write($"Skin: 設定ファイル {FilePath(inipath)} を読み込みました。", true);
        return (namedic, numdic);
    }

    public static void SetFont(string font)
    {
        var f = FHandle.Create(font, 16);
        if (f != null)// && f.Enable
            Drawing.DefaultFont = f;
    }

    private static int _logcount = 0;
    public static void FinishLoad()
    {
        if (!_loading) return;
        if (SkinQue.Count > 0)
        {
            Log.Warning($"Skin: 読み込みキューに {SkinQue.Count} items 残っています。");
            while (SkinQue.Count > 0)
            {
                ReadQue(100);
            }
        }

        int total = Textures.Count + Sounds.Count + Numbers.Count + ExoDatas.Count;
        // TextureとSoundの非同期読み込み完了を待機
        // NumberとExoもカウント
        int count = ReadedAsync();

        if (count < total && QueMax > 0)
        {
            int l = count / (int)(QueMax / 10.0);
            if (l != _logcount)
            {
                _logcount = l;
                Log.Debug($"Skin: 非同期読み込み中... ({count}/{total})", true);
            }
            return;
        }

        ReadedCount = QueMax;
        //ResourceLoad(true);
        Log.Write($"Skin: 読み込み完了! ({Textures.Count} textures, {Sounds.Count} sounds, {Numbers.Count} numbers, {Fonts.Count} fonts, {ExoDatas.Count} exo)", true);
        string logpath = Path.Combine(AppPath, "Logs\\LoadedSkin.txt");
        Text.Save(logpath, LogExport());
        Log.Write($"Skin: ログを {logpath} に保存しました。", true);
        _loading = false;
    }

    public static bool Loaded => !_loading && SkinQue.Count == 0;

    public static int QueMax = 0;
    public static int ReadQue(int count = 1)
    {
        if (!_loading) return QueMax;
        if (count < 1) count = 1;
        for (int i = 0; i < count; i++)
        {
            if (SkinQue.Count == 0)
            {
                FinishLoad();
                return QueMax;
            }
            var (key, value) = SkinQue.Dequeue();
            string name = key[3..];
            string file = value;
            switch (key[..3])
            {
                case "tex":
                    Textures.TryAdd(name, new(file));
                    break;
                case "snd":
                    Sounds.TryAdd(name, new(file));
                    break;
                case "fon":
                    {
                        string[] param = file.Split(',');
                        Fonts.TryAdd(name, FHandle.Create(
                            param[0],
                            size: int.Parse(param[1]),
                            thick: int.Parse(param[2]),
                            edge: int.Parse(param[3]),
                            spacing: int.Parse(param[4])) ?? Drawing.DefaultFont);
                        if (name == "default")
                        {
                            var deffont = GetFont("Default");
                            //if (deffont.Enable)
                            Drawing.DefaultFont = deffont;
                        }
                    }
                    break;
                case "num":
                    {
                        string[] param = file.Split(',');
                        Numbers.TryAdd(name, new(param[0], int.Parse(param[1]), int.Parse(param[2]), !string.IsNullOrEmpty(param[3]) ? param[3].ToArray() : null, int.Parse(param[4]), int.Parse(param[5]))
                        {
                            Space = int.Parse(param[6])
                        });
                    }
                    break;
                case "exo":
                    {
                        //ExoDatas.TryAdd(name, new(file, true));
                    }
                    break;
            }
        }
        if (SkinQue.Count == 0) FinishLoad();
        return QueMax - SkinQue.Count;
    }

    public static int ReadedCount = 0;
    private static int ReadedAsync()
    {
        int count = 0;
        foreach (var tex in Textures.Values)
        {
            if (tex.Loaded) count++;
        }
        foreach (var snd in Sounds.Values)
        {
            if (snd.Loaded) count++;
        }
        foreach (var num in Numbers.Values)
        {
            if (num.Loaded) count++;
        }
        foreach (var exo in ExoDatas.Values)
        {
            //if (exo.Loaded()) count++;
        }
        count += Fonts.Count;
        ReadedCount = count;
        return count;
    }

    private static void Add(string name, string file, bool inque)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!File.Exists(file)) return;

        string ext = Path.GetExtension(file).ToLower();
        if (ext is ".png" or ".jpg")
        {
            AddTexture(name, file, inque);
        }
        else if (ext is ".wav" or ".ogg" or ".mp3")
        {
            // 既存のSoundsおよびSkinQue内のkeyをチェック
            if (SndExit(name, file))
            {
                string parentName = new DirectoryInfo(Path.GetDirectoryName(file) ?? "").Name;
                name = $"{parentName.ToLower()}_{name}";
                string n = name;
                int i = 0;
                while (SndExit(name, file))
                {
                    i++;
                    name = n + "_" + i;
                }
            }
            if (!Added(file))
            {
                if (inque)
                    SkinQue.Enqueue(("snd" + name, file));
                else
                    Sounds[name] = new Sound(file);
            }
        }
        else if (ext == ".exo")
        {
            // 既存のExoDatasおよびSkinQue内のkeyをチェック
            if (ExoDatas.ContainsKey(name) || SkinQue.Any(q => q.key == "exo" + name))
            {
                string parentName = new DirectoryInfo(Path.GetDirectoryName(file) ?? "").Name;
                name = $"{parentName.ToLower()}_{name}";
                string n = name;
                int i = 0;
                while (ExoDatas.ContainsKey(name) || SkinQue.Any(q => q.key == "exo" + name))
                {
                    i++;
                    name = n + "_" + i;
                }
            }
            if (!Added(file))
            {
                if (inque)
                    SkinQue.Enqueue(("exo" + name, file));
                // else
                //    ExoDatas[name] = new Exo(file, true);
            }
        }
    }
    public static void AddTexture(string name, string file, bool inque = false)
    {
        name = name.ToLower();
        if (TexExit(name, file))
        {
            string parentName = new DirectoryInfo(Path.GetDirectoryName(file) ?? "").Name;
            name = $"{parentName.ToLower()}_{name}";
            string n = name;
            int i = 0;
            while (TexExit(name))
            {
                i++;
                name = n + "_" + i;
            }
        }
        if (!Added(file))
        {
            if (inque)
                SkinQue.Enqueue(("tex" + name, file));
            else
                Textures[name] = new Texture(file);
        }
    }
    private static bool TexExit(string name, string path = "", bool duplicate = true) =>
        // 既存のTexturesおよびSkinQue内のkeyをチェック
        (duplicate || PathList.Contains(Path.GetFullPath(path))) &&
            (Textures.ContainsKey(name) || SkinQue.Any(q => q.key == "tex" + name));
    private static bool SndExit(string name, string path = "", bool duplicate = true) =>
        // 既存のSoundsおよびSkinQue内のkeyをチェック
        (duplicate || PathList.Contains(Path.GetFullPath(path))) &&
            (Sounds.ContainsKey(name) || SkinQue.Any(q => q.key == "snd" + name));
    private static bool Added(string path) => PathList.Contains(Path.GetFullPath(path));

    #region Resourse
    #region Texture
    public static Texture GetTexture(string name, string subname = "") => Texture(name) ?? new();
    public static Texture? Texture(string name, string subname = "")
    {
        name = name.ToLower();
        if (_textureCache.TryGetValue(name, out var cached))
            return cached;
        Texture? result = null;
        if (Textures.TryGetValue(name.ToLower(), out var value))
        {
            result = value;
        }
        else if (!string.IsNullOrEmpty(subname))
        {
            if (Textures.TryGetValue(subname, out var subvalue))
            {
                result = subvalue;
            }
        }
        if (result != null)
            _textureCache[name] = result;
        result?.Pump();
        return result != null && result.Enable ? result : null;
    }
    public static void Tx(string name, double x, double y, double opacity = 1, ReferencePoint point = ReferencePoint.TopLeft)
    {
        name = name.ToLower();/*
        if (ExoDatas.ContainsKey(name))
        {
            if (!ExoDatas[name].IsPlaying())
                ExoDatas[name].Start();
            ExoDatas[name].Draw((float)x, (float)y, point);
        }
        else */
        if (Textures.TryGetValue(name, out var value))
        {
            value.Opacity = opacity;
            value.Point = point;
            value.Draw(x, y);
        }
    }
    public static Texture[] TextureList(string dir)
    {
        dir = dir.ToLower();
        List<Texture> list = [];
        foreach (var key in Textures.Values)
        {
            if (key.Path.StartsWith(dir + "_"))
            {
                list.Add(key);
            }
        }
        return [.. list];
    }
    #endregion
    #region Sound
    public static Sound? Sound(string name, string subname = "")
    {
        name = name.ToLower();
        Sound? result = null;
        if (Sounds.TryGetValue(name, out var value))
        {
            value?.Pump();
            result = value;
        }
        else if (!string.IsNullOrEmpty(subname))
        {
            if (Sounds.TryGetValue(subname, out var subvalue))
            {
                subvalue?.Pump();
                result = subvalue;
            }
        }
        return result != null && result.Enable ? result : null;
    }
    public static void Sfx(string name, bool loop = false)
    {
        name = name.ToLower();
        if (Sounds.ContainsKey(name))
        {
            if (loop)
            {
                Sounds[name].PlayStream();
            }
            else
            {
                Sounds[name].Play();
            }
        }
    }
    #endregion
    #region Font
    public static IFont GetFont(string name) => FontHandle(name) ?? Drawing.DefaultFont;
    public static IFont? FontHandle(string name)
    {
        name = name.ToLower();
        return Fonts.ContainsKey(name) ? Fonts[name] : null;
    }
    #endregion
    #region Number
    public static Number GetNumber(string name) => Number(name) ?? new();
    public static Number? Number(string name)
    {
        name = name.ToLower();
        return Numbers.TryGetValue(name, out var value) ? value : null;
    }
    public static void Num(string name, double x, double y, object num,
        int type = 0, double opacity = 1, ReferencePoint point = ReferencePoint.TopLeft, int left = 0, bool scaleadd = true)
        => Num(name, x, y, num, 1.0, type, opacity, point, left, scaleadd);
    public static void Num(string name, double x, double y, object num, double size,
        int type = 0, double opacity = 1, ReferencePoint point = ReferencePoint.TopLeft, int left = 0, bool scaleadd = true)
        => Num(name, x, y, num, size, size, type, opacity, point, left, scaleadd);
    public static void Num(string name, double x, double y, object num, double scaleX, double scaleY,
        int type = 0, double opacity = 1, ReferencePoint point = ReferencePoint.TopLeft, int left = 0, bool scaleadd = true)
    {
        name = name.ToLower();
        var number = Number(name);
        if (number != null) number.Draw(x, y, num, scaleX, scaleY, type, opacity, point, left, scaleadd);
        else Drawing.Text(x, y, num, Color.White, point, opacity: opacity);
    }
    #endregion
    #region Exo
    public static ExoDummy GetExo(string name) => Exo(name) ?? new();
    public static ExoDummy? Exo(string name)
    {
        return new();
        name = name.ToLower();
        //return ExoDatas.TryGetValue(name, out var value) && value.Enable ? value : null;
    }
    #endregion
    #endregion
    #region Config
    public static string? SkinConfig(string key)
    {
        key = key.ToLower();
        if (_configCache.TryGetValue(key, out string? cached)) return cached;
        string result = "";
        if (Configs.TryGetValue(key, out string? value))
        {
            result = value;
        }
        _configCache[key] = result;
        return !string.IsNullOrEmpty(result) ? result : null;
    }
    public static bool ExitsConfig(string key)
        => !string.IsNullOrEmpty(SkinConfig(key));
    public static double SkinConfigValue(string key, double nulldefault = 0) => double.TryParse(SkinConfig(key)?.Trim(), out double value) ? value : nulldefault;
    public static double[] SkinConfigArray(string key, char separator = ',', double[]? defaults = null)
    {
        string? value = SkinConfig(key);
        if (string.IsNullOrEmpty(value)) return defaults ?? [];
        string[] parts = value.Split(separator);
        double[] result = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (double.TryParse(parts[i].Trim(), out double v))
            {
                result[i] = v;
            }
        }
        return result;
    }
    public static System.Drawing.Point SkinConfigPoint(string key, char separator = ',', double defaultx = 0, double defaulty = 0)
    {
        double x = SkinConfigValue(key + "X", defaultx);
        double y = SkinConfigValue(key + "Y", defaulty);
        double[] value = SkinConfigArray(key, separator);
        return value.Length < 2 ? new((int)x, (int)y) : new((int)value[0], (int)value[1]);
    }
    public static bool SkinConfigBool(string key) => SkinConfigValue(key) > 0;
    #endregion

    public static List<string> LogExport()
    {
        List<string> log = [];
        log.Add($"Skin Config:");
        foreach (string key in Configs.Keys)
        {
            log.Add($"{key}: {Configs[key]}");
        }
        log.Add("");
        log.Add($"Textures: {Textures.Count}");
        foreach (var tex in Textures)
        {
            log.Add($"  {tex.Key}: {tex.Value.Path} ({tex.Value.Width}x{tex.Value.Height})");
        }
        log.Add("");
        log.Add($"Sounds: {Sounds.Count}");
        foreach (var snd in Sounds)
        {
            log.Add($"  {snd.Key}: {snd.Value.Path}");
        }
        log.Add("");
        log.Add($"Numbers: {Numbers.Count}");
        foreach (var num in Numbers)
        {
            log.Add($"  {num.Key}: {num}");
        }
        log.Add("");
        log.Add($"Fonts: {Fonts.Count}");
        foreach (var fon in Fonts)
        {
            log.Add($"  {fon.Key}: {fon})");
        }
        log.Add("");
        log.Add($"Exo: {ExoDatas.Count}");
        foreach (var exo in ExoDatas)
        {
            log.Add($"  {exo.Key}: {exo}");
        }
        return log;
    }

    private static List<string> _skinList = [];
    public static List<string> SkinList()
    {
        string inifile = "Skin.ini";
        string targetdir = FilePath("System");

        if (_skinList.Count < Directory.GetDirectories(targetdir).Length)
            _skinList = [];
        if (_skinList.Count > 0) return _skinList;

        List<string> list = [];
        if (Directory.Exists(targetdir))
        {
            foreach (string dir in Directory.GetDirectories(targetdir))
            {
                string skinname = Path.GetFileName(dir);
                string inipath = Path.Combine(dir, inifile);
                if (File.Exists(inipath))
                {
                    list.Add(skinname);
                }
            }
        }
        if (list.Count == 0) list.Add("Default");
        _skinList = list;
        return _skinList;
    }

    #region WindowSize
    public static string WindowSize((int, int) value) => value == (1280, 720)
            ? "HD"
            : value == (1920, 1080) ? "FHD" : value == (2560, 1440) ? "WQHD" : value == (3840, 2160) ? "4K" : $"{value.Item1}*{value.Item2}";
    public static (int width, int height) WindowSize(string? value)
    {
        if (value == "HD" || string.IsNullOrEmpty(value)) return (1280, 720);
        if (value == "FHD") return (1920, 1080);
        if (value == "WQHD") return (2560, 1440);
        if (value == "4K") return (3840, 2160);
        string[] split = value.Split(new char[] { ',', '*' });
        return (int.Parse(split[0]), int.Parse(split[1]));
    }
    public static int WindowSizeInt((int, int) value) => value == (1280, 720) ? 1 : value == (1920, 1080) ? 2 : value == (2560, 1440) ? 3 : value == (3840, 2160) ? 4 : 0;
    public static (int width, int height) WindowSizeInt(int value)
    {
        if (value == 1) return (1280, 720);
        if (value == 2) return (1920, 1080);
        if (value == 3) return (2560, 1440);
        if (value == 4) return (3840, 2160);
        return (0, 0);
    }
    #endregion
}