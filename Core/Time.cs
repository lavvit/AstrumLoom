using System.Diagnostics;

namespace AstrumLoom;

public interface ITime
{
    float DeltaTime { get; }
    float TotalTime { get; }
    float CurrentFps { get; }
    float TargetFps { get; set; }

    void BeginFrame();
    void EndFrame();
}

/// <summary>
/// カウンタークラス。
/// </summary>
public class Counter
{
    private readonly Func<long> _now;
    public static long DefaultNow()
    {
        // Stopwatch をマイクロ秒に変換
        double ticks = Stopwatch.GetTimestamp();
        return (long)(ticks * 1_000_000.0 / Stopwatch.Frequency);
    }
    public long Now => _now();

    private static long NormalizeInterval(long interval) =>
        // 0 は無限ループの原因になるため、最小値として 1 を採用する。
        interval == 0 ? 1 : interval;

    /// <summary>
    /// カウンターを初期化します。
    /// </summary>
    /// <param name="begin">開始値。</param>
    /// <param name="end">終了値。</param>
    /// <param name="interval">Tickする間隔(マイクロ秒)。</param>
    /// <param name="isLoop">ループするか否か。</param>
    public Counter(long begin = 0, long end = 1000, long interval = 1000, bool isLoop = false,
        Func<long>? nowProvider = null)
    {
        _now = nowProvider ?? DefaultNow;

        NowTime = Now;
        Begin = begin;
        End = end;
        Interval = NormalizeInterval(interval);
        Value = begin;
        IsLoop = isLoop;
        State = TimerState.Stopped;
    }

    /// <summary>
    /// Tickします。
    /// </summary>
    /// <returns>何Tickしたか。</returns>
    public long Tick()
    {
        // 何Tickしたかのカウント
        int tickCount = 0;
        long nowTime = Now;

        // 停止状態の場合、現在時間をそのままプロパティに代入して、終わる。
        if (State == TimerState.Stopped)
        {
            NowTime = nowTime;
            return 0;
        }

        // 現在時間から以前Tick()したまでの時間の差
        long diffTime = nowTime - NowTime;

        if (diffTime < 0)
        {
            // diffTimeが0未満、つまり、カウンターがループしてしまった
            // diffTime を nowTime + 差分にする。

            //diffTime = nowTime + (long.MaxValue - NowTime);

            // しかし、これでも負の値になる場合がある。
            // その場合は、diffTimeを0にしてしまう。
            diffTime = 0;
        }

        // 安全ガード: Interval が 0 のままになっていた場合、無限ループ回避のため補正する。
        if (Interval == 0)
        {
            Interval = 1;
        }

        if (Interval >= 0)
        {
            while (tickCount >= 0 && diffTime >= Interval)
            {
                // 時間の差が間隔未満になるまで進める
                Value++;
                tickCount++;
                if (Value >= End)
                {
                    if (IsLoop)
                    {
                        // ループ設定かつ現在の値が終了値より大きかったら
                        Value = Begin;
                        Looped?.Invoke(this, new EventArgs());
                    }
                    else
                    {
                        // 非ループ設定かつ現在の値が終了値より大きかったら、終了値を維持してタイマーを停止する。
                        Value = End;
                        Stop();
                        Ended?.Invoke(this, new EventArgs());
                    }
                }
                diffTime -= Interval;
            }
        }
        else // 逆の処理
        {
            while (diffTime >= -Interval)
            {
                // 時間の差が間隔未満になるまで進める
                Value--;
                tickCount++;
                if (Value <= Begin)
                {
                    if (IsLoop)
                    {
                        // ループ設定かつ現在の値が終了値より大きかったら
                        Value = End;
                        Looped?.Invoke(this, new EventArgs());
                    }
                    else
                    {
                        // 非ループ設定かつ現在の値が終了値より大きかったら、終了値を維持してタイマーを停止する。
                        Value = Begin;
                        Stop();
                        Ended?.Invoke(this, new EventArgs());
                    }
                }
                diffTime += Interval;
            }
        }
        // 余ったdiffTimeを引いて、次Tick()したときにちゃんとなるように
        NowTime = nowTime - diffTime;
        return tickCount;
    }

    /// <summary>
    /// タイマーを開始します。必ずこのメソッドを呼び出してください。
    /// </summary>
    public void Start()
    {
        if (State == TimerState.Started)
        {
            // すでに開始しているなら、何もしない。
            return;
        }

        // Tick()を呼び出して、NowTimeに現在の時間を代入させてからタイマーを開始する。
        Tick();
        State = TimerState.Started;
    }

    /// <summary>
    /// タイマーを停止します。
    /// </summary>
    public void Stop()
    {
        if (State == TimerState.Stopped)
        {
            // すでに停止しているなら、何もしない。
            return;
        }

        State = TimerState.Stopped;
    }

