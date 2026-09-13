using System.Numerics;

namespace AstrumLoom.Rhythm;

public sealed record TempoChange(long Pulse, long BpmNumerator, long BpmDenominator = 1, long StopUs = 0);
public sealed record PulseNote(int Id, int Lane, long Pulse, long? EndPulse = null);
public static class ChartTiming
{
    /// <summary>Stops precede notes at the same pulse. The new BPM applies after that pulse.</summary>
    public static RhythmChart Compile(string id, int pulsesPerBeat, IEnumerable<TempoChange> tempo, IEnumerable<PulseNote> notes)
    {
        var changes = tempo.OrderBy(t => t.Pulse).ToArray();
        if (pulsesPerBeat <= 0 || changes.Length == 0 || changes[0].Pulse != 0 ||
            changes.Any(t => t.Pulse < 0 || t.BpmNumerator <= 0 || t.BpmDenominator <= 0 || t.StopUs < 0) ||
            changes.Select(t => t.Pulse).Distinct().Count() != changes.Length) throw new ArgumentException("Invalid tempo map.");
        long Convert(long pulse)
        {
            if (pulse < 0) throw new ArgumentOutOfRangeException(nameof(pulse));
            BigInteger numerator = 0, denominator = 1;
            for (int i = 0; i < changes.Length && changes[i].Pulse <= pulse; i++)
            {
                var t = changes[i];
                numerator += t.StopUs * denominator;
                long end = i + 1 < changes.Length ? Math.Min(pulse, changes[i + 1].Pulse) : pulse;
                BigInteger d = (BigInteger)pulsesPerBeat * t.BpmNumerator;
                numerator = numerator * d + (BigInteger)(end - t.Pulse) * 60_000_000 * t.BpmDenominator * denominator;
                denominator *= d;
                var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
                numerator /= gcd; denominator /= gcd;
            }
            return RhythmTime.DivideRounded(numerator, denominator);
        }
        return new(id, notes.Select(n => new RhythmNote(n.Id, n.Lane, Convert(n.Pulse), n.EndPulse.HasValue ? Convert(n.EndPulse.Value) : null)));
    }
}
