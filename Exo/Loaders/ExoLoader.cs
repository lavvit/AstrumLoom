using System.Globalization;

namespace AstrumLoom.Exo.Loaders;

/// <summary>
/// AviUtl(初代)の exedit プロジェクトファイル（.exo）のパースを担当するローダ。
/// 旧 Extend/ExoAnimation.cs に同居していたパース処理をそのまま移設したもの。
/// </summary>
internal class ExoLoader : IAnimeLoader
{
    // exoファイルの "key=value" 形式の行を解釈するための小さなヘルパー群
    private static string Key(string line) => line.Split('=', 2)[0];
    private static string Parse(string line) => line.Split('=', 2)[1];
    private static int ParseInt(string line) => int.TryParse(Parse(line), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : 0;

    public AnimeDocument Load(string filePath)
    {
        var doc = new AnimeDocument();
        var imageObjects = doc.ImageObjects;
        var groupObjects = doc.GroupObjects;
        var soundObjects = doc.SoundObjects;

        // file名からTextureを直接引くための辞書。imageObjectsはfile=行が無い中間点でも
        // 増えるため添字が対応しなくなる。ここではその暗黙対応に頼らない。
        Dictionary<string, Texture> textureByFileName = [];

        Object? currentObject = null;
        var currentFilter = FilterType.None;
        SoundObject? currentSoundObject = null;

        IEnumerable<string> lines = Text.Read(filePath);
        foreach (string line in lines)
        {
            string key = Key(line);
            #region [exeditのパース]
            if (currentObject == null)
            {
                if (key is "width" or "video.width")
                    doc.Width = ParseInt(line);
                else if (key is "height" or "video.height")
                    doc.Height = ParseInt(line);
                else if (key is "rate" or "video.rate")
                    doc.Rate = ParseInt(line);
                else if (key is "scale" or "video.scale")
                    doc.Scale = ParseInt(line);
                else if (key is "length")
                    doc.Length = ParseInt(line);
            }
            #endregion

            #region [ オブジェクトのパース]

            // [0]のような小数点なしの行
            if (line.StartsWith('[') && line.Contains(']') && !line.StartsWith("[exedit]") && !line.Contains("."))
            {
                string indexString = line.Trim('[', ']');
                if (!indexString.Contains('.') && indexString != "exedit" && indexString != "project" && !indexString.StartsWith("scene"))
                    if (int.TryParse(indexString, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                    {
                        // オブジェクトの作成
                        Object exoObject = new();
                        currentObject = exoObject;
                        currentSoundObject = null;
                    }
            }

            if (currentObject != null)
            {
                // [0]のような小数点なしの行
                if (line.StartsWith("start="))
                {
                    currentObject.StartFrame = ParseInt(line);
                }
                else if (line.StartsWith("end="))
                {
                    currentObject.EndFrame = ParseInt(line);
                }
                else if (line.StartsWith("layer="))
                {
                    currentObject.Layer = ParseInt(line);
                }

                if (line.StartsWith("_name="))
                {
                    string name = Parse(line);
                    if (name == "グループ制御")
                    {
                        currentFilter = FilterType.None;
                        GroupObject groupObject = new(currentObject);
                        currentObject = groupObject;

                        groupObjects.Add(groupObject);
                    }
                    else if (name == "画像ファイル")
                    {
                        currentFilter = FilterType.None;
                        ImageObject imageObject = new(currentObject);
                        currentObject = imageObject;

                        imageObjects.Add(imageObject);
                    }
                    else if (name is "音声ファイル" or "音声波形")
                    {
                        currentFilter = FilterType.None;
                        SoundObject soundObject = new(currentObject);
                        currentObject = soundObject;
                        currentSoundObject = soundObject;

                        soundObjects.Add(soundObject);
                    }
                    else if (name == "音声再生")
                    {
                        currentFilter = FilterType.None;
                        // 直前の音声ファイルオブジェクトに再生パラメータを追記する（音量・左右など）
                    }
                    else if (name is "リサイズ" or "拡大率")
                    {
                        currentFilter = FilterType.Scale;
                    }
                    else if (name == "回転")
                    {
                        currentFilter = FilterType.Rotation;

                    }
                    else if (name == "透明度")
                    {
                        currentFilter = FilterType.Opacity;
                    }
                    else if (name == "反転")
                    {
                        currentFilter = FilterType.Reverse;
                    }
                }

                #region [フィルター以外の場合]
                if (currentFilter == FilterType.None)
                {
                    if (line.StartsWith("X="))
                    {
                        var r = ParamParser.Parse(line[2..]);

                        if (currentObject is GroupObject groupObject)
                        {
                            groupObject = groupObjects[^1];
                            groupObject.Position.StartPosition = new(r.Start, groupObject.Position.StartPosition.Y);
                            groupObject.Position.EndPosition = r.HasEnd
                                ? new(r.End, groupObject.Position.EndPosition.Y)
                                : new(groupObject.Position.StartPosition.X, groupObject.Position.StartPosition.Y);
                            groupObject.Position.Easing = r.Easing;
                        }
                        else if (currentObject is ImageObject imageObject)
                        {
                            // file が未指定の場合は中間点なので同じレイヤーの最後のオブジェクトをコピーする
                            if (imageObject.Texture == null)
                            {
                                var sameLayerObjects = imageObjects.Where(obj => obj.Layer == imageObject.Layer && obj.Texture != null).ToList();
                                if (sameLayerObjects.Count > 0)
                                {
                                    var lastObject = sameLayerObjects[^1].Clone();
                                    imageObject.Texture = lastObject.Texture;
                                }
                            }

                            imageObject = imageObjects[^1];
                            imageObject.Position.StartPosition = new(r.Start, imageObject.Position.StartPosition.Y);
                            imageObject.Position.EndPosition = r.HasEnd
                                ? new(r.End, imageObject.Position.EndPosition.Y)
                                : new(imageObject.Position.StartPosition.X, imageObject.Position.StartPosition.Y);
                            imageObject.Position.Easing = r.Easing;
                        }
                    }
                    else if (line.StartsWith("Y="))
                    {
                        var r = ParamParser.Parse(line[2..]);

                        if (currentObject is GroupObject groupObject)
                        {
                            groupObject = groupObjects[^1];
                            groupObject.Position.StartPosition = new(groupObject.Position.StartPosition.X, r.Start);
                            groupObject.Position.EndPosition = r.HasEnd
                                ? new(groupObject.Position.EndPosition.X, r.End)
                                : new(groupObject.Position.StartPosition.X, groupObject.Position.StartPosition.Y);
                            groupObject.Position.Easing = r.Easing;
                        }
                        else if (currentObject is ImageObject imageObject)
                        {
                            imageObject = imageObjects[^1];
                            imageObject.Position.StartPosition = new(imageObject.Position.StartPosition.X, r.Start);
                            imageObject.Position.EndPosition = r.HasEnd
                                ? new(imageObject.Position.EndPosition.X, r.End)
                                : new(imageObject.Position.StartPosition.X, imageObject.Position.StartPosition.Y);
                            imageObject.Position.Easing = r.Easing;
                        }
                    }
                    else if (line.StartsWith("拡大率="))
                    {
                        var r = ParamParser.Parse(line[4..]);

                        if (currentObject is GroupObject groupObject)
                        {
                            groupObject = groupObjects[^1];
                            groupObject.Scale.StartScale = r.Start / 100.0f;
                            groupObject.Scale.EndScale = r.HasEnd ? r.End / 100.0f : groupObject.Scale.StartScale;
                            groupObject.Scale.Easing = r.Easing;
                        }
                        else if (currentObject is ImageObject imageObject)
                        {
                            imageObject = imageObjects[^1];
                            imageObject.Scale.StartScale = r.Start / 100.0f;
                            imageObject.Scale.EndScale = r.HasEnd ? r.End / 100.0f : imageObject.Scale.StartScale;
                            imageObject.Scale.Easing = r.Easing;
                        }
                    }
                    else if (line.StartsWith("上位グループ制御の影響を受ける="))
                    {
                        // "上位グループ制御の影響を受ける=" を削除
                        string str = line[16..];

                        if (currentObject is GroupObject groupObject)
                        {
                            groupObject = groupObjects[^1];
                            groupObject.AffectUpperGroup = int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out int affect) && affect != 0;
                        }
                    }
                    else if (line.StartsWith("range="))
                    {
                        // "range=" を削除
                        string str = line[6..];

                        if (currentObject is GroupObject groupObject)
                        {
                            groupObject = groupObjects[^1];
                            groupObject.Range = int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out int range) ? range : 0;
                        }
                    }
                    else if (line.StartsWith("file="))
                    {
                        // "file=" を削除して、ファイル名を取得
                        string filePart = line[5..];
                        string fileName = Path.GetFileName(filePart);

                        if (currentObject is ImageObject imageObject)
                        {
                            imageObject = imageObjects[^1];

                            // 画像の読み込み（file名→Textureの辞書引きにする。imageObjectsのインデックス対応は
                            // 中間点オブジェクトの分だけずれるため使わない）
                            if (!textureByFileName.TryGetValue(fileName, out var texture))
                            {
                                string candidate = Path.GetDirectoryName(filePath) + @"\" + fileName;
                                // 隣接フォルダに無ければ、exoに書かれた絶対パスをフォールバックとして試す
                                if (!File.Exists(candidate) && File.Exists(filePart))
                                    candidate = filePart;
                                texture = new Texture(candidate);
                                textureByFileName[fileName] = texture;
                            }
                            imageObject.Texture = texture;
                        }
                        else if (currentObject is SoundObject soundObject)
                        {
                            soundObject = soundObjects[^1];
                            string candidate = Path.GetDirectoryName(filePath) + @"\" + fileName;
                            if (!File.Exists(candidate) && File.Exists(filePart))
                                candidate = filePart;
                            soundObject.Sound = new Sound(candidate);
                        }
                    }
                    else if (line.StartsWith("透明度="))
                    {
                        var r = ParamParser.Parse(line[4..]);

                        if (currentObject is ImageObject imageObject)
                        {
                            imageObject = imageObjects[^1];
                            imageObject.Opacity.StartOpacity = 1 - r.Start / 100.0f;
                            imageObject.Opacity.EndOpacity = r.HasEnd ? 1 - r.End / 100.0f : imageObject.Opacity.StartOpacity;
                            imageObject.Opacity.Easing = r.Easing;
                        }
                    }
                    else if (line.StartsWith("回転="))
                    {
                        var r = ParamParser.Parse(line[3..]);

                        if (currentObject is ImageObject imageObject)
                        {
                            imageObject = imageObjects[^1];
                            imageObject.Rotation.StartRotation = r.Start;
                            imageObject.Rotation.EndRotation = r.HasEnd ? r.End : imageObject.Rotation.StartRotation;
                            imageObject.Rotation.Easing = r.Easing;
                        }
                    }
                    else if (line.StartsWith("合成モード="))
                    {
                        if (currentObject is ImageObject imageObject)
                        {
                            imageObject = imageObjects[^1];
                            imageObject.BlendMode = BlendModeMap.Parse(Parse(line));
                        }
                    }
                    else if (line.StartsWith("音量="))
                    {
                        var r = ParamParser.Parse(line[3..]);

                        if (currentSoundObject != null)
                        {
                            currentSoundObject.StartVolume = r.Start / 100.0f;
                            currentSoundObject.EndVolume = r.HasEnd ? r.End / 100.0f : currentSoundObject.StartVolume;
                        }
                    }
                    else if (line.StartsWith("左右="))
                    {
                        if (currentSoundObject != null && float.TryParse(line[3..].Split(',')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float pan))
                            currentSoundObject.Pan = pan / 100.0f;
                    }
                    else if (line.StartsWith("ループ再生="))
                    {
                        if (currentSoundObject != null)
                            currentSoundObject.Loop = ParseInt(line) != 0;
                    }
                }
                #endregion

                #region [リサイズフィルター]
                else if (currentFilter == FilterType.Scale)
                {
                    if (line.StartsWith("拡大率="))
                    {
                        ScaleFilter scaleFilter = new();
                        currentObject.Filters.Add(scaleFilter);

                        var r = ParamParser.Parse(line[4..]);

                        var filterObject = (ScaleFilter)currentObject.Filters[^1];
                        filterObject.StartBaseScale = r.Start / 100.0f;
                        filterObject.EndBaseScale = r.HasEnd ? r.End / 100.0f : filterObject.StartBaseScale;
                    }
                    else if (line.StartsWith("X="))
                    {
                        var r = ParamParser.Parse(line[2..]);

                        // 「拡大率=が必ず先に来る」前提でFilters.Last()を無防備にキャストすると、
                        // 順序が違う.exoやリストが空の場合に例外になる。無ければここで生成して追加する。
                        var filterObject = currentObject.Filters.OfType<ScaleFilter>().LastOrDefault();
                        if (filterObject == null)
                        {
                            filterObject = new ScaleFilter();
                            currentObject.Filters.Add(filterObject);
                        }

                        filterObject.StartScale = new(r.Start / 100.0f, filterObject.StartScale.Height);
                        filterObject.EndScale = r.HasEnd
                            ? new(r.End / 100.0f, filterObject.EndScale.Height)
                            : new(filterObject.StartScale.Width, filterObject.StartScale.Height);
                    }
                    else if (line.StartsWith("Y="))
                    {
                        var r = ParamParser.Parse(line[2..]);

                        var filterObject = currentObject.Filters.OfType<ScaleFilter>().LastOrDefault();
                        if (filterObject == null)
                        {
                            filterObject = new ScaleFilter();
                            currentObject.Filters.Add(filterObject);
                        }

                        filterObject.StartScale = new(filterObject.StartScale.Width, r.Start / 100.0f);
                        filterObject.EndScale = r.HasEnd
                            ? new(filterObject.EndScale.Width, r.End / 100.0f)
                            : new(filterObject.StartScale.Width, filterObject.StartScale.Height);

                        // フィルターの終了
                        currentFilter = FilterType.None;
                    }
                }

                #endregion
                #region [回転フィルター]
                else if (currentFilter == FilterType.Rotation)
                {
                    if (line.StartsWith("Z="))
                    {
                        // フィルターの作成
                        RotationFilter rotationFilter = new();
                        currentObject.Filters.Add(rotationFilter);

                        var r = ParamParser.Parse(line[2..]);

                        var filterObject = (RotationFilter)currentObject.Filters[^1];

                        filterObject.Rotation.StartRotation = r.Start;
                        filterObject.Rotation.EndRotation = r.HasEnd ? r.End : filterObject.Rotation.StartRotation;

                        // フィルターの終了
                        currentFilter = FilterType.None;
                    }
                }

                #endregion
                #region [透明度フィルター]
                else if (currentFilter == FilterType.Opacity)
                {
                    if (line.StartsWith("透明度="))
                    {
                        // フィルターの作成
                        OpacityFilter opacityFilter = new();
                        currentObject.Filters.Add(opacityFilter);

                        var r = ParamParser.Parse(line[4..]);

                        var filterObject = (OpacityFilter)currentObject.Filters[^1];

                        filterObject.Opacity.StartOpacity = 1 - r.Start / 100.0f;
                        filterObject.Opacity.EndOpacity = r.HasEnd ? 1 - r.End / 100.0f : filterObject.Opacity.StartOpacity;

                        // フィルターの終了
                        currentFilter = FilterType.None;
                    }
                }

                #endregion
                #region [反転フィルター]
                else if (currentFilter == FilterType.Reverse)
                {
                    if (line.StartsWith("上下反転="))
                    {
                        // フィルターの作成
                        ReverseFilter reverseFilter = new();
                        currentObject.Filters.Add(reverseFilter);

                        var filterObject = (ReverseFilter)currentObject.Filters[^1];

                        filterObject.ReverseY = Convert.ToBoolean(ParseInt(line));
                    }
                    else if (line.StartsWith("左右反転="))
                    {
                        var filterObject = (ReverseFilter)currentObject.Filters[^1];

                        filterObject.ReverseX = Convert.ToBoolean(ParseInt(line));

                        // フィルターの終了
                        currentFilter = FilterType.None;
                    }
                }

                #endregion

            }

            #endregion
        }

        // 尺の未指定時は全画像オブジェクトの最大終端フレームから算出する。
        // 読み込み完了後に1回だけ計算する（旧実装は行ループの内側にあり、1行読むたびに再計算していた）。
        if (doc.Length == 0)
        {
            doc.Length = imageObjects.Count > 0 ? imageObjects.Max(obj => obj.EndFrame) : 0;
        }

        return doc;
    }
}