    /// <summary>
    /// タイマーをリセットします。
    /// </summary>
    public void Reset()
    {
        // 現在時間を入れる。
        NowTime = Now;
        // カウンターを最小値に戻す。
        Value = Begin;
    }

    /// <summary>
    /// タイマーのTick間隔を変更します。
    /// </summary>
    /// <param name="interval">Tickする間隔(マイクロ秒)。</param>
    public void ChangeInterval(long interval)
    {
        // 今までのカウンター値を更新する。
        Tick();
        // 間隔を更新する。
        Interval = NormalizeInterval(interval);
        // 間隔更新後、あまりがあるかもしれないのでもう一度カウンター値を更新する。
        Tick();
    }

    /// <summary>
    /// タイマーの終了値を変更します。
    /// </summary>
    /// <param name="end">終了値。</param>
    public void ChangeEnd(long end)
    {
        End = end;
        if (End < Value)
        {
            Value = End;
        }
    }

    /// <summary>
    /// タイマーの開始値を変更します。
    /// </summary>
    /// <param name="begin">開始値。</param>
    public void ChangeBegin(long begin)
    {
        Begin = begin;
        if (Begin > Value)
        {
            Value = Begin;
        }
    }

    /// <summary>
    /// ループした場合、イベントが発生します。
    /// </summary>
    public event EventHandler? Looped;

    /// <summary>
    /// タイマーが止まった。
    /// </summary>
    public event EventHandler? Ended;

    /// <summary>
    /// 現在のコンピュータの時間(マイクロ秒)。
    /// </summary>
    public long NowTime { get; private set; }

    /// <summary>
    /// 開始値。
    /// </summary>
    public long Begin { get; private set; }

    /// <summary>
    /// 終了値。
    /// </summary>
    public long End { get; private set; }

    /// <summary>
    /// タイマー間隔。
    /// </summary>
    public long Interval { get; private set; }

    /// <summary>
    /// カウンターの現在の値。
    /// </summary>
    public long Value
    {
        get;
        set
        {
            // 最小値・最大値を超える場合、丸める。
            if (value < Begin)
            {
                field = Begin;
                return;
            }
            if (End < value)
            {
                field = End;
                return;
            }
            field = value;
        }
    }

    /// <summary>
    /// ループするかどうか。
    /// </summary>
    public bool IsLoop { get; }

    /// <summary>
    /// 現在の状態。
    /// </summary>
    public TimerState State { get; private set; }

    public DateTime Time
    {
        get
        {
            DateTime time = new();
            time.AddMilliseconds(Value);
            return time;
        }
    }
    public TimeConvert CTime => new(this);

    public double Progress => End > Begin ? (double)(Value - Begin) / (End - Begin) : 0.0;

    public bool IsEnd => !IsLoop && Value == End;
    public bool IsBegin => !IsLoop && Value == Begin;

    public override string ToString() => $"{Value}/{End} From:{Begin} {Interval}tic {State}{(IsLoop ? " Loop" : "")}";
}

/// <summary>
/// タイマーの状態。
/// </summary>
public enum TimerState
{
    /// <summary>
    /// 停止している。
    /// </summary>
    Stopped,

    /// <summary>
    /// 動作している。
    /// </summary>
    Started
}

public struct TimeConvert
{
    public int MiliSeconds;
    public int Seconds;
    public int Minutes;
    public int Hours;
    public int Days;

    public TimeConvert() { }
    public TimeConvert(Counter counter) => this = new TimeConvert((int)counter.Value);
    public TimeConvert(int milisec)
    {
        int n = milisec;
        MiliSeconds = n % 1000;
        n /= 1000;
        Seconds = n % 60;
        n /= 60;
        Minutes = n % 60;
        n /= 60;
        Hours = n % 24;
        n /= 24;
        Days = n;
    }
    public TimeConvert(DateTime time)
    {
        MiliSeconds = time.Millisecond;
        Seconds = time.Second;
        Minutes = time.Minute;
        Hours = time.Hour;
        Days = time.Day;
    }
    public TimeConvert(TimeSpan time)
    {
        MiliSeconds = time.Milliseconds;
        Seconds = time.Seconds;
        Minutes = time.Minutes;
        Hours = time.Hours;
        Days = time.Days;
    }

    public override readonly string ToString() => Days != 0
            ? $"{Days}/{Hours:00}:{Minutes:00}:{Seconds:00}.{MiliSeconds:000}"
            : Hours != 0 ? $"{Hours:00}:{Minutes:00}:{Seconds:00}.{MiliSeconds:000}" : $"{Minutes:00}:{Seconds:00}.{MiliSeconds:000}";
}
