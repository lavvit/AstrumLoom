namespace AstrumLoom;

public class Randomize
{
    // ロック対象は差し替えない専用オブジェクトにする。
    // _random 自身を lock すると Seed() での差し替え後に別インスタンスを掴んでしまい、排他にならない。
    private static readonly object _sync = new();
    private static Random _random = new();

    /// <summary>直近に設定されたシード。未設定なら null。</summary>
    public static int? CurrentSeed { get; private set; }

    public static void Seed(int seed)
    {
        lock (_sync)
        {
            _random = new(seed);
            CurrentSeed = seed;
        }
    }

    public static int Int(int max) => Int(0, max);
    public static int Int(int min, int max)
    {
        lock (_sync)
            return _random.Next(min, max);
    }
    public static double Double()
    {
        lock (_sync)
            return _random.NextDouble();
    }

    public static bool Bool() => Int(0, 2) == 1;
    public static int Int4() => Int(0, 16);
    public static int Int8() => Int(0, 256);
    public static int Int16() => Int(0, 65536);
    public static int Int32() => Int(0, int.MaxValue);
    public static int Int() => Int32();

    public static int[] Ints(int count, int min, int max)
    {
        int[] result = new int[count];
        lock (_sync)
            for (int i = 0; i < count; i++)
                result[i] = _random.Next(min, max);
        return result;
    }
    public static double[] Doubles(int count)
    {
        double[] result = new double[count];
        lock (_sync)
            for (int i = 0; i < count; i++)
                result[i] = _random.NextDouble();
        return result;
    }
}
