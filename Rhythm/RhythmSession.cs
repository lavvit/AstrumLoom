using System.Diagnostics;

namespace AstrumLoom.Rhythm;

/// <summary>Single-consumer adapter. Call Pump independently of drawing for timely feedback.</summary>
public sealed class RhythmSession
{
    private readonly ITimedInputSource input;
    private readonly IAudioTransport audio;
    private readonly Dictionary<int,int> lanes;
    private readonly long graceTicks;
    private readonly List<TimedInputEvent> waiting = new();
    private readonly List<ReplayOperation> operations = new();
    private readonly List<(TimedInputEvent Raw, JudgeInput Mapped, long Generation, long Segment)> trace = new();
    private long generation;
    private string? selectedDevice;
    public JudgeSession Judge { get; }
    public IReadOnlyList<ReplayOperation> Operations => operations.AsReadOnly();
    public IReadOnlyList<(TimedInputEvent Raw, JudgeInput Mapped, long Generation, long Segment)> InputTrace => trace.AsReadOnly();
    public RhythmSession(ITimedInputSource input, IAudioTransport audio, RhythmChart chart,
        IReadOnlyDictionary<int,int> physicalLanes, long graceUs, JudgeSettings? settings=null)
    {
        if(graceUs<0 || input.TickFrequency!=Stopwatch.Frequency) throw new ArgumentException("Unsupported input clock or grace.");
        this.input=input; this.audio=audio; lanes=new(physicalLanes);
        graceTicks=RhythmTime.Convert(graceUs,1_000_000,Stopwatch.Frequency);
        Judge=new(chart,settings); generation=audio.Generation;
    }
    private void Abort(string reason) { Judge.Abort(reason); operations.Add(new(Abort:reason)); }
    public void Pump()
    {
        if(Judge.Failure!=null)return;
        if(input.Loss!=null) { Abort("Input queue overflow."); return; }
        if(input is WindowsRawInputSource raw && raw.Failure!=null) { Abort("Input source failure: "+raw.Failure.Message); return; }
        if(audio.Generation!=generation) { Abort("Transport generation changed; create a new session."); return; }
        if(audio.State is TransportState.Faulted or TransportState.Paused) { Abort("Transport stopped or paused."); return; }
        while(input.TryRead(out var e)) waiting.Add(e);
        if(!audio.TryGetClock(out var clock))return;
        long now=Stopwatch.GetTimestamp();
        if(!clock.TryMap(now,out _)) { Abort("Invalid or expired audio observation."); return; }
        foreach(var e in waiting)
        {
            if(e.Tick<clock.StartTick)continue; // inputs before this playback interval are not scoreable
            if(!clock.TryMap(e.Tick,out long song)) { Abort("Input is outside retained audio observation."); return; }
            if(e.Action!=InputAction.Reset && !lanes.ContainsKey(e.PhysicalCode))continue;
            if(e.Action!=InputAction.Reset)
            {
                selectedDevice ??= e.DeviceId;
                if(selectedDevice!=e.DeviceId)continue;
            }
            var mapped=new JudgeInput(song,e.Sequence,e.Action==InputAction.Reset?-1:lanes[e.PhysicalCode],e.Action);
            Judge.Enqueue(mapped); operations.Add(new(mapped)); trace.Add((e,mapped,clock.Generation,clock.Id));
        }
        waiting.Clear();
        long boundary=now-graceTicks;
        if(boundary>=clock.StartTick && clock.TryMap(boundary,out long watermark))
        {
            if(Judge.Watermark.HasValue) watermark=Math.Max(watermark,Judge.Watermark.Value);
            Judge.AdvanceTo(watermark); operations.Add(new(Watermark:watermark));
        }
    }
}
