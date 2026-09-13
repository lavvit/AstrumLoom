using System.Text.Json;

namespace AstrumLoom.Rhythm;

public sealed record RhythmNote(int Id, int Lane, long TimeUs, long? EndUs = null);
public sealed class RhythmChart
{
    public IReadOnlyList<RhythmNote> Notes { get; }
    public string Id { get; }
    public RhythmChart(string id, IEnumerable<RhythmNote> notes)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        var data = notes.OrderBy(n => n.TimeUs).ThenBy(n => n.Id).ToArray();
        if (data.Any(n => n.Lane < 0 || n.EndUs.HasValue && n.EndUs <= n.TimeUs) ||
            data.Select(n => n.Id).Distinct().Count() != data.Length) throw new ArgumentException("Invalid notes.");
        foreach (var lane in data.GroupBy(n => n.Lane))
        {
            long? heldUntil = null;
            foreach (var note in lane)
            {
                if (heldUntil.HasValue && note.TimeUs <= heldUntil) throw new ArgumentException("Overlapping hold lane.");
                heldUntil = note.EndUs;
            }
        }
        Notes = Array.AsReadOnly(data);
    }
}
public sealed record JudgeSettings(long EarlyUs = 100_000, long LateUs = 100_000,
    long ReleaseEarlyUs = 100_000, long ReleaseLateUs = 100_000,
    long InputCorrectionUs = 0, long ChartOffsetUs = 0);
public readonly record struct JudgeInput(long SongUs, long Sequence, int Lane, InputAction Action);
public enum Judgement { Hit, Miss, HoldComplete, HoldBroken }
public sealed record JudgeResult(int NoteId, Judgement Kind, long? ErrorUs);

/// <summary>Single consumer. Enqueue arrival batches, then advance the inclusive committed watermark.</summary>
public sealed class JudgeSession
{
    private readonly RhythmChart chart;
    private readonly JudgeSettings settings;
    private readonly PriorityQueue<JudgeInput, (long, long)> pending = new();
    private readonly HashSet<long> seen = new();
    private readonly HashSet<int> held = new();
    private readonly HashSet<int> started = new();
    private readonly HashSet<int> finished = new();
    private readonly Dictionary<int, RhythmNote> holds = new();
    private readonly List<JudgeResult> results = new();
    public IReadOnlyList<JudgeResult> Results => results.AsReadOnly();
    public long? Watermark { get; private set; }
    public string? Failure { get; private set; }
    public bool Complete => Failure == null && finished.Count == chart.Notes.Count;
    public JudgeSession(RhythmChart chart, JudgeSettings? settings = null)
    {
        this.chart = chart;
        this.settings = settings ?? new();
        if (this.settings.EarlyUs < 0 || this.settings.LateUs < 0 || this.settings.ReleaseEarlyUs < 0 || this.settings.ReleaseLateUs < 0)
            throw new ArgumentOutOfRangeException(nameof(settings));
    }
    public void Abort(string reason) { Failure ??= reason; held.Clear(); holds.Clear(); pending.Clear(); }
    public void Enqueue(JudgeInput input)
    {
        if (Failure != null) return;
        if (!seen.Add(input.Sequence)) { Abort("Duplicate input sequence."); return; }
        if (Watermark.HasValue && input.SongUs <= Watermark) { Abort("Input arrived after commitment."); return; }
        pending.Enqueue(input, (input.SongUs, input.Sequence));
    }
    public void AdvanceTo(long watermark)
    {
        if (Failure != null) return;
        if (Watermark.HasValue && watermark < Watermark) throw new ArgumentException("Watermark moved backwards.");
        while (pending.TryPeek(out var input, out _) && input.SongUs <= watermark)
        {
            pending.Dequeue();
            Expire(input.SongUs);
            Apply(input);
            if (Failure != null) return;
        }
        Expire(watermark);
        Watermark = watermark;
    }
    private long Error(long time, long note) => checked(checked(time + settings.InputCorrectionUs) - checked(note + settings.ChartOffsetUs));
    private void Expire(long time)
    {
        foreach (var note in chart.Notes)
        {
            if (!started.Contains(note.Id) && Error(time, note.TimeUs) > settings.LateUs)
            { started.Add(note.Id); finished.Add(note.Id); results.Add(new(note.Id, Judgement.Miss, null)); }
            else if (holds.TryGetValue(note.Lane, out var hold) && hold.Id == note.Id && Error(time, note.EndUs!.Value) > settings.ReleaseLateUs)
            { holds.Remove(note.Lane); finished.Add(note.Id); results.Add(new(note.Id, Judgement.HoldBroken, null)); }
        }
    }
    private void Apply(JudgeInput input)
    {
        if (input.Action == InputAction.Reset)
        {
            if (holds.Count > 0) { Abort("Hold interrupted."); return; }
            held.Clear(); return;
        }
        if (input.Action == InputAction.Up)
        {
            held.Remove(input.Lane);
            if (holds.Remove(input.Lane, out var hold))
            {
                long error = Error(input.SongUs, hold.EndUs!.Value);
                results.Add(new(hold.Id, error >= -settings.ReleaseEarlyUs && error <= settings.ReleaseLateUs ? Judgement.HoldComplete : Judgement.HoldBroken, error));
                finished.Add(hold.Id);
            }
            return;
        }
        if (!held.Add(input.Lane)) return;
        if (holds.ContainsKey(input.Lane)) return;
        foreach (var note in chart.Notes)
        {
            if (note.Lane != input.Lane || started.Contains(note.Id)) continue;
            long error = Error(input.SongUs, note.TimeUs);
            if (error < -settings.EarlyUs || error > settings.LateUs) continue;
            started.Add(note.Id); results.Add(new(note.Id, Judgement.Hit, error));
            if (note.EndUs.HasValue) holds.Add(note.Lane, note); else finished.Add(note.Id);
            break;
        }
    }
}

public sealed record ReplayOperation(JudgeInput? Input = null, long? Watermark = null, string? Abort = null);
public sealed record RhythmReplay(int Version, string ChartHash, string AudioHash,
    JudgeSettings Settings, RhythmNote[] Notes, ReplayOperation[] Operations)
{
    public string ToJson() => JsonSerializer.Serialize(this);
    public static RhythmReplay FromJson(string json) => JsonSerializer.Deserialize<RhythmReplay>(json) ?? throw new FormatException("Empty replay.");
    public JudgeSession Play()
    {
        if (Version != 1) throw new NotSupportedException("Replay version.");
        var judge = new JudgeSession(new(ChartHash, Notes), Settings);
        foreach (var op in Operations)
        {
            if ((op.Input.HasValue ? 1 : 0) + (op.Watermark.HasValue ? 1 : 0) + (op.Abort != null ? 1 : 0) != 1) throw new FormatException("Invalid replay operation.");
            if (op.Input.HasValue) judge.Enqueue(op.Input.Value);
            if (op.Watermark.HasValue) judge.AdvanceTo(op.Watermark.Value);
            if (op.Abort != null) judge.Abort(op.Abort);
        }
        return judge;
    }
}
