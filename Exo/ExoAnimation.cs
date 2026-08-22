using AstrumLoom.Exo.Loaders;

namespace AstrumLoom.Exo;

/// <summary>
/// AviUtl の exedit プロジェクトファイル（.exo / .aup2）を読み込み、
/// 画像オブジェクト・グループ制御・各種フィルターをタイムライン再生できる形にして描画するクラス。
///
/// パース処理そのものは <see cref="Loaders.IAnimeLoader"/> の実装（<see cref="ExoLoader"/> / <see cref="Aup2Loader"/>）に
/// 委譲し、このクラスは「読み込んだ結果（<see cref="AnimeDocument"/>）を再生・描画する」ことだけに専念する。
/// </summary>
public class ExoAnimation
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
    private List<SoundObject> soundObjects = []; // サウンドオブジェクトのリスト

    public Counter counter = new();

    public bool Enable => imageObjects.Count > 0;

    /// <summary>
    /// exo/aup2ファイルを読み込む
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <param name="isLoop">ループするかどうか</param>
    /// <param name="isUseAntialiasing">アンチエイリアスをかけるかどうか</param>
    public ExoAnimation(string filePath, bool isLoop = false, bool isUseAntialiasing = false)
    {
        FilePath = filePath;
        IsLoop = isLoop;

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        // 拡張子に応じたローダを選び、パースだけを行わせる
        var loader = AnimeFormat.CreateLoader(filePath);
        var doc = loader.Load(filePath);

        Width = doc.Width;
        Height = doc.Height;
        Rate = doc.Rate;
        Scale = doc.Scale;
        Length = doc.Length;
        imageObjects = doc.ImageObjects;
        groupObjects = doc.GroupObjects;
        soundObjects = doc.SoundObjects;

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
        // Rate=0のとき 1000.0/Rate が Infinity になり、(int)キャストで int.MinValue に化けて
        // Counterの間隔計算が壊れる（アニメーションが実質フリーズする）。未設定時は既定30fps相当にフォールバックする。
        int rate = Rate > 0 ? Rate : 30;
        counter = new Counter(1, Length, (int)(1000.0 * (1000.0 / rate)), IsLoop);
        counter.Start();
        _isPlaying = true;

        // サウンドオブジェクトの再生位置をリセットする
        foreach (var soundObject in soundObjects)
        {
            soundObject.Stop();
        }
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

        foreach (var soundObject in soundObjects)
        {
            soundObject.Stop();
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

    /// <summary>読み込んだ画像オブジェクトの数。</summary>
    public int ImageObjectCount => imageObjects.Count;
    /// <summary>読み込んだサウンドオブジェクトの数。</summary>
    public int SoundObjectCount => soundObjects.Count;
    /// <summary>プロジェクトの尺（フレーム数）。</summary>
    public int LengthFrames => Length;

    /// <summary>
    /// 現在の再生時間（秒）。フレーム数をフレームレートで割って求める（Rate=0のときは0）。
    /// </summary>
    public double Time => counter == null || Rate <= 0 ? 0 : counter.Value / Rate;

    /// <summary>プロジェクトの尺（秒）。Rate=0のときは0。</summary>
    public double EndTime => Rate <= 0 ? 0 : (double)Length / Rate;

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

            // サウンドオブジェクトのボリューム・再生状態を現在フレームに合わせて更新する
            int nowFrame = (int)counter.Value;
            foreach (var soundObject in soundObjects)
            {
                soundObject.UpdateVolume(nowFrame);
            }
        }
    }

    /// <summary>
    /// 全ての画像オブジェクトのテクスチャ読み込みが完了しているか確認します（未完了のものは Pump してロードを進める）。
    /// </summary>
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

    public override string ToString() => $"Exo: {Width}x{Height} Rate={Rate} Scale={Scale} Length={Length} Loop={IsLoop} Objects={imageObjects.Count} Groups={groupObjects.Count} Sounds={soundObjects.Count} Path:{FilePath}";

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
