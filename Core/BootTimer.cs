namespace AstrumLoom;

/// <summary>
/// 起動の各段階にかかった時間を記録して、まとめて表に出す計測器。
/// <c>--boot-timing</c>（または環境変数 <c>ASTRUMLOOM_BOOT_TIMING=1</c>）を付けて起動すると、
/// 最初の描画が終わった時点で内訳がコンソールとログに出ます。
/// </summary>
/// <remarks>
/// 記録そのものは常に行います（1 点あたり Stopwatch の読み取りと文字列 1 個ぶんで、
/// 起動時に数十回しか呼ばれない）。出力だけをオプションで切り替えます。
/// こうしておかないと「計測を有効にしたときだけ通る道」ができて、
/// 測った値が普段の起動と違うものになります。
/// </remarks>
public static class BootTimer
{
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static readonly List<(string Name, double Ms, bool IsDetail)> Marks = [];
    private static readonly object Gate = new();
    private static bool _reported;

    /// <summary>プロセス開始からこのクラスが初めて触られるまで（= .NET ランタイムの立ち上がり）の時間。ミリ秒。</summary>
    public static double RuntimeStartupMs { get; } = MeasureRuntimeStartup();

    /// <summary>出力するかどうか。<c>--boot-timing</c> か環境変数で立つ。</summary>
    public static bool Enabled { get; set; }
        = Environment.GetEnvironmentVariable("ASTRUMLOOM_BOOT_TIMING") is "1" or "true";

    private static double MeasureRuntimeStartup()
    {
        try
        {
            // Process.StartTime は OS がプロセスを作った時刻。ここから今までが
            // 「Main に入るまで」で、JIT やアセンブリの読み込みが効いてくる部分。
            var start = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
            double ms = (DateTime.UtcNow - start).TotalMilliseconds;
            // まれに OS から現実的でない開始時刻が返る（1 分以上前など）。
            // そのまま足すと合計が桁違いになって、表そのものが信用できなくなる。
            return ms is >= 0 and < 30000 ? ms : double.NaN;
        }
        catch
        {
            return double.NaN; // 取れない環境でも計測全体は止めない
        }
    }

    /// <summary>1 つの段階が終わったことを記録します。名前は表にそのまま出ます。</summary>
    /// <remarks>
    /// 表に出るのは「前の記録からの差」なので、間に挟まった別の処理もその段階に乗ります。
    /// ある処理そのものにかかった時間を知りたいときは <see cref="Detail"/> を使ってください。
    /// </remarks>
    public static void Mark(string name)
    {
        lock (Gate) Marks.Add((name, Clock.Elapsed.TotalMilliseconds, false));
    }

    /// <summary>
    /// ある処理そのものにかかった時間を、内訳の中に 1 行足します（時系列の区切りにはなりません）。
    /// 同じ名前で複数回呼ぶと合算され、回数も出ます。
    /// </summary>
    public static void Detail(string name, double ms)
    {
        lock (Gate)
        {
            for (int i = 0; i < Marks.Count; i++)
            {
                if (Marks[i].IsDetail && Marks[i].Name == name)
                {
                    Marks[i] = (name, Marks[i].Ms + ms, true);
                    DetailCounts[name] = DetailCounts.GetValueOrDefault(name) + 1;
                    return;
                }
            }
            Marks.Add((name, ms, true));
            DetailCounts[name] = 1;
        }
    }

    private static readonly Dictionary<string, int> DetailCounts = [];

    /// <summary>ある処理を計り、終わったら <see cref="Detail"/> へ足す使い捨ての計測器を返します。</summary>
    public static IDisposable Measure(string name) => new Span(name);

    private sealed class Span(string name) : IDisposable
    {
        private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
        public void Dispose() => Detail(name, _sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>記録した内訳を出力します。2 回目以降は何もしません。</summary>
    public static void Report()
    {
        if (!Enabled) return;
        lock (Gate)
        {
            if (_reported) return;
            _reported = true;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine("=== 起動時間の内訳 ===");
            if (!double.IsNaN(RuntimeStartupMs))
                sb.AppendLine($"  {"ランタイムの立ち上がり（Main に入るまで）",-44} {RuntimeStartupMs,9:F1} ms");
            else
                sb.AppendLine($"  {"ランタイムの立ち上がり（Main に入るまで）",-44}     測れず（合計には入れていません）");

            double prev = 0;
            foreach (var (name, ms, isDetail) in Marks)
            {
                if (isDetail)
                {
                    // 内訳の 1 行。時系列の区切りではないので prev は動かさない。
                    int count = DetailCounts.GetValueOrDefault(name);
                    string times = count > 1 ? $" × {count} 回" : "";
                    sb.AppendLine($"    └ {name,-40} {ms,9:F1} ms{times}");
                    continue;
                }
                sb.AppendLine($"  {name,-44} {ms - prev,9:F1} ms   （累計 {ms,8:F1} ms）");
                prev = ms;
            }

            double total = prev + (double.IsNaN(RuntimeStartupMs) ? 0 : RuntimeStartupMs);
            sb.AppendLine($"  {"合計",-44} {total,9:F1} ms");
            sb.AppendLine("======================");

            // Log.Write はコンソールにも流すので、ここで Console.WriteLine すると二重に出る。
            Log.Write(sb.ToString());
        }
    }

    /// <summary>計測をやり直します。テストから複数回起動するとき用。</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Marks.Clear();
            DetailCounts.Clear();
            _reported = false;
            Clock.Restart();
        }
    }
}
