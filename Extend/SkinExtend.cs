namespace AstrumLoom.Extend;

public class SkinExtend
{
    public static Dictionary<string, SoundExtend> ExSounds = [];

    private static bool _loading = false;
    private static Queue<string> InportQue = [];
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
    public static void FinishInport() => Loaded = true;
    public static bool Loaded { get; private set; } = false;
    public static int QueueCount => InportQue.Count;
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
    public static void AddSound(string key, string path)
    {
        if (ExSounds.ContainsKey(key))
            return;
        var s = Skin.Sound(key);
        var sound = new SoundExtend(path, s?.Loop ?? false, true);
        ExSounds.Add(key, sound);
    }
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
    public static SoundExtend GetSound(string key)
        => GetSound(key.ToLowerInvariant()) ?? new("");
    #endregion
}
