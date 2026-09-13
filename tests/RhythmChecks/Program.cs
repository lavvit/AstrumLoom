using AstrumLoom.Rhythm;


if(args.Contains("--native"))
{
    using(var raw=new WindowsRawInputSource()) { Thread.Sleep(30); if(raw.Failure!=null) throw raw.Failure; }
    using(var raw=new WindowsRawInputSource()) { Thread.Sleep(30); if(raw.Failure!=null) throw raw.Failure; }
    using var audio=new WasapiTransport(new float[48000*2*3],48000);
    audio.Play(); Thread.Sleep(150);
    if(!audio.TryGetClock(out var c) || !c.TryMap(System.Diagnostics.Stopwatch.GetTimestamp(),out var pos) || pos<=0) throw new Exception("Native audio clock did not advance: "+audio.Failure);
    audio.Pause(); if(audio.State!=TransportState.Paused || audio.TryGetClock(out _))throw new Exception("Pause clock");
    audio.Play(); Thread.Sleep(80); if(!audio.TryGetClock(out _))throw new Exception("Resume clock");
    long generation=audio.Generation; audio.Seek(1_000_000); audio.Play(); Thread.Sleep(100);
    if(audio.Generation!=generation+1 || !audio.TryGetClock(out c) || c.AnchorSongUs<1_000_000)throw new Exception("Seek clock");
    using var input=new TimedInputQueue();
    var integration=new RhythmSession(input,audio,new RhythmChart("native",[new(1,0,1_500_000)]),new Dictionary<int,int>{{1,0}},50_000);
    var until=System.Diagnostics.Stopwatch.StartNew(); bool injected=false;
    while(until.ElapsedMilliseconds<1500 && !integration.Judge.Complete && integration.Judge.Failure==null)
    {
        if(!injected && audio.TryGetClock(out c) && c.TryMap(System.Diagnostics.Stopwatch.GetTimestamp(),out long nowUs) && nowUs>=1_500_000)
        { input.Publish(System.Diagnostics.Stopwatch.GetTimestamp(),"synthetic",1,InputAction.Down,InputOrigin.Synthetic); injected=true; }
        integration.Pump(); Thread.Sleep(2);
    }
    if(!integration.Judge.Complete || integration.Judge.Results.Single().Kind!=Judgement.Hit)throw new Exception("Native integration: "+integration.Judge.Failure);
    Console.WriteLine("PASS native raw-input lifecycle and WASAPI play/pause/resume/seek clock (silent PCM)");
    return;
}

