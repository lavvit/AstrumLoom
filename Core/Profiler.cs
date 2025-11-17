// 詳細な設計（疑似コード）
// 1) Profiler クラスを作る（静的）
// 2) API:
//    - BeginLoop(): ループ開始。内部タイマーをリセットして開始。
//    - EndLoop(): ループ終了。記録された各セクションの経過時間を合計時間と比較して割合を算出し、履歴に保存。
//    - BeginSection(name): セクション開始。名前ごとにスタックで開始タイムスタンプを保存（ネスト対応）。
//    - EndSection(name): セクション終了。開始スタンプをポップして経過を現在ループ集計に加算。
//    - Measure(name): IDisposable を返して using で簡単に計測できるヘルパー。
//    - GetLastLoopReports(): 最後に EndLoop() したループのレポートを取得（名前, ticks, ms, percent）。
//    - ReportLastLoop(): 人間向けの整形文字列を返す。
// 3) 内部データ構造:
//    - currentTicks: Dictionary<string,long> 名前ごとの累積 ticks（Stopwatch ticks）
//    - startStacks: Dictionary<string, Stack<long>> ネスト対応の開始スタック
//    - loopStopwatch: Stopwatch でループ全体時間を計測
//    - lastReports: List<Dictionary<string,Report>> 履歴（最大 N 件）
// 4) EndLoop() で計算する内容:
//    - totalTicks = loopStopwatch.ElapsedTicks
//    - 各セクション percent = ticks / totalTicks * 100
//    - unaccounted = totalTicks - sum(known ticks) を "<unaccounted>" として追加（計測漏れ時間）
// 5) スレッドセーフにするために内部ロックを使用する。
// 6) 簡単な使用例（コメント）をファイル末尾に残す。

using System.Diagnostics;

namespace AstrumLoom;

public readonly struct ProfilerReport
{
    public string Name { get; init; }
    public long Ticks { get; init; }
    public double Milliseconds { get; init; }
    public double Percent { get; init; }
    public override string ToString() => $"{Name}: {Milliseconds:F3} ms ({Percent:F1}%)";
}

public static class Profiler
{
    // 設定
    private static readonly object _sync = new();
    private static readonly Dictionary<string, long> _currentTicks = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Stack<long>> _startStacks = new(StringComparer.Ordinal);
    private static readonly List<ProfilerReport[]> _history = [];
    private const int MaxHistory = 20;

    private static readonly Stopwatch _loopStopwatch = new();

    // Freeze threshold (ms)
    private static readonly double FreezeThresholdMs = 64.0;
    private static readonly long FreezeThresholdTicks = (long)(Stopwatch.Frequency * FreezeThresholdMs / 1000.0);

    // 開始タイミング。ループ直前に呼ぶ。
    public static void BeginLoop()
    {
        lock (_sync)
        {
            _currentTicks.Clear();
            _startStacks.Clear();
            _loopStopwatch.Restart();
        }
    }

    // セクション開始。name は任意（同じ名前を複数回呼べる＝累積）。
    public static void BeginSection(string name)
    {
        name ??= "<null>";
        long stamp = Stopwatch.GetTimestamp();
        lock (_sync)
        {
            if (!_startStacks.TryGetValue(name, out var stack))
            {
                stack = new Stack<long>();
                _startStacks[name] = stack;
            }
            stack.Push(stamp);
        }
    }

    // セクション終了。BeginSection と対で呼ぶ。BeginSection が複数回呼ばれていれば LIFO で対応。
    public static void EndSection(string name, bool logFreeze = true)
    {
        name ??= "<null>";
        long end = Stopwatch.GetTimestamp();
        lock (_sync)
        {
            if (!_startStacks.TryGetValue(name, out var stack) || stack.Count == 0)
            {
                // 呼び出しミスを無視しつつログに残すこともできる
                //Log.Debug($"Profiler: EndSection called without matching BeginSection for '{name}'");
                return;
            }
            long start = stack.Pop();
            long delta = end - start;

            if (_currentTicks.ContainsKey(name))
                _currentTicks[name] += delta;
            else
                _currentTicks[name] = delta;

            // Freeze 検出 (>16 ms)
            if (delta > FreezeThresholdTicks && logFreeze)
            {
                double ms = delta * 1000.0 / Stopwatch.Frequency;
                Log.Debug($"Profiler: freeze detected in section '{name}': {ms:F3} ms");
            }
        }
    }

    // using (Profiler.Measure("X")) { ... }
    public static IDisposable Measure(string name, bool logFreeze = true) => new SectionDisposable(name, logFreeze);

    private sealed class SectionDisposable : IDisposable
    {
        private readonly string _name;
        private bool _disposed;
        private bool _freezeLogged;

        public SectionDisposable(string name, bool freezeLogged = true)
        {
            _name = name ?? "<null>";
            BeginSection(_name);
            _freezeLogged = freezeLogged;
        }
        public void Dispose()
        {
            if (!_disposed)
            {
                EndSection(_name, _freezeLogged);
                _disposed = true;
            }
        }
    }

