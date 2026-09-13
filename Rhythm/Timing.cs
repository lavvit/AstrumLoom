using System.Numerics;

namespace AstrumLoom.Rhythm;

public static class RhythmTime
{
    public static long DivideRounded(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        var q = BigInteger.DivRem(numerator, denominator, out var r);
        int comparison = (BigInteger.Abs(r) * 2).CompareTo(denominator);
        if (comparison > 0 || comparison == 0 && !q.IsEven) q += numerator.Sign;
        return checked((long)q);
    }

    public static long Convert(long value, long fromFrequency, long toFrequency)
    {
        if (fromFrequency <= 0 || toFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(fromFrequency));
        return DivideRounded((BigInteger)value * toFrequency, fromFrequency);
    }
}

public enum ClockQuality { Valid, Degraded, Invalid }
public enum TransportState { Stopped, Playing, Paused, Ended, Faulted }

public sealed record ClockSegment(long Generation, long Id, long StartTick, long EndTick,
    long AnchorTick, long AnchorSongUs, long TickFrequency, long SpeedNumerator,
    long SpeedDenominator, long MaxExtrapolationTicks, ClockQuality Quality,
    TransportState State)
{
    public bool TryMap(long tick, out long songUs)
    {
        songUs = 0;
        if (TickFrequency <= 0 || SpeedNumerator <= 0 || SpeedDenominator <= 0 ||
            MaxExtrapolationTicks < 0 || tick < StartTick || tick >= EndTick ||
            Quality != ClockQuality.Valid || State != TransportState.Playing ||
            BigInteger.Abs((BigInteger)tick - AnchorTick) > MaxExtrapolationTicks) return false;
        songUs = checked(AnchorSongUs + RhythmTime.DivideRounded(
            ((BigInteger)tick - AnchorTick) * 1_000_000 * SpeedNumerator,
            (BigInteger)TickFrequency * SpeedDenominator));
        return true;
    }
}

public interface IAudioTransport : IDisposable
{
    TransportState State { get; }
    long Generation { get; }
    bool TryGetClock(out ClockSegment segment);
    void Play();
    void Pause();
    void Seek(long songUs);
}

public enum InputOrigin { OsReceipt, Hardware, Synthetic }
public enum InputAction { Down, Up, Reset }
public readonly record struct TimedInputEvent(long Tick, long Sequence, string DeviceId,
    int PhysicalCode, InputAction Action, InputOrigin Origin);
public sealed record InputLoss(long FirstSequence, long LastSequence, long Count, long Tick);
public interface ITimedInputSource : IDisposable
{
    long TickFrequency { get; }
    InputLoss? Loss { get; }
    bool TryRead(out TimedInputEvent input);
}

/// <summary>Single ordered publication point; overflow latches until a new source is created.</summary>
public sealed class TimedInputQueue : ITimedInputSource
{
    private readonly int capacity;
    public TimedInputQueue(int capacity=4096)
    {
        if(capacity<=0)throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity=capacity;
    }
    private readonly Queue<TimedInputEvent> queue = new();
    private readonly object gate = new();
    private long sequence;
    private long lastTick = long.MinValue;
    private InputLoss? loss;
    public long TickFrequency => System.Diagnostics.Stopwatch.Frequency;
    public InputLoss? Loss { get { lock (gate) return loss; } }
    public void Publish(long tick, string device, int code, InputAction action, InputOrigin origin = InputOrigin.OsReceipt)
    {
        lock (gate)
        {
            if (tick < lastTick) throw new ArgumentException("Input clock moved backwards.");
            lastTick = tick;
            long seq = checked(++sequence);
            if (loss != null || queue.Count >= capacity)
            {
                loss = new(loss?.FirstSequence ?? seq, seq, (loss?.Count ?? 0) + 1, tick);
                return;
            }
            queue.Enqueue(new(tick, seq, device, code, action, origin));
        }
    }
    public bool TryRead(out TimedInputEvent input) { lock (gate) return queue.TryDequeue(out input); }
    public void Dispose() { }
}
