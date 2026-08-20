namespace AstrumLoom.Extend;

/// <summary>
/// Skin が読み込んだ Sound 群を、拡張再生機能を持つ <see cref="SoundExtend"/> として二重に読み込み直す橋渡しクラス。
/// Skin.Sounds のパスをそのまま使うので、Skin 側の読み込みが完了している前提で Inport を呼ぶ。
/// </summary>
public class SkinExtend
{
    public static Dictionary<string, SoundExtend> ExSounds = [];

    private static bool _loading = false;
    private static Queue<string> InportQue = [];
    /// <summary>
    /// Skin.Sounds の内容を ExSounds へ取り込みます。<paramref name="inque"/> なら即座に生成せず
    /// InportQue に積み、<see cref="ReadQueue"/> で少しずつ処理します。
    /// </summary>
    public static void Inport(bool inque = false)
    {
        if (_loading || Loaded)
            return;
        _loading = true;
        foreach (var sound in Skin.Sounds)
        {
            if (inque)
            {
                // Add to queue
                InportQue.Enqueue("exsnd" + sound.Key);
            }
            else
            {
                // Import directly
                AddSound(sound.Key, sound.Value.Path);
            }
        }
    }
    /// <summary>取り込み完了フラグを立てます。</summary>
    public static void FinishInport() => Loaded = true;
    public static bool Loaded { get; private set; } = false;
    public static int QueueCount => InportQue.Count;
    /// <summary>
    /// 取り込みキューから最大 <paramref name="count"/> 件を実体化し、未読み込みのサウンドを Pump します。
    /// 全て読み込み完了かつキューが空になったら自動的に FinishInport します。
    /// </summary>
    public static void ReadQueue(int count = 1)
    {
        if (Loaded) return;
        while (InportQue.Count > 0 && count > 0)
        {
            string key = InportQue.Dequeue();
            if (key.StartsWith("exsnd"))
            {
                string sndkey = key[5..];
                if (Skin.Sounds.TryGetValue(sndkey, out var value))
                {
                    AddSound(sndkey, value.Path);
                }
            }
            count--;
        }
        foreach (var sound in ExSounds.Values.Where(s => !s.Loaded))
        {
            sound.Pump();
        }
        if (SoundLoaded() && InportQue.Count == 0)
            FinishInport();
    }
    /// <summary>指定キーが未登録なら、対応する Skin.Sound のループ設定を引き継いで SoundExtend を生成します。</summary>
    public static void AddSound(string key, string path)
    {
        if (ExSounds.ContainsKey(key))
            return;
        var s = Skin.Sound(key);
        var sound = new SoundExtend(path, s?.Loop ?? false, true);
        ExSounds.Add(key, sound);
    }
    /// <summary>Skin.Sounds と同じ件数取り込み済みで、かつ全て読み込み完了しているか。</summary>
    public static bool SoundLoaded()
    {
        if (ExSounds.Count < Skin.Sounds.Count)
            return false;
        foreach (var sound in ExSounds.Values)
        {
            if (!sound.Enable)
                return false;
        }
        return true;
    }

    #region Sound
    /// <summary>名前で SoundExtend を取得します。見つからない、または未読み込みなら null。</summary>
    public static SoundExtend? SoundExtend(string name, string? subname = null)
    {
        name = name.ToLower();
        SoundExtend? result = null;
        if (ExSounds.TryGetValue(name, out var value))
        {
            value?.Pump();
            result = value;
        }
        else if (!string.IsNullOrEmpty(subname))
        {
            if (ExSounds.TryGetValue(subname, out var subvalue))
            {
                subvalue?.Pump();
                result = subvalue;
            }
        }
        return result != null && result.Enable ? result : null;
    }
    /// <summary>
    /// 名前から音を引きます。見つからなければ空のインスタンスを返すので、戻り値は null になりません。
    /// </summary>
    /// <remarks>
    /// 呼ぶ先は 1 つ上の <see cref="SoundExtend(string, string?)"/>。
    /// ここを GetSound と書くと自分自身を呼んで無限再帰になり、
    /// StackOverflowException（.NET では捕捉できない）でプロセスごと落ちる。
    /// </remarks>
    public static SoundExtend GetSound(string key)
        => SoundExtend(key.ToLowerInvariant()) ?? new("");
    #endregion
}
