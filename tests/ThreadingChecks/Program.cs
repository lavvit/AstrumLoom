using AstrumLoom;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}
var queue = new MainThreadQueue();
int observed = 0;
Check(queue.TryPost(() => observed = 1, 2, "value"), "initial keyed post");
for (int i = 2; i <= 100000; i++)
{
    int value = i;
    CheckQuiet(queue.TryPost(() => observed = value, 2, "value"));
}
Check(queue.Count == 1, "100000 writes occupy one slot");
queue.TryPost(() => observed++, 2);
Check(!queue.TryPost(() => { }, 2), "overflow is rejected");
Check(queue.TryTake(out var first), "dequeue latest");
first();
Check(observed == 100000, "latest value replaces old action");
queue.TryTake(out var second);
second();
Check(observed == 100001, "FIFO after keyed value");
queue.TryPost(() => observed = 1, 2, "first-wins", false);
queue.TryPost(() => observed = 2, 2, "first-wins", false);
queue.TryTake(out first);
first();
Check(observed == 1, "draw hooks retain first registration");
queue.Clear();
Parallel.For(0, 100000, i => queue.TryPost(() => { }, 64));
Check(queue.Count == 64, "concurrent producers respect capacity");
queue.Clear();
var state = new RenderState<Pair>(new(0, 0));
long bad = 0;
Parallel.Invoke(
    () => { for (int i = 0; i < 1000000; i++) state.Publish(new(i, -i)); },
    () => { for (int i = 0; i < 1000000; i++) { var p = state.Read(); if (p.A != -p.B) Interlocked.Increment(ref bad); } });
Check(bad == 0, "snapshot fields remain consistent under concurrent publication");
Console.WriteLine("All threading checks passed.");
static void CheckQuiet(bool condition) { if (!condition) throw new Exception("key replacement rejected"); }
sealed record Pair(long A, long B);