int count = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
Check(RhythmTime.Convert(1, 2, 1) == 0 && RhythmTime.Convert(3, 2, 1) == 2 && RhythmTime.Convert(-3, 2, 1) == -2, "ties to even");
Check(RhythmTime.Convert(long.MaxValue, 1_000_000, 1_000_000) == long.MaxValue, "wide intermediate");
Check(RhythmTime.Convert(44100, 44100, 1_000_000) == 1_000_000, "44.1k conversion");
var segment = new ClockSegment(1, 1, 0, 1000, 0, 0, 3000, 1, 1, 1000, ClockQuality.Valid, TransportState.Playing);
Check(segment.TryMap(3, out var us) && us == 1000 && !segment.TryMap(1000, out _), "non-QPC frequency and half-open segment");
Check(!(segment with { Quality = ClockQuality.Degraded }).TryMap(3, out _), "degraded clock rejected");
var chart = new RhythmChart("test", [new(1, 0, 1_000_000), new(2, 1, 1_000_000, 2_000_000)]);
var settings = new JudgeSettings();
var operations = new ReplayOperation[] { new(new(1_000_000, 1, 0, InputAction.Down)), new(new(1_000_000, 2, 1, InputAction.Down)), new(Watermark:1_500_000), new(new(2_000_000, 3, 1, InputAction.Up)), new(Watermark:2_200_000) };
var replay = new RhythmReplay(1, "test", "audio", settings, chart.Notes.ToArray(), operations);
var result = replay.Play();
Check(result.Complete && result.Results.Count == 3 && result.Results[^1].Kind == Judgement.HoldComplete, "tap and hold");
Check(RhythmReplay.FromJson(replay.ToJson()).Play().Results.SequenceEqual(result.Results), "JSON replay exact");
foreach (long delta in new long[] {-100001,-100000,100000,100001})
{
    var j = new JudgeSession(new("boundary", [new(0,0,1_000_000)]));
    j.Enqueue(new(1_000_000 + delta, 1, 0, InputAction.Down)); j.AdvanceTo(2_000_000);
    Check(j.Results.Single().Kind == (Math.Abs(delta) <= 100000 ? Judgement.Hit : Judgement.Miss), "boundary " + delta);
}
var late = new JudgeSession(chart); late.AdvanceTo(1_100_001); var before = late.Results.ToArray(); late.Enqueue(new(1_000_000,1,0,InputAction.Down));
Check(late.Failure != null && late.Results.SequenceEqual(before), "late input cannot mutate committed misses");
var duplicate = new JudgeSession(chart); duplicate.Enqueue(new(0,1,0,InputAction.Down)); duplicate.Enqueue(new(1,1,0,InputAction.Up));
Check(duplicate.Failure != null, "duplicate sequence rejected");
var queue = new TimedInputQueue(1); queue.Publish(0,"a",1,InputAction.Down); queue.Publish(1,"a",1,InputAction.Up); queue.Publish(2,"a",1,InputAction.Down);
Check(queue.Loss is { Count:2, FirstSequence:2, LastSequence:3 }, "overflow range retained");
var compiled = ChartTiming.Compile("tempo", 480, [new(0,120),new(480,60,1,250000)], [new(1,0,480),new(2,0,960)]);
Check(compiled.Notes[0].TimeUs == 750000 && compiled.Notes[1].TimeUs == 1750000, "BPM change and stop before same-pulse note");
foreach (int batch in new[] {1,2,16,1000})
{
    var j = new JudgeSession(chart);
    var input = operations.Where(o => o.Input.HasValue).Select(o => o.Input!.Value).ToArray();
    for(int i=0;i<input.Length;i+=batch) { foreach(var e in input.Skip(i).Take(batch)) j.Enqueue(e); }
    j.AdvanceTo(3_000_000);
    Check(j.Results.SequenceEqual(result.Results), "batch invariant " + batch);
}
Console.WriteLine($"{count} rhythm checks passed.");
var holdChart=new RhythmChart("hold",[new(1,0,1_000_000,2_000_000)]);
foreach(long release in new long[]{1_899_999,1_900_000,2_100_000,2_100_001})
{
    var j=new JudgeSession(holdChart); j.Enqueue(new(1_000_000,1,0,InputAction.Down)); j.Enqueue(new(release,2,0,InputAction.Up)); j.AdvanceTo(3_000_000);
    Check(j.Results.Last().Kind==(release>=1_900_000 && release<=2_100_000?Judgement.HoldComplete:Judgement.HoldBroken),"hold release boundary "+release);
}
var reset=new JudgeSession(holdChart); reset.Enqueue(new(1_000_000,1,0,InputAction.Down));reset.Enqueue(new(1_500_000,2,-1,InputAction.Reset));reset.AdvanceTo(2_000_000);
Check(reset.Failure!=null,"reset interrupts hold");
var correction=new JudgeSession(new("offset",[new(1,0,100000)]),new(InputCorrectionUs:10000));correction.Enqueue(new(90000,1,0,InputAction.Down));correction.AdvanceTo(90000);
Check(correction.Results.Single().ErrorUs==0,"correction sign");
var reordered=new JudgeSession(chart);reordered.Enqueue(new(2_000_000,3,1,InputAction.Up));reordered.Enqueue(new(1_000_000,2,1,InputAction.Down));reordered.Enqueue(new(1_000_000,1,0,InputAction.Down));reordered.AdvanceTo(3_000_000);
Check(reordered.Results.SequenceEqual(result.Results),"out-of-order arrival sorted before commitment");
Console.WriteLine($"TOTAL {count} rhythm checks passed.");
