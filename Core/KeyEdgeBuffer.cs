namespace AstrumLoom;

/// <summary>
/// バックエンド共通の「押下エッジを取りこぼさない」キー状態バッファ。
///
/// <para>
/// <see cref="Sample"/> は生の押下状態を取り込む側（メインスレッドの <c>IInput.Buffer()</c>）が呼び、
/// <see cref="Commit"/> は状態遷移を確定させる側（更新スレッドの <c>IInput.Update()</c>）が呼びます。
/// UseMultiThreadUpdate=true では両者が別スレッド・別の回数で回るため、
/// 「Sample が押下と解放の両方を書いたあとに Commit が走る」ことが普通に起きます。
/// 素直に「今の生状態」だけを見て遷移を作ると、この窓に収まった打鍵は
/// 押下も解放も一度も観測されず、入力が丸ごと消えます。
/// </para>
///
/// <para>
/// そこで <see cref="Sample"/> 側で押下の立ち上がりを件数として溜め（<c>_pending</c>）、
/// <see cref="Commit"/> はそれを 1 フレームに 1 件ずつ吐き出します。
/// 溜まった打鍵は「押下 → 解放」の順で必ず 2 フレームに分けて観測されるので、
/// Push も Left も落ちません。
/// </para>
///
/// <para>
/// Sample と Commit が 1:1 で交互に走る単一スレッド構成では、
/// 立ち上がりの件数が常に 0 か 1 なので、以前の実装と完全に同じ遷移になります。
/// 入力再生（--replay）がずれないのはこのためです。
/// </para>
/// </summary>
public sealed class KeyEdgeBuffer
{
    /// <summary>溜め込む押下エッジの上限。異常時に際限なく溜まって「離してもしばらく効き続ける」のを防ぐ。</summary>
    private const int MaxPending = 8;

    // Sample 側だけが読み書きする（立ち上がり判定用の前回値）。
    private readonly bool[] _samplePrev;
    // Sample が書き、Commit が読む生の押下状態。
    private readonly bool[] _raw;
    // Sample が積み、Commit が 1 件ずつ消費する押下エッジの件数。
    private readonly int[] _pending;
    // 上限に達してエッジを捨てたことを示す印。捨てたあとも押しっぱなしなら押下として復帰させる。
    private readonly int[] _overflow;
    // Commit だけが書く確定済みの遷移（1=押下開始, 2=保持, -1=離鍵, 0=非押下）。
    private readonly int[] _state;

    public KeyEdgeBuffer(int length)
    {
        _samplePrev = new bool[length];
        _raw = new bool[length];
        _pending = new int[length];
        _overflow = new int[length];
        _state = new int[length];
    }

    public int Length => _state.Length;

    /// <summary>生の押下状態を 1 件取り込みます。押下の立ち上がりならエッジを 1 件積みます。</summary>
    public void Sample(int index, bool isDown)
    {
        if (isDown && !_samplePrev[index])
        {
            if (Volatile.Read(ref _pending[index]) < MaxPending)
                Interlocked.Increment(ref _pending[index]);
            else
                Volatile.Write(ref _overflow[index], 1);   // 捨てた。復帰は Commit 側で面倒を見る。
        }

        _samplePrev[index] = isDown;
        Volatile.Write(ref _raw[index], isDown);
    }

    /// <summary>取り込み済みの生状態と溜まったエッジから、各キーの遷移状態を 1 フレーム分確定させます。</summary>
    public void Commit()
    {
        for (int i = 0; i < _state.Length; i++)
        {
            bool raw = Volatile.Read(ref _raw[i]);
            int prev = _state[i];
            int next;

            if (prev > 0)
            {
                // 参照側は「押されている」と思っている状態。
                // エッジが残っている＝この窓の中で一度離されて押し直されたということなので、
                // まず離鍵を出す。押し直しは次の Commit でエッジを消費して観測される。
                next = (Volatile.Read(ref _pending[i]) > 0 || !raw) ? -1 : 2;
            }
            else if (TryConsumePending(i))
            {
                next = 1;
            }
            else if (raw && Interlocked.Exchange(ref _overflow[i], 0) != 0)
            {
                // 上限を超えてエッジを捨てたのに、まだ押されたまま。押下として拾い直す。
                next = 1;
            }
            else
            {
                // ここで raw を見て押下を作ってはいけない。Sample は「エッジを積む → 生状態を書く」の
                // 順で走るが、Commit 側から見ると生状態が先に見えることがあり、
                // 「押されているのにエッジがまだ 0」という一瞬が普通に起きる。
                // その一瞬を押下として拾うと、直後にエッジを消費したぶんと合わせて
                // 1 回の打鍵が 2 回の Push になる（実測で 300 打鍵が 308 回 Push になった）。
                // エッジは消えずに残っているので、次の Commit で必ず観測される。
                next = 0;
            }

            _state[i] = next;
        }
    }

    private bool TryConsumePending(int index)
    {
        if (Volatile.Read(ref _pending[index]) <= 0) return false;
        Interlocked.Decrement(ref _pending[index]);
        return true;
    }

    /// <summary>まだ吐き出していない押下エッジの件数。取りこぼし検証用。</summary>
    public int PendingEdges(int index)
        => (uint)index < (uint)_pending.Length ? Volatile.Read(ref _pending[index]) : 0;

    /// <summary>溜め込める押下エッジの上限。</summary>
    public static int PendingCapacity => MaxPending;

    /// <summary>確定済みの遷移状態（1=押下開始, 2=保持, -1=離鍵, 0=非押下）。範囲外は 0。</summary>
    public int GetState(int index)
        => (uint)index < (uint)_state.Length ? _state[index] : 0;

    public bool GetKey(int index) => GetState(index) > 0;
    public bool GetKeyDown(int index) => GetState(index) == 1;
    public bool GetKeyUp(int index) => GetState(index) < 0;
}
