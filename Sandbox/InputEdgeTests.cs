using AstrumLoom;

namespace Sandbox;

/// <summary>
/// KeyEdgeBuffer（IInput の Buffer()/Update() の土台）の取りこぼし検証。
///
/// <para>
/// このバグは「Buffer() が押下と解放の両方を書いたあとに Update() が走る」ときにだけ出ます。
/// VirtualInput 経由の合成入力は InputBridge が押下集合から直接エッジを作るので、
/// この経路を一切通りません。だから見本帳シーンを --mt で走らせても検出できず、
/// ここでバッファ本体を直接叩いて確かめます。
/// </para>
/// </summary>
internal static class InputEdgeTests
{
    private const int Idx = 0;

    /// <summary>取り込み（描画）が確定（更新）より速い場合。1 窓に押下と解放が収まっても両方観測できること。</summary>
    public static bool DrawFasterThanUpdate()
    {
        var b = new KeyEdgeBuffer(1);
        b.Sample(Idx, true);
        b.Sample(Idx, false);   // ここまでが 1 回の Update の間に起きる

        b.Commit();
        if (!b.GetKeyDown(Idx)) return false;   // 以前はここが false で入力が丸ごと消えていた
        b.Commit();
        if (!b.GetKeyUp(Idx)) return false;
        b.Commit();
        return b.GetState(Idx) == 0;
    }

    /// <summary>取り込みと確定が 1:1 で交互に走る単一スレッド構成。遷移が以前と同一であること（--replay がずれない条件）。</summary>
    public static bool LockStepUnchanged()
    {
        var b = new KeyEdgeBuffer(1);
        int[] expected = [1, 2, 2, -1, 0];
        bool[] downs = [true, true, true, false, false];
        for (int i = 0; i < downs.Length; i++)
        {
            b.Sample(Idx, downs[i]);
            b.Commit();
            if (b.GetState(Idx) != expected[i]) return false;
        }
        return true;
    }

    /// <summary>確定（更新）が取り込み（描画）より速い場合。押下が何度も再発火せず保持になること。</summary>
    public static bool UpdateFasterThanDraw()
    {
        var b = new KeyEdgeBuffer(1);
        b.Sample(Idx, true);
        b.Commit();
        if (!b.GetKeyDown(Idx)) return false;
        for (int i = 0; i < 5; i++)
        {
            b.Commit();
            if (b.GetState(Idx) != 2) return false;   // 取り込みが来なくても保持のまま
        }
        b.Sample(Idx, false);
        b.Commit();
        return b.GetKeyUp(Idx);
    }

    /// <summary>1 窓のなかで 2 回叩いた場合。押下→解放→押下→解放の順に 4 フレームへ分けて観測されること。</summary>
    public static bool DoubleTapInOneWindow()
    {
        var b = new KeyEdgeBuffer(1);
        b.Sample(Idx, true);
        b.Sample(Idx, false);
        b.Sample(Idx, true);
        b.Sample(Idx, false);

        int[] expected = [1, -1, 1, -1, 0];
        foreach (int want in expected)
        {
            b.Commit();
            if (b.GetState(Idx) != want) return false;
        }
        return true;
    }

    /// <summary>溜め込みの上限を超えた分は捨てられること（際限なく溜まって押しっぱなしに見えないこと）。</summary>
    public static bool PendingIsCapped()
    {
        var b = new KeyEdgeBuffer(1);
        for (int i = 0; i < KeyEdgeBuffer.PendingCapacity * 4; i++)
        {
            b.Sample(Idx, true);
            b.Sample(Idx, false);
        }
        return b.PendingEdges(Idx) == KeyEdgeBuffer.PendingCapacity;
    }

    /// <summary>
    /// 取り込みと確定を別スレッドで同時に回しても、押した回数と観測できた押下エッジの数が一致すること。
    /// 溜め込みの上限を超えると設計上は捨てるので、上限に余裕がある間だけ次の打鍵へ進む。
    /// </summary>
    public static bool ConcurrentNoLoss(int taps = 100)
    {
        var b = new KeyEdgeBuffer(1);
        int produced = 0;
        bool done = false;

        var sampler = new Thread(() =>
        {
            for (int i = 0; i < taps; i++)
            {
                // 上限に張り付いていると設計上エッジを捨てるので、掃けるまで待ってから叩く。
                while (b.PendingEdges(Idx) >= KeyEdgeBuffer.PendingCapacity - 1) Thread.Yield();

                b.Sample(Idx, true);
                b.Sample(Idx, false);
                Interlocked.Increment(ref produced);
            }
            Volatile.Write(ref done, true);
        })
        { IsBackground = true, Name = "InputEdgeTests.Sampler" };

        sampler.Start();

        int downs = 0;
        while (true)
        {
            b.Commit();
            if (b.GetKeyDown(Idx)) downs++;
            // 取り込みが終わり、溜まったエッジも吐き切り、状態も戻ったら終了。
            if (Volatile.Read(ref done) && b.PendingEdges(Idx) == 0 && b.GetState(Idx) == 0)
                break;
            // これはゲームの更新スレッド上で走る。素に回すと取り込み側を待つ間ずっとループを
            // 占有してしまい、実時間キャッチアップが暴発して他の検証を巻き添えにする。
            if (b.PendingEdges(Idx) == 0) Thread.Yield();
        }
        sampler.Join();

        return downs == Volatile.Read(ref produced) && downs == taps;
    }
}
