namespace AstrumLoom;

/// <summary>
/// 更新側で完成させた変更不可の状態を描画へ公開します。公開後は状態とその配下を変更しないでください。
/// 描画開始時にReadを一度呼び、そのフレームでは同じ状態を使用します。
/// </summary>
public sealed class RenderState<T>(T initial) where T : class
{
    private T _value = initial ?? throw new ArgumentNullException(nameof(initial));
    public T Read() => Volatile.Read(ref _value);
    public void Publish(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Volatile.Write(ref _value, value);
    }
}

/// <summary>短いロックで受け付ける有界キュー。ユーザー処理はロック外で実行する。</summary>
internal sealed class MainThreadQueue
{
    private readonly object _gate = new();
    private readonly Queue<(Action? Action, string? Key)> _queue = new();
    private readonly Dictionary<string, Action> _latest = new();
    public int Count { get { lock (_gate) return _queue.Count; } }

    public bool TryPost(Action action, int capacity, string? key = null, bool replace = true)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            if (key != null && _latest.ContainsKey(key))
            {
                if (replace) _latest[key] = action;
                return true;
            }
            if (_queue.Count >= capacity) return false;
            if (key != null) _latest.Add(key, action);
            _queue.Enqueue((key == null ? action : null, key));
            return true;
        }
    }

    public bool TryTake(out Action action)
    {
        lock (_gate)
        {
            if (!_queue.TryDequeue(out var item)) { action = null!; return false; }
            action = item.Key == null ? item.Action! : _latest[item.Key];
            if (item.Key != null) _latest.Remove(item.Key);
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate) { _queue.Clear(); _latest.Clear(); }
    }
}