using System.Globalization;

namespace AstrumLoom.Exo.Loaders;

/// <summary>
/// AviUtl2 のプロジェクトファイル（.aup2）のパースを担当するローダ。
///
/// .exo との主な差分:
/// - ヘッダは [project] + [scene.N] の中に video.width/video.height/video.rate/video.scale が入る（lengthは無い）
/// - 区間は frame=start,end（0始まり・両端含む）で表現される。内部では 1始まりに正規化するため +1 する
/// - エフェクト名は effect.name=、ファイルパスは ファイル= で表現される
/// - オブジェクトの位置・拡大率・透明度は「標準描画」エフェクトの X=/Y=/拡大率=/透明度=/合成モード= にまとまっている
/// </summary>
internal class Aup2Loader : IAnimeLoader
{
    private static string Key(string line) => line.Split('=', 2)[0];
    private static string Parse(string line) => line.Split('=', 2)[1];
    private static int ParseInt(string line) => int.TryParse(Parse(line), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : 0;

    /// <summary>セクション見出し（"[...]"）の種類。</summary>
    private enum SectionKind { None, Project, Scene, Object, Effect }

    /// <summary>
    /// "[project]" "[scene.0]" "[12]" "[12.3]" を明示的に区別する。
    /// 旧実装の「ドットを含むかどうか」だけで判定する脆いロジックは使わない。
    /// </summary>
    private static SectionKind Classify(string line, out int index, out int subIndex)
    {
        index = subIndex = -1;
        if (!line.StartsWith('[') || !line.EndsWith(']')) return SectionKind.None;

        string inner = line[1..^1];
        if (inner == "project") return SectionKind.Project;
        if (inner.StartsWith("scene.")) return SectionKind.Scene;

        string[] parts = inner.Split('.');
        if (parts.Length == 1 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) return SectionKind.Object;
        if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out subIndex)) return SectionKind.Effect;