    // ループ終了。EndLoop を呼んだ時点の割合などを計算して履歴に残す。
    public static void EndLoop()
    {
        ProfilerReport[] snapshot;
        lock (_sync)
        {
            _loopStopwatch.Stop();
            long totalTicks = _loopStopwatch.ElapsedTicks;
            long loopEndRaw = Stopwatch.GetTimestamp();

            //例外などで EndSection が呼ばれずスタックに残っているセクションを処理してログ出力
            foreach (var kv in _startStacks)
            {
                string name = kv.Key;
                var stack = kv.Value;
                if (stack == null || stack.Count == 0) continue;

                long accumulated = 0;
                int count = 0;
                while (stack.Count > 0)
                {
                    long start = stack.Pop();
                    long delta = loopEndRaw - start;
                    accumulated += delta;
                    count++;
                }
                if (accumulated > 0)
                {
                    if (_currentTicks.ContainsKey(name)) _currentTicks[name] += accumulated;
                    else _currentTicks[name] = accumulated;

                    double ms = accumulated * 1000.0 / Stopwatch.Frequency;
                    //Log.Debug($"Profiler: section '{name}' was not closed {count} time(s) and was counted as {ms:F3} ms (likely skipped due to exception or early exit)");
                }
            }

            long sumKnown = _currentTicks.Values.Sum();

            var list = new List<ProfilerReport>();

            // 各セクションを報告に変換
            foreach (var kv in _currentTicks.OrderByDescending(k => k.Value))
            {
                double ms = kv.Value * 1000.0 / Stopwatch.Frequency;
                double pct = totalTicks > 0 ? (double)kv.Value / totalTicks * 100.0 : 0.0;
                list.Add(new ProfilerReport { Name = kv.Key, Ticks = kv.Value, Milliseconds = ms, Percent = pct });
            }

            // 計測漏れ時間を追加
            long unaccounted = totalTicks - sumKnown;
            if (unaccounted > 0)
            {
                double ms = unaccounted * 1000.0 / Stopwatch.Frequency;
                double pct = totalTicks > 0 ? (double)unaccounted / totalTicks * 100.0 : 0.0;
                list.Add(new ProfilerReport { Name = "<unaccounted>", Ticks = unaccounted, Milliseconds = ms, Percent = pct });
            }

            // 合計行
            {
                double totalMs = totalTicks * 1000.0 / Stopwatch.Frequency;
                list.Add(new ProfilerReport { Name = "<total>", Ticks = totalTicks, Milliseconds = totalMs, Percent = 100.0 });

                // フレームの合計が閾値を超えていたらログ出力
                if (totalMs > FreezeThresholdMs && !_currentTicks.Any(kv => kv.Value > FreezeThresholdTicks))
                {
                    Log.Debug($"Profiler: frame freeze detected: {totalMs:F3} ms");
                }
            }

            snapshot = list.ToArray();
            _history.Add(snapshot);
            if (_history.Count > MaxHistory) _history.RemoveAt(0);
        }
    }

    // 最後に EndLoop() したループのレポートを取得。存在しない場合は空配列を返す。
    public static ProfilerReport[] GetLastLoopReports()
    {
        lock (_sync)
        {
            if (_history.Count == 0) return Array.Empty<ProfilerReport>();
            // 戻り値の安全のためコピーを返す
            var last = _history[^1];
            var copy = new ProfilerReport[last.Length];
            Array.Copy(last, copy, last.Length);
            return copy;
        }
    }

    // 人間向けの整形文字列（一行ごとに Name: ms (pct%)）
    public static string ReportLastLoop()
    {
        var reports = GetLastLoopReports();
        return reports.Length == 0 ? "No profiler data." : string.Join(Environment.NewLine, reports.Select(r => r.ToString()));
    }

    // 履歴をクリア
    public static void ClearHistory()
    {
        lock (_sync) _history.Clear();
    }

    // 簡易チェック用：最後の N ループの平均を計算して返す（オプション）
    public static ProfilerReport[] GetAverageOfLast(int lastCount)
    {
        lock (_sync)
        {
            if (_history.Count == 0) return Array.Empty<ProfilerReport>();
            lastCount = Math.Max(1, Math.Min(lastCount, _history.Count));
            var slice = _history.Skip(Math.Max(0, _history.Count - lastCount)).ToArray();

            // 名前の集合を作る
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var arr in slice) foreach (var r in arr) names.Add(r.Name);

            var aggregated = new List<ProfilerReport>();
            foreach (string name in names)
            {
                long sumTicks = 0;
                double sumMs = 0;
                double sumPct = 0;
                int found = 0;
                foreach (var arr in slice)
                {
                    var r = Array.Find(arr, x => x.Name == name);
                    if (!r.Equals(default(ProfilerReport)))
                    {
                        sumTicks += r.Ticks;
                        sumMs += r.Milliseconds;
                        sumPct += r.Percent;
                        found++;
                    }
                }
                if (found > 0)
                {
                    aggregated.Add(new ProfilerReport
                    {
                        Name = name,
                        Ticks = sumTicks / found,
                        Milliseconds = sumMs / found,
                        Percent = sumPct / found
                    });
                }
            }
            return aggregated.OrderByDescending(r => r.Percent).ToArray();
        }
    }
}

/*
使用例:
    // 各ループの先頭で
    Profiler.BeginLoop();

    // 計測したい処理
    using (Profiler.Measure("Load"))
    {
        LoadSomething();
    }

    Profiler.BeginSection("Compute");
    Compute();
    Profiler.EndSection("Compute");

    // ループの終わりで
    Profiler.EndLoop();

    // レポート表示
    Console.WriteLine(Profiler.ReportLastLoop());
*/
