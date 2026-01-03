namespace AstrumLoom;

public class Randomize
{
    private static Random _random = new();

    public static void Seed(int seed)
    {
        lock (_random)
            _random = new(seed);
    }

    public static int Int(int max) => Int(0, max);
    public static int Int(int min, int max)
    {
        lock (_random)
            return _random.Next(min, max);
    }
    public static double Double()
    {
        lock (_random)
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
        lock (_random)
            for (int i = 0; i < count; i++)
                result[i] = _random.Next(min, max);
        return result;
    }
    public static double[] Doubles(int count)
    {
        double[] result = new double[count];
        lock (_random)
            for (int i = 0; i < count; i++)
                result[i] = _random.NextDouble();
        return result;
    }
}