        return SectionKind.None;
    }

    public AnimeDocument Load(string filePath)
    {
        var doc = new AnimeDocument();
        var imageObjects = doc.ImageObjects;
        var groupObjects = doc.GroupObjects;
        var soundObjects = doc.SoundObjects;

        Dictionary<string, Texture> textureByFileName = [];

        // 現在の [<n>] オブジェクトのフレーム範囲・レイヤー（1始まりに正規化済み）
        int objStart = 0, objEnd = 0, objLayer = 0;

        // 現在のオブジェクトに属する各要素。[<n>.<m>] を跨いで参照する
        ImageObject? currentImage = null;
        GroupObject? currentGroup = null;
        SoundObject? currentSound = null;
        // 現在の [<n>.<m>] エフェクトが位置/拡大率/透明度などを適用すべき対象（画像 or グループ）
        Object? currentTransformTarget = null;

        IEnumerable<string> lines = Text.Read(filePath);
        foreach (string line in lines)
        {
            var kind = Classify(line, out _, out _);
            if (kind != SectionKind.None)
            {
                if (kind == SectionKind.Object)
                {
                    // 新しいオブジェクトの開始。前オブジェクトの状態をリセットする
                    objStart = objEnd = objLayer = 0;
                    currentImage = null;
                    currentGroup = null;
                    currentSound = null;
                    currentTransformTarget = null;
                }
                // Effectセクションの境目では currentTransformTarget は保持する
                // （画像ファイル/グループ制御の直後に続く標準描画セクションが同じオブジェクトを指すため）
                continue;
            }

            string key = Key(line);

            if (key == "video.width") doc.Width = ParseInt(line);
            else if (key == "video.height") doc.Height = ParseInt(line);
            else if (key == "video.rate") doc.Rate = ParseInt(line);
            else if (key == "video.scale") doc.Scale = ParseInt(line);
            else if (key == "layer") objLayer = ParseInt(line);
            else if (key == "frame")
            {
                // "frame=0,899" (0始まり・両端含む) を内部の1始まりに正規化する
                string[] parts = Parse(line).Split(',');
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int f0);
                int f1 = parts.Length > 1
                    ? (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int p1) ? p1 : f0)
                    : f0;
                objStart = f0 + 1;
                objEnd = f1 + 1;
            }
            else if (key == "effect.name")
            {
                string name = Parse(line);
                Object baseObj = new() { StartFrame = objStart, EndFrame = objEnd, Layer = objLayer };
                switch (name)
                {
                    case "画像ファイル":
                        currentImage = new ImageObject(baseObj);
                        imageObjects.Add(currentImage);
                        currentTransformTarget = currentImage;
                        break;
                    case "グループ制御":
                        currentGroup = new GroupObject(baseObj);
                        groupObjects.Add(currentGroup);
                        currentTransformTarget = currentGroup;
                        break;
                    case "音声ファイル":
                        currentSound = new SoundObject(baseObj);
                        soundObjects.Add(currentSound);
                        break;
                    case "標準描画":
                        // 直前の画像/グループ制御に対する位置・拡大率・透明度をこの後の行で読む
                        break;
                    case "音声再生":
                        // 直前の音声ファイルに対する音量・左右をこの後の行で読む
                        break;
                }
            }
            else if (key == "ファイル")
            {
                string filePart = Parse(line);
                string fileName = Path.GetFileName(filePart);

                if (currentImage != null && currentImage.Texture == null)
                {
                    if (!textureByFileName.TryGetValue(fileName, out var texture))
                    {
                        string candidate = Path.GetDirectoryName(filePath) + @"\" + fileName;
                        if (!File.Exists(candidate) && File.Exists(filePart))
                            candidate = filePart;
                        texture = new Texture(candidate);
                        textureByFileName[fileName] = texture;
                    }
                    currentImage.Texture = texture;
                }
                else if (currentSound != null && currentSound.Sound == null)
                {
                    string candidate = Path.GetDirectoryName(filePath) + @"\" + fileName;
                    if (!File.Exists(candidate) && File.Exists(filePart))
                        candidate = filePart;
                    currentSound.Sound = new Sound(candidate);
                }
            }
            else if (key == "X")
            {
                ApplyPosition(currentTransformTarget, line, isX: true);
            }
            else if (key == "Y")
            {
                ApplyPosition(currentTransformTarget, line, isX: false);
            }
            else if (key == "拡大率")
            {
                var r = ParamParser.Parse(Parse(line));
                if (currentTransformTarget is ImageObject img)
                {
                    img.Scale.StartScale = r.Start / 100.0f;
                    img.Scale.EndScale = r.HasEnd ? r.End / 100.0f : img.Scale.StartScale;
                    img.Scale.Easing = r.Easing;
                }
                else if (currentTransformTarget is GroupObject grp)
                {
                    grp.Scale.StartScale = r.Start / 100.0f;
                    grp.Scale.EndScale = r.HasEnd ? r.End / 100.0f : grp.Scale.StartScale;
                    grp.Scale.Easing = r.Easing;
                }
            }
            else if (key == "透明度")
            {
                var r = ParamParser.Parse(Parse(line));
                if (currentTransformTarget is ImageObject img)
                {
                    img.Opacity.StartOpacity = 1 - r.Start / 100.0f;
                    img.Opacity.EndOpacity = r.HasEnd ? 1 - r.End / 100.0f : img.Opacity.StartOpacity;
                    img.Opacity.Easing = r.Easing;
                }
            }
            else if (key == "Z軸回転")
            {
                var r = ParamParser.Parse(Parse(line));
                if (currentTransformTarget is ImageObject img)
                {
                    img.Rotation.StartRotation = r.Start;
                    img.Rotation.EndRotation = r.HasEnd ? r.End : img.Rotation.StartRotation;
                    img.Rotation.Easing = r.Easing;
                }
            }
            else if (key == "合成モード")
            {
                if (currentTransformTarget is ImageObject img)
                    img.BlendMode = BlendModeMap.Parse(Parse(line));
            }
            else if (key == "音量")
            {
                var r = ParamParser.Parse(Parse(line));
                if (currentSound != null)
                {
                    currentSound.StartVolume = r.Start / 100.0f;
                    currentSound.EndVolume = r.HasEnd ? r.End / 100.0f : currentSound.StartVolume;
                }
            }
            else if (key == "左右")
            {
                if (currentSound != null)
                {
                    string v = Parse(line).Split(',')[0];
                    if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float pan)) currentSound.Pan = pan / 100.0f;
                }
            }
            else if (key == "ループ再生")
            {
                if (currentSound != null) currentSound.Loop = ParseInt(line) != 0;
            }
        }

        // 尺は全オブジェクトの最大終端フレームから算出する（.aup2 には length に相当する値が無い）
        int maxEnd = 0;
        foreach (var o in imageObjects) maxEnd = System.Math.Max(maxEnd, o.EndFrame);
        foreach (var o in soundObjects) maxEnd = System.Math.Max(maxEnd, o.EndFrame);
        doc.Length = maxEnd;

        return doc;
    }

    private static void ApplyPosition(Object? target, string line, bool isX)
    {
        var r = ParamParser.Parse(Parse(line));
        if (target is ImageObject img)
        {
            img.Position.StartPosition = isX
                ? new(r.Start, img.Position.StartPosition.Y)
                : new(img.Position.StartPosition.X, r.Start);
            img.Position.EndPosition = r.HasEnd
                ? (isX ? new(r.End, img.Position.EndPosition.Y) : new(img.Position.EndPosition.X, r.End))
                : new(img.Position.StartPosition.X, img.Position.StartPosition.Y);
            img.Position.Easing = r.Easing;
        }
        else if (target is GroupObject grp)
        {
            grp.Position.StartPosition = isX
                ? new(r.Start, grp.Position.StartPosition.Y)
                : new(grp.Position.StartPosition.X, r.Start);
            grp.Position.EndPosition = r.HasEnd
                ? (isX ? new(r.End, grp.Position.EndPosition.Y) : new(grp.Position.EndPosition.X, r.End))
                : new(grp.Position.StartPosition.X, grp.Position.StartPosition.Y);
            grp.Position.Easing = r.Easing;
        }
    }
}
