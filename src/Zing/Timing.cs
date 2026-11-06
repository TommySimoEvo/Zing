using System.Diagnostics;

namespace Zing;

/// <summary>Tempi di avvio (ms dall'avvio del processo), riportati dal self-test.</summary>
internal static class Timing
{
    private static readonly DateTime ProcessStart = Process.GetCurrentProcess().StartTime;
    private static readonly List<(string Step, double Ms)> Marks = new();

    public static void Mark(string step)
    {
        double ms = (DateTime.Now - ProcessStart).TotalMilliseconds;
        lock (Marks) Marks.Add((step, Math.Round(ms)));
    }

    public static Dictionary<string, double> Snapshot()
    {
        lock (Marks)
        {
            var d = new Dictionary<string, double>();
            foreach (var (step, ms) in Marks) d.TryAdd(step, ms);
            return d;
        }
    }
}
