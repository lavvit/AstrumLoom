namespace AstrumLoom.Extend;

public class SkinExtend
{
    public static Dictionary<string, SoundExtend> ExSounds = [];

    private static bool _loading = false;
    private static Queue<string> InportQue = [];
    public static void Inport(bool inque = false)
    {
        if (_loading)
        {
            ReadQueue();
            return;
        }
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
    public static bool Loaded => !_loading && InportQue.Count == 0 && SoundLoaded();
    public static int QueueCount => InportQue.Count;
    public static void ReadQueue()
    {
        while (InportQue.Count > 0)
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
        }
    }
    public static void AddSound(string key, string path)
    {
        if (ExSounds.ContainsKey(key))
            return;
        var sound = new SoundExtend(path);
        ExSounds.Add(key, sound);
    }
    public static bool SoundLoaded()
    {
        if (ExSounds.Count < Skin.Sounds.Count)
            return false;
        foreach (var sound in ExSounds)
        {
            if (!sound.Value.Loaded)
                return false;
        }
        return true;
    }

    #region Sound
    public static SoundExtend? GetSound(string key)
        => ExSounds.TryGetValue(key, out var sound) ? sound : null;
    public static SoundExtend SoundExtend(string key)
        => GetSound(key) ?? new();
    #endregion
}
