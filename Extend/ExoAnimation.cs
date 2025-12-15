namespace AstrumLoom.Extend;

public class Exo
{
    public string FilePath { get; set; } = "";
    /// <summary>
    /// exeditのプロパティ
    /// </summary>
    public int Width { get; set; }
    public int Height { get; set; }
    private int Rate { get; set; }
    private int Scale { get; set; }
    private int Length { get; set; }
    private bool IsLoop { get; set; }
    private bool _isPlaying { get; set; }

    private List<ImageObject> imageObjects = []; // 画像オブジェクトのリスト
    private List<GroupObject> groupObjects = []; // グループ制御オブジェクトのリスト
    private List<string> textureFileNames = []; // テクスチャ名のリスト

    public Counter counter = new();

    public bool Enable => imageObjects.Count > 0;

    /// <summary>
    /// exoファイルを読み込む
    /// </summary>
    /// <param name="exoPath">exoのファイルパス</param>
    /// <param name="isLoop">ループするかどうか</param>
    /// <param name="isUseAntialiasing">アンチエイリアスをかけるかどうか</param>
    public Exo(string exoPath, bool isLoop = false, bool isUseAntialiasing = false)
    {
        FilePath = exoPath;
        IsLoop = isLoop;
        Object? currentObject = null;
        var currentFilter = FilterType.None;
        IEnumerable<string> lines = Text.Read(exoPath);

        foreach (string line in lines)
        {
            #region [exeditのパース] 
            if (currentObject == null)
            {
                if (line.StartsWith("width="))
                {
                    Width = int.Parse(line.Split('=')[1]);
                }
                else if (line.StartsWith("height="))
                {
                    Height = int.Parse(line.Split('=')[1]);
                }
                else if (line.StartsWith("rate="))
                {
                    Rate = int.Parse(line.Split('=')[1]);
                }
                else if (line.StartsWith("scale="))
                {
                    Scale = int.Parse(line.Split('=')[1]);
                }
                else if (line.StartsWith("length="))
                {
                    Length = int.Parse(line.Split('=')[1]);
                }
            }
            #endregion

            #region [ オブジェクトのパース]

            // [0]のような小数点なしの行
            if (line.StartsWith("[") && line.Contains("]") && !line.StartsWith("[exedit]") && !line.Contains("."))
            {
                string indexString = line.Trim('[', ']');
                if (int.TryParse(indexString, out int index))
                {
                    // オブジェクトの作成
                    Object exoObject = new();
                    currentObject = exoObject;
                }
            }

            if (currentObject != null)
            {
                // [0]のような小数点なしの行
                if (line.StartsWith("start="))
                {
                    currentObject.StartFrame = int.Parse(line.Split('=')[1]);
                }
                else if (line.StartsWith("end="))
                {
                    currentObject.EndFrame = int.Parse(line.Split('=')[1]);
                }
                else if (line.StartsWith("layer="))
                {
                    currentObject.Layer = int.Parse(line.Split('=')[1]);
                }

                if (line.StartsWith("_name="))
                {
                    if (line.Split('=')[1] == "グループ制御")
                    {
                        currentFilter = FilterType.None;
                        GroupObject groupObject = new(currentObject);
                        currentObject = groupObject;

                        groupObjects.Add(groupObject);
                    }
                    else if (line.Split('=')[1] == "画像ファイル")
                    {
                        currentFilter = FilterType.None;
                        ImageObject imageObject = new(currentObject);
                        currentObject = imageObject;

                        imageObjects.Add(imageObject);
                    }
                    else if (line.Split('=')[1] is "リサイズ" or "拡大率")
                    {
                        currentFilter = FilterType.Scale;
                    }
                    else if (line.Split('=')[1] == "回転")
                    {
                        currentFilter = FilterType.Rotation;

                    }
                    else if (line.Split('=')[1] == "透明度")
                    {
                        currentFilter = FilterType.Opacity;
                    }
                    else if (line.Split('=')[1] == "反転")
                    {
                        currentFilter = FilterType.Reverse;
                    }
                }

                #region [フィルター以外の場合]
                if (currentFilter == FilterType.None)
                {
                    if (line.StartsWith("X="))
                    {
                        // "X=" を削除し、カンマで分割
                        string[] parts = line[2..].Split(',');

                        // 3番目に@でプラグイン名が入る場合があるので、それを除外
                        // X=-400.0,-400.0,15@easing_normal@uf_easing,1
                        // 15 と easing_normal@uf_easing を分離する など
                        string plaginPart = parts.Length > 2 ? parts[2] : "";
                        if (plaginPart.Contains("@"))
                        {
                            plaginPart = plaginPart.Split('@', 2)[1];
                            parts[2] = parts[2].Split('@', 2)[0];
                        }
                        else plaginPart = "";

                        // 数値に変換
                        float[] numbers = parts.Select(float.Parse).ToArray();

                        if (currentObject is GroupObject groupObject)
                        {
                            groupObject = groupObjects.Last();
                            groupObject.Position.StartPosition = new(numbers[0], groupObject.Position.StartPosition.Y);

                            // EndPositionがある場合は設定
                            groupObject.Position.EndPosition = line.Contains(",")
                                ? new(numbers[1], groupObject.Position.EndPosition.Y)
                                // EndPositionがない場合はStartPositionと同じにする
                                : new(groupObject.Position.StartPosition.X, groupObject.Position.StartPosition.Y);
                            if (!string.IsNullOrEmpty(plaginPart))
                            {
                                // easing_normal@uf_easing
                                groupObject.Position.Easing = plaginPart.StartsWith("easing_normal") ? (UfEasing)numbers[3] : numbers.Length > 3 ? (UfEasing)numbers[3] : UfEasing.Linear;
                            }
                        }
                        else if (currentObject is ImageObject imageObject)
                        {
                            // file が未指定の場合は中間点なので同じレイヤーの最後のオブジェクトをコピーする
                            if (imageObject.Texture == null)
                            {
                                var sameLayerObjects = imageObjects.Where(obj => obj.Layer == imageObject.Layer && obj.Texture != null).ToList();
                                if (sameLayerObjects.Count > 0)
                                {
                                    var lastObject = sameLayerObjects.Last().Clone();
                                    imageObject.Texture = lastObject.Texture;
                                }
                            }

                            imageObject = imageObjects.Last();
                            imageObject.Position.StartPosition = new(numbers[0], imageObject.Position.StartPosition.Y);

                            // EndPositionがある場合は設定
                            imageObject.Position.EndPosition = line.Contains(",")
                                ? new(numbers[1], imageObject.Position.EndPosition.Y)
                                // EndPositionがない場合はStartPositionと同じにする
                                : new(imageObject.Position.StartPosition.X, imageObject.Position.StartPosition.Y);
                            if (!string.IsNullOrEmpty(plaginPart))
                            {
                                // easing_normal@uf_easing
                                imageObject.Position.Easing = plaginPart.StartsWith("easing_normal") ? (UfEasing)numbers[3] : numbers.Length > 3 ? (UfEasing)numbers[3] : UfEasing.Linear;
                            }
                        }
                    }
                    else if (line.StartsWith("Y="))
                    {
                        // "Y=" を削除し、カンマで分割
                        string[] parts = line[2..].Split(',');

                        // 3番目に@でプラグイン名が入る場合があるので、それを除外
                        // X=-400.0,-400.0,15@easing_normal@uf_easing,1
                        // 15 と easing_normal@uf_easing を分離する など
                        string plaginPart = parts.Length > 2 ? parts[2] : "";
                        if (plaginPart.Contains("@"))
                        {
                            plaginPart = plaginPart.Split('@', 2)[1];
                            parts[2] = parts[2].Split('@', 2)[0];
                        }

                        // 数値に変換
                        float[] numbers = parts.Select(float.Parse).ToArray();

                        if (currentObject is GroupObject groupObject)
                        {
                            groupObject = groupObjects.Last();
                            groupObject.Position.StartPosition = new(groupObject.Position.StartPosition.X, numbers[0]);

                            // EndPositionがある場合は設定
                            groupObject.Position.EndPosition = line.Contains(",")
                                ? new(groupObject.Position.EndPosition.X, numbers[1])
                                // EndPositionがない場合はStartPositionと同じにする
                                : new(groupObject.Position.StartPosition.X, groupObject.Position.StartPosition.Y);
                            if (!string.IsNullOrEmpty(plaginPart))
                            {
                                // easing_normal@uf_easing
                                groupObject.Position.Easing = plaginPart.StartsWith("easing_normal") ? (UfEasing)numbers[3] : numbers.Length > 3 ? (UfEasing)numbers[3] : UfEasing.Linear;
                            }
                        }
                        else if (currentObject is ImageObject imageObject)
                        {
                            imageObject = imageObjects.Last();
                            imageObject.Position.StartPosition = new(imageObject.Position.StartPosition.X, numbers[0]);

                            // EndPositionがある場合は設定
                            imageObject.Position.EndPosition = line.Contains(",")
                                ? new(imageObject.Position.EndPosition.X, numbers[1])
                                // EndPositionがない場合はStartPositionと同じにする
                                : new(imageObject.Position.StartPosition.X, imageObject.Position.StartPosition.Y);
                            if (!string.IsNullOrEmpty(plaginPart))
                            {
                                // easing_normal@uf_easing
                                imageObject.Position.Easing = plaginPart.StartsWith("easing_normal") ? (UfEasing)numbers[3] : numbers.Length > 3 ? (UfEasing)numbers[3] : UfEasing.Linear;
                            }
                        }
                    }
                    else if (line.StartsWith("拡大率="))
                    {
                        // "拡大率=" を削除し、カンマで分割
                        string[] parts = line[4..].Split(',');

                        // 3番目に@でプラグイン名が入る場合があるので、それを除外
                        // X=-400.0,-400.0,15@easing_normal@uf_easing,1
                        // 15 と easing_normal@uf_easing を分離する など
                        string plaginPart = parts.Length > 2 ? parts[2] : "";
                        if (plaginPart.Contains("@"))
                        {
                            plaginPart = plaginPart.Split('@', 2)[1];
                            parts[2] = parts[2].Split('@', 2)[0];
                        }

                        // 数値に変換
                        float[] numbers = parts.Select(float.Parse).ToArray();

                        if (currentObject is GroupObject groupObject)
                        {
                            groupObject = groupObjects.Last();
                            groupObject.Scale.StartScale = numbers[0] / 100.0f;

                            // EndScaleがある場合は設定
                            groupObject.Scale.EndScale = line.Contains(",")
                                ? numbers[1] / 100.0f
                                // EndScaleがない場合はStartScaleと同じにする
                                : groupObject.Scale.StartScale;
                            if (!string.IsNullOrEmpty(plaginPart))
                            {
                                // easing_normal@uf_easing
                                groupObject.Position.Easing = plaginPart.StartsWith("easing_normal") ? (UfEasing)numbers[3] : numbers.Length > 3 ? (UfEasing)numbers[3] : UfEasing.Linear;
                            }
                        }
                        else if (currentObject is ImageObject imageObject)
                        {
                            imageObject = imageObjects.Last();
                            imageObject.Scale.StartScale = numbers[0] / 100.0f;

                            // EndScaleがある場合は設定
                            imageObject.Scale.EndScale = line.Contains(",")
                                ? numbers[1] / 100.0f
                                // EndScaleがない場合はStartScaleと同じにする
                                : imageObject.Scale.StartScale;
                            if (!string.IsNullOrEmpty(plaginPart))
                            {
                                // easing_normal@uf_easing
                                imageObject.Position.Easing = plaginPart.StartsWith("easing_normal") ? (UfEasing)numbers[3] : numbers.Length > 3 ? (UfEasing)numbers[3] : UfEasing.Linear;
                            }
                        }
                    }
                    else if (line.StartsWith("上位グループ制御の影響を受ける="))
                    {
                        // "上位グループ制御の影響を受ける=" を削除
                        string str = line[16..];

                        if (currentObject is GroupObject groupObject)
                        {
                            groupObject = groupObjects.Last();
                            groupObject.AffectUpperGroup = Convert.ToBoolean(int.Parse(str));
                        }
                    }
                    else if (line.StartsWith("range="))
                    {
                        // "range=" を削除
                        string str = line[6..];

                        if (currentObject is GroupObject groupObject)
                        {
                            groupObject = groupObjects.Last();
                            groupObject.Range = int.Parse(str);
                        }
                    }
                    else if (line.StartsWith("file="))
                    {
                        // "file=" を削除して、ファイル名を取得
                        string fileName = Path.GetFileName(line[5..]);

                        if (currentObject is ImageObject imageObject)
                        {
                            imageObject = imageObjects.Last();

                            // 画像の読み込み
                            imageObject.Texture = !textureFileNames.Contains(fileName)
                                ? new Texture(Path.GetDirectoryName(exoPath) + @"\" + fileName)
                                : imageObjects[textureFileNames.IndexOf(fileName)].Texture;
                            /*
                            // アンチエイリアスをかける
                            if (isUseAntialiasing)
                                DX.Filter(imageObject.Texture.RayTexture, TextureFilter.Bilinear); // テクスチャフィルターを設定
                            */

                            textureFileNames.Add(fileName);
                        }
                    }
                    else if (line.StartsWith("透明度="))
                    {
                        // "透明度=" を削除し、カンマで分割
                        string[] parts = line[4..].Split(',');

                        // 3番目に@でプラグイン名が入る場合があるので、それを除外
                        // X=-400.0,-400.0,15@easing_normal@uf_easing,1
                        // 15 と easing_normal@uf_easing を分離する など
                        string plaginPart = parts.Length > 2 ? parts[2] : "";
                        if (plaginPart.Contains("@"))
                        {
                            plaginPart = plaginPart.Split('@', 2)[1];
                            parts[2] = parts[2].Split('@', 2)[0];
                        }

                        // 数値に変換
                        float[] numbers = parts.Select(float.Parse).ToArray();

                        if (currentObject is ImageObject imageObject)
                        {
                            imageObject = imageObjects.Last();
                            imageObject.Opacity.StartOpacity = 1 - numbers[0] / 100.0f;

                            // EndOpacityがある場合は設定
                            imageObject.Opacity.EndOpacity = line.Contains(",")
                                ? 1 - numbers[1] / 100.0f
                                // EndOpacityがない場合はStartOpacityと同じにする
                                : imageObject.Opacity.StartOpacity;
                            if (!string.IsNullOrEmpty(plaginPart))
                            {
                                // easing_normal@uf_easing
                                imageObject.Position.Easing = plaginPart.StartsWith("easing_normal") ? (UfEasing)numbers[3] : numbers.Length > 3 ? (UfEasing)numbers[3] : UfEasing.Linear;
                            }
                        }
                    }
                    else if (line.StartsWith("回転="))
                    {
                        // "回転=" を削除し、カンマで分割
                        string[] parts = line[3..].Split(',');

                        // 3番目に@でプラグイン名が入る場合があるので、それを除外
                        // X=-400.0,-400.0,15@easing_normal@uf_easing,1
                        // 15 と easing_normal@uf_easing を分離する など
                        string plaginPart = parts.Length > 2 ? parts[2] : "";
                        if (plaginPart.Contains("@"))
                        {
                            plaginPart = plaginPart.Split('@', 2)[1];
                            parts[2] = parts[2].Split('@', 2)[0];
                        }

                        // 数値に変換
                        float[] numbers = parts.Select(float.Parse).ToArray();

                        if (currentObject is ImageObject imageObject)
                        {
                            imageObject = imageObjects.Last();
                            imageObject.Rotation.StartRotation = numbers[0];

                            // EndPositionがある場合は設定
                            imageObject.Rotation.EndRotation = line.Contains(",")
                                ? numbers[1]
                                // EndPositionがない場合はStartPositionと同じにする
                                : imageObject.Rotation.StartRotation;
                            if (!string.IsNullOrEmpty(plaginPart))
                            {
                                // easing_normal@uf_easing
                                imageObject.Position.Easing = plaginPart.StartsWith("easing_normal") ? (UfEasing)numbers[3] : numbers.Length > 3 ? (UfEasing)numbers[3] : UfEasing.Linear;
                            }
                        }
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

                        // "拡大率=" を削除し、カンマで分割
                        string[] parts = line[4..].Split(',');

                        // 数値に変換
                        float[] numbers = parts.Select(float.Parse).ToArray();

                        var FilterObject = (ScaleFilter)currentObject.Filters.Last();
                        FilterObject.StartBaseScale = numbers[0] / 100.0f;

                        // EndScaleがある場合は設定
                        FilterObject.EndBaseScale = line.Contains(",")
                            ? numbers[1] / 100.0f
                            // EndScaleがない場合はStartScaleと同じにする
                            : FilterObject.StartBaseScale;
                    }
                    else if (line.StartsWith("X="))
                    {
                        // "X=" を削除し、カンマで分割
                        string[] parts = line[2..].Split(',');

                        // 数値に変換
                        float[] numbers = parts.Select(float.Parse).ToArray();

                        var FilterObject = (ScaleFilter)currentObject.Filters.Last();

                        FilterObject.StartScale = new(numbers[0] / 100.0f, FilterObject.StartScale.Height);

                        // EndScaleがある場合は設定
                        FilterObject.EndScale = line.Contains(",")
                            ? new(numbers[1] / 100.0f, FilterObject.EndScale.Height)
                            // EndScaleがない場合はStartScaleと同じにする
                            : new(FilterObject.StartScale.Width, FilterObject.StartScale.Height);
                    }
                    else if (line.StartsWith("Y="))
                    {
                        // "Y=" を削除し、カンマで分割
                        string[] parts = line[2..].Split(',');

                        // 数値に変換
                        float[] numbers = parts.Select(float.Parse).ToArray();

                        var FilterObject = (ScaleFilter)currentObject.Filters.Last();

                        FilterObject.StartScale = new(FilterObject.StartScale.Width, numbers[0] / 100.0f);

                        // EndScaleがある場合は設定
                        FilterObject.EndScale = line.Contains(",")
                            ? new(FilterObject.EndScale.Width, numbers[1] / 100.0f)
                            // EndScaleがない場合はStartScaleと同じにする
                            : new(FilterObject.StartScale.Width, FilterObject.StartScale.Height);

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

                        // "Z=" を削除し、カンマで分割
                        string[] parts = line[2..].Split(',');

                        // 数値に変換
                        float[] numbers = parts.Select(float.Parse).ToArray();

                        var FilterObject = (RotationFilter)currentObject.Filters.Last();

                        FilterObject.Rotation.StartRotation = numbers[0];

                        // EndRotationがある場合は設定
                        FilterObject.Rotation.EndRotation = line.Contains(",")
                            ? numbers[1]
                            // EndRotationがない場合はStartRotationと同じにする
                            : FilterObject.Rotation.StartRotation;

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

                        // "透明度=" を削除し、カンマで分割
                        string[] parts = line[4..].Split(',');

                        // 数値に変換
                        float[] numbers = parts.Select(float.Parse).ToArray();

                        var FilterObject = (OpacityFilter)currentObject.Filters.Last();

                        FilterObject.Opacity.StartOpacity = 1 - numbers[0] / 100.0f;

                        // EndOpacityがある場合は設定
                        FilterObject.Opacity.EndOpacity = line.Contains(",")
                            ? 1 - numbers[1] / 100.0f
                            // EndOpacityがない場合はStartOpacityと同じにする
                            : FilterObject.Opacity.StartOpacity;

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

                        var FilterObject = (ReverseFilter)currentObject.Filters.Last();

                        FilterObject.ReverseY = Convert.ToBoolean(int.Parse(line.Split('=')[1]));
                    }
                    else if (line.StartsWith("左右反転="))
                    {
                        var FilterObject = (ReverseFilter)currentObject.Filters.Last();

                        FilterObject.ReverseX = Convert.ToBoolean(int.Parse(line.Split('=')[1]));

                        // フィルターの終了
                        currentFilter = FilterType.None;
                    }
                }

                #endregion

            }

            #endregion
        }

        #region [画像オブジェクトとグループ制御オブジェクトの関連付け]
        foreach (var imageObject in imageObjects)
        {
            foreach (var groupObject in groupObjects)
            {
                // グループ制御の適応範囲内の場合
                if (imageObject.Layer <= groupObject.Layer + groupObject.Range || groupObject.Range == 0)
                {
                    // グループ制御のレイヤーが画像オブジェクトのレイヤーより下の場合はスキップ
                    if (imageObject.Layer < groupObject.Layer)
                        continue;

                    // グループ制御のフレーム内に画像オブジェクトがない場合はスキップ
                    if (groupObject.StartFrame > imageObject.EndFrame || groupObject.EndFrame < imageObject.StartFrame)
                        continue;

                    imageObject.GroupObjects.Add(groupObject);
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// カウンターをスタートする
    /// </summary>
    public void Start()
    {
        counter = new Counter(1, Length, (int)(1000.0 * (1000.0 / Rate)), IsLoop);
        counter.Start();
        _isPlaying = true;
    }

    /// <summary>
    /// カウンターをストップする
    /// </summary>
    public void Stop()
    {
        if (counter != null)
        {
            counter.Stop();
            _isPlaying = false;
        }
    }

    /// <summary>
    /// カウンターを再開する
    /// </summary>
    public void Resume()
    {
        if (counter != null)
        {
            counter.Start();
            _isPlaying = true;
        }
    }

    /// <summary>
    /// 再生中かどうか
    /// </summary>
    /// <returns></returns>
    public bool IsPlaying() => _isPlaying;

    /// <summary>
    /// 現在何フレーム目かを取得する
    /// </summary>
    /// <returns></returns>
    public int GetNowFrame() => (int)counter.Value;

    public double Time => counter == null ? 0 : counter.Value * Rate;

    public double EndTime => Length * Rate;

    /// <summary>
    /// を描画する関数
    /// </summary>
    /// <param name="offsetX">OffsetX</param>
    /// <param name="offsetY">OffsetY</param>
    public void Draw(float offsetX, float offsetY, ReferencePoint point = ReferencePoint.Center, float scale = 1)
    {
        if (!Loaded()) return;
        if (counter != null)
        {
            counter.Tick();

            switch (point)
            {
                case ReferencePoint.TopLeft:
                    offsetX += Width / 2;
                    offsetY += Height / 2;
                    break;
                case ReferencePoint.TopCenter:
                    offsetY += Height / 2;
                    break;
                case ReferencePoint.TopRight:
                    offsetX -= Width / 2;
                    offsetY += Height / 2;
                    break;
                case ReferencePoint.CenterLeft:
                    offsetX += Width / 2;
                    break;
                case ReferencePoint.CenterRight:
                    offsetX -= Width / 2;
                    break;
                case ReferencePoint.BottomLeft:
                    offsetX += Width / 2;
                    offsetY -= Height / 2;
                    break;
                case ReferencePoint.BottomCenter:
                    offsetY -= Height / 2;
                    break;
                case ReferencePoint.BottomRight:
                    offsetX -= Width / 2;
                    offsetY -= Height / 2;
                    break;
            }
            foreach (var imageObject in imageObjects)
            {
                if (counter.Value >= imageObject.StartFrame && counter.Value <= imageObject.EndFrame)
                {
                    UpdateTransform(imageObject); // Transformを更新
                    ApplyFilter(imageObject); // フィルターを適用
                    ApplyGroupObject(imageObject); // グループ制御オブジェクトを適用
                    imageObject.Draw(offsetX, offsetY, scale);
                }
            }
        }
    }

    public bool Loaded()
    {
        int loadedCount = 0;
        foreach (var imageObject in imageObjects)
        {
            var texture = imageObject.Texture;
            if (texture == null || texture.Loaded)
            { loadedCount++; continue; }

            texture.Pump();

            if (texture.Loaded)
                loadedCount++;
        }
        return loadedCount == imageObjects.Count;
    }

    public override string ToString() => $"Exo: {Width}x{Height} Rate={Rate} Scale={Scale} Length={Length} Loop={IsLoop} Objects={imageObjects.Count} Groups={groupObjects.Count} Path:{FilePath}";

    #region [Private]

    /// <summary>
    /// 画像オブジェクトのTransformを更新する関数
    /// </summary>
    /// <param name="imageObject"></param>
    private void UpdateTransform(ImageObject imageObject)
    {
        // 0.0～1.0の進行度
        float t = (float)(counter.Value - imageObject.StartFrame) / (imageObject.EndFrame - imageObject.StartFrame);

        // StartFrameとEndFrameが同じ場合(1フレームの場合)1.0固定
        if (imageObject.StartFrame == imageObject.EndFrame)
        {
            t = 0.0f;
        }

        // 補間を行う
        var interpolatedPosition = imageObject.Position.StartPosition + (imageObject.Position.EndPosition - imageObject.Position.StartPosition) * AnimationEasing.Get(imageObject.Position.Easing, t);
        float interpolatedScale = imageObject.Scale.StartScale + (imageObject.Scale.EndScale - imageObject.Scale.StartScale) * AnimationEasing.Get(imageObject.Scale.Easing, t);
        float interpolatedRotation = imageObject.Rotation.StartRotation + (imageObject.Rotation.EndRotation - imageObject.Rotation.StartRotation) * AnimationEasing.Get(imageObject.Rotation.Easing, t);
        float interpolatedOpacity = imageObject.Opacity.StartOpacity + (imageObject.Opacity.EndOpacity - imageObject.Opacity.StartOpacity) * AnimationEasing.Get(imageObject.Opacity.Easing, t);

        // Transformの更新
        imageObject.Transfrom.Position = interpolatedPosition;
        imageObject.Transfrom.Scale = new(interpolatedScale, interpolatedScale);
        imageObject.Transfrom.Rotation = interpolatedRotation;
        imageObject.Transfrom.Opacity = interpolatedOpacity;
        imageObject.Transfrom.ReverseX = false; // 画像オブジェクトは反転がないので、初期値false
        imageObject.Transfrom.ReverseY = false; // 画像オブジェクトは反転がないので、初期値false
    }

    /// <summary>
    /// グループ制御オブジェクトを適用する関数
    /// </summary>
    /// <param name="imageObject"></param>
    private void ApplyGroupObject(ImageObject imageObject)
    {
        List<GroupObject> nowFrameGroupObjects = []; // 今のフレームに存在するグループ制御オブジェクトのリスト

        // 今のフレームに存在するグループ制御オブジェクトをリストに追加
        foreach (var groupObject in imageObject.GroupObjects)
        {
            if (counter.Value >= groupObject.StartFrame && counter.Value <= groupObject.EndFrame)
            {
                nowFrameGroupObjects.Add(groupObject);
            }
        }

        // 今のフレームに存在するグループ制御オブジェクトを適用
        foreach (var nowFrameGroupObject in Enumerable.Reverse(nowFrameGroupObjects))
        {
            //Console.WriteLine(nowFrameGroupObjects.Count);

            if (imageObject.IsAffectUpperGroup || nowFrameGroupObject == nowFrameGroupObjects.Last())
            {
                // 0.0～1.0の進行度
                float t = (float)(counter.Value - nowFrameGroupObject.StartFrame) / (nowFrameGroupObject.EndFrame - nowFrameGroupObject.StartFrame);

                // StartFrameとEndFrameが同じ場合(1フレームの場合)1.0固定
                if (nowFrameGroupObject.StartFrame == nowFrameGroupObject.EndFrame)
                {
                    t = 0.0f;
                }

                // 補間を行う
                var interpolatedPosition = nowFrameGroupObject.Position.StartPosition + (nowFrameGroupObject.Position.EndPosition - nowFrameGroupObject.Position.StartPosition) * AnimationEasing.Get(nowFrameGroupObject.Position.Easing, t);
                float interpolatedScale = nowFrameGroupObject.Scale.StartScale + (nowFrameGroupObject.Scale.EndScale - nowFrameGroupObject.Scale.StartScale) * AnimationEasing.Get(nowFrameGroupObject.Scale.Easing, t);
                float interpolatedRotation = nowFrameGroupObject.Rotation.StartRotation + (nowFrameGroupObject.Rotation.EndRotation - nowFrameGroupObject.Rotation.StartRotation) * AnimationEasing.Get(nowFrameGroupObject.Rotation.Easing, t);

                // グループ制御オブジェクトのTransformの更新
                nowFrameGroupObject.Transfrom.Position = interpolatedPosition;
                nowFrameGroupObject.Transfrom.Scale = new(interpolatedScale, interpolatedScale);
                nowFrameGroupObject.Transfrom.Rotation = interpolatedRotation;
                nowFrameGroupObject.Transfrom.Opacity = 1.0f; // グループ制御は透明度がないので、初期値1.0
                nowFrameGroupObject.Transfrom.ReverseX = false; // グループ制御は反転がないので、初期値false
                nowFrameGroupObject.Transfrom.ReverseY = false; // グループ制御は反転がないので、初期値false

                // グループ制御オブジェクトのフィルターを適用
                ApplyFilter(nowFrameGroupObject);

                // 画像オブジェクトのTransformにグループ制御オブジェクトのTransformを適用
                imageObject.Transfrom.Position += nowFrameGroupObject.Transfrom.Position;
                imageObject.Transfrom.Position *= interpolatedScale; // グループ制御の拡大率で補正
                imageObject.Transfrom.Scale *= nowFrameGroupObject.Transfrom.Scale;
                imageObject.Transfrom.Rotation += nowFrameGroupObject.Transfrom.Rotation;
                imageObject.Transfrom.Opacity *= nowFrameGroupObject.Transfrom.Opacity;

                // 反転の適用
                if (nowFrameGroupObject.Transfrom.ReverseX) imageObject.Transfrom.ReverseX = !imageObject.Transfrom.ReverseX;
                if (nowFrameGroupObject.Transfrom.ReverseY) imageObject.Transfrom.ReverseY = !imageObject.Transfrom.ReverseY;

                imageObject.IsAffectUpperGroup = nowFrameGroupObject.AffectUpperGroup;
            }
        }
    }

    /// <summary>
    /// フィルターを適用する関数
    /// </summary>
    /// <param name="exoObject"></param>
    private void ApplyFilter(Object exoObject)
    {
        foreach (var filter in exoObject.Filters)
        {
            // 0.0～1.0の進行度
            float t = (float)(counter.Value - exoObject.StartFrame) / (exoObject.EndFrame - exoObject.StartFrame);

            // StartFrameとEndFrameが同じ場合(1フレームの場合)1.0固定
            if (exoObject.StartFrame == exoObject.EndFrame)
            {
                t = 0.0f;
            }

            // リサイズフィルター
            if (filter is ScaleFilter scaleFilter)
            {
                // 補間を行う
                float interpolatedBaseScale = scaleFilter.StartBaseScale + (scaleFilter.EndBaseScale - scaleFilter.StartBaseScale) * t;
                var interpolatedScale = scaleFilter.StartScale + (scaleFilter.EndScale - scaleFilter.StartScale) * t;

                // Transformの更新
                exoObject.Transfrom.Scale *= interpolatedBaseScale;
                exoObject.Transfrom.Scale *= interpolatedScale;
            }
            // 回転フィルター
            else if (filter is RotationFilter rotationFilter)
            {
                // 補間を行う
                float interpolatedRotation = rotationFilter.Rotation.StartRotation + (rotationFilter.Rotation.EndRotation - rotationFilter.Rotation.StartRotation) * t;

                // Transformの更新
                exoObject.Transfrom.Rotation += interpolatedRotation;
            }
            // 透明度フィルター
            else if (filter is OpacityFilter opacityFilter)
            {
                // 補間を行う
                float interpolatedOpacity = opacityFilter.Opacity.StartOpacity + (opacityFilter.Opacity.EndOpacity - opacityFilter.Opacity.StartOpacity) * t;

                // Transformの更新
                exoObject.Transfrom.Opacity *= interpolatedOpacity;
            }
            // 反転フィルター
            else if (filter is ReverseFilter reverseFilter)
            {
                if (reverseFilter.ReverseX) exoObject.Transfrom.ReverseX = !exoObject.Transfrom.ReverseX;
                if (reverseFilter.ReverseY) exoObject.Transfrom.ReverseY = !exoObject.Transfrom.ReverseY;
            }
        }
    }

    #endregion
}