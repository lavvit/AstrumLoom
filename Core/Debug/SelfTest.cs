using System.Text;

namespace AstrumLoom;

/// <summary>セルフテストの 1 項目の結果。</summary>
public readonly record struct SelfTestResult(string Label, bool Passed, string Detail, long Frame)
{
    public override string ToString()
        => $"[{(Passed ? "PASS" : "FAIL")}] f{Frame,-6} {Label}{(string.IsNullOrEmpty(Detail) ? "" : $"  — {Detail}")}";
}

/// <summary>
/// テスト計画を順番に自動走行し、PASS/FAIL とスクリーンショットを残す仕組み。
/// <code>
/// SelfTest.Wait(30);
/// SelfTest.Shot("title");
/// SelfTest.Check("タイトルが出ている", () =&gt; Scene.NowScene.Name == "TitleScene");
/// SelfTest.Tap(Key.Enter);
/// SelfTest.Wait(60);
/// SelfTest.Check("ゲーム画面に移った", () =&gt; Scene.NowScene.Name == "PlayScene");
/// </code>
/// 計画は <c>AstrumCore.Boot</c> より前に並べます。<c>--selftest</c> を付けて起動すると走ります。
/// </summary>
public static class SelfTest
{
    /// <summary>計画の 1 ステップ。Frames フレーム分だけ Advance で消費される。</summary>
    private abstract class Action_
    {
        public int Frames;
        public virtual void OnStart() { }
        public virtual void OnEnd() { }
    }

    /// <summary>何もせず Frames フレームだけ経過を待つ。</summary>
    private sealed class WaitAction : Action_ { }

    /// <summary>開始時にキーを合成入力で押し、終了時に離す。</summary>
    private sealed class HoldAction : Action_
    {
        public required Key Key;
        public override void OnStart() => VirtualInput.Press(Key);
        public override void OnEnd() => VirtualInput.Release(Key);
    }

    /// <summary>開始時にスクリーンショットを要求する（0 フレームでは撮影要求と描画保存が同フレームにならないため Frames=1）。</summary>
    private sealed class ShotAction : Action_
    {
        public required string Name;
        public override void OnStart() => Snapshot.Request(Name);
    }

    /// <summary>開始時に条件を評価し、結果を記録する。例外も FAIL として扱う。</summary>
    private sealed class CheckAction : Action_
    {
        public required string Label;
        public required Func<bool> Predicate;
        public string Detail = "";
        public override void OnStart()
        {
            bool ok;
            string detail = Detail;
            try
            {
                ok = Predicate();
            }
            catch (Exception ex)
            {
                ok = false;
                detail = $"{ex.GetType().Name}: {ex.Message}";
            }
            Record(Label, ok, detail);
        }
    }

    /// <summary>開始時に任意処理を 1 回実行する。例外は FAIL として記録される。</summary>
    private sealed class DoAction : Action_
    {
        public required string Label;
        public required Action Body;
        public override void OnStart()
        {
            try
            {
                Body();
            }
            catch (Exception ex)
            {
                Record(Label, false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static readonly List<Action_> _plan = [];
    private static readonly List<SelfTestResult> _results = [];

    /// <summary>現在実行中（または次に実行する）計画項目のインデックス。</summary>
    private static int _index;
    /// <summary>現在の項目で残っている待機フレーム数。</summary>
    private static int _framesLeft;
    /// <summary>現在の項目の OnStart が呼ばれ済みか。</summary>
    private static bool _current;
    private static bool _finished;
    /// <summary>計画の走行を開始した論理フレーム。0 は未開始の意味も兼ねるため、Advance の初回に設定する。</summary>
    private static long _startFrame;

    /// <summary><c>--selftest</c> で起動されたか。</summary>
    public static bool Enabled { get; internal set; }

    /// <summary>計画が最後まで走り終わったか。</summary>
    public static bool Finished => _finished;

    /// <summary>安全のための総フレーム上限。超えると FAIL 扱いで打ち切ります。</summary>
    public static long FrameLimit { get; set; } = 60 * 60 * 5; // 5 分相当

    public static IReadOnlyList<SelfTestResult> Results => _results;
    public static int Passed => _results.Count(r => r.Passed);
    public static int Failed => _results.Count(r => !r.Passed);
    public static bool HasPlan => _plan.Count > 0;

    #region 計画の組み立て

    /// <summary>何もせず指定フレーム数だけ待ちます。</summary>
    public static void Wait(int frames)
        => _plan.Add(new WaitAction { Frames = Math.Max(0, frames) });

    /// <summary>キーを指定フレーム数だけ押しっぱなしにします。</summary>
    public static void Hold(Key key, int frames)
        => _plan.Add(new HoldAction { Key = key, Frames = Math.Max(1, frames) });

    /// <summary>キーを 1 フレームだけ押します。</summary>
    public static void Tap(Key key) => Hold(key, 1);

    /// <summary>スクリーンショットを 1 枚撮ります。</summary>
    public static void Shot(string name)
        => _plan.Add(new ShotAction { Name = name, Frames = 1 });

    /// <summary>条件を検査して PASS/FAIL を記録します。</summary>
    public static void Check(string label, Func<bool> predicate)
        => _plan.Add(new CheckAction { Label = label, Predicate = predicate, Frames = 0 });

    /// <summary>条件を検査し、失敗時の補足も残します。</summary>
    public static void Check(string label, Func<bool> predicate, string detailOnFail)
        => _plan.Add(new CheckAction { Label = label, Predicate = predicate, Detail = detailOnFail, Frames = 0 });

    /// <summary>任意の処理を 1 回実行します。例外は FAIL として記録されます。</summary>
    public static void Do(string label, Action body)
        => _plan.Add(new DoAction { Label = label, Body = body, Frames = 0 });

    /// <summary>計画と結果を空にします。</summary>
    public static void Clear()
    {
        _plan.Clear();
        _results.Clear();
        _index = 0;
        _framesLeft = 0;
        _current = false;
        _finished = false;
    }

    #endregion

    /// <summary>結果を 1 件記録します。テストの中から直接呼んでも構いません。</summary>
    public static void Record(string label, bool passed, string detail = "")
    {
        var r = new SelfTestResult(label, passed, detail, AstrumCore.FrameCount);
        _results.Add(r);
        // Log.SelfTest は Console/ファイルには即出すが、画面表示は完了時にまとめて出す
        // （実行中に表示すると毎フレームの描画コストがかさむ上、キャプチャの邪魔にもなる）。
        Log.SelfTest(r.ToString());
    }

    /// <summary>ゲーム更新のあとに 1 フレーム分だけ計画を進めます。</summary>
    internal static void Advance()
    {
        if (!Enabled || _finished) return;
        if (_startFrame == 0) _startFrame = AstrumCore.FrameCount;

        if (AstrumCore.FrameCount - _startFrame > FrameLimit)
        {
            Record("セルフテストの制限フレーム超過", false, $"{FrameLimit} フレームを超えたため打ち切りました。");
            Complete();
            return;
        }

        // 0 フレームの項目は同じフレームで連続して消化する。
        // 進捗しない項目は存在しないので、この while は必ず終わる。
        while (true)
        {
            if (_index >= _plan.Count)
            {
                Complete();
                return;
            }

            var action = _plan[_index];
            if (!_current)
            {
                _current = true;
                _framesLeft = action.Frames;
                action.OnStart();
            }

            if (_framesLeft > 0)
            {
                _framesLeft--;
                return; // このフレームはここまで
            }

            action.OnEnd();
            _current = false;
            _index++;
        }
    }

    /// <summary>計画を完了させ、合成入力の解除とプロセス終了コードの設定を行う。</summary>
    private static void Complete()
    {
        if (_finished) return;
        _finished = true;
        VirtualInput.ReleaseAll();

        // 実行中は画面描画を止めていたぶん、完了時にまとめて表示する。
        // --log-overlay で明示的に off されている場合はそれを尊重する。
        if (DebugSession.Options.LogOverlay != false) Log.DrawOnScreen = true;

        string summary = Failed == 0
            ? $"セルフテスト成功: {Passed} 件すべて PASS"
            : $"セルフテスト失敗: {Failed} 件 FAIL / 全 {_results.Count} 件";
        Log.Write(summary, Failed == 0 ? LogLevel.Info : LogLevel.Error);
        Environment.ExitCode = Failed == 0 ? 0 : 1;
    }

    /// <summary>結果をテキストファイルに書き出します。</summary>
    internal static void SaveReport(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var body = new List<string>
            {
                $"AstrumLoom セルフテスト結果  {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"バックエンド: {AstrumCore.Platform?.BackendKind}",
                $"解像度: {AstrumCore.Width}x{AstrumCore.Height}",
                $"走行フレーム: {AstrumCore.FrameCount}",
                "",
            };
            body.AddRange(_results.Select(r => r.ToString()));
            body.Add("");
            body.Add($"PASS {Passed} / FAIL {Failed} / 合計 {_results.Count}");
            File.WriteAllLines(path, body, new UTF8Encoding(true));
            Console.WriteLine($"結果を書き出しました: {path}");
        }
        catch (Exception ex)
        {
            Log.Error($"セルフテスト結果の書き出しに失敗しました: {ex.Message}");
        }
    }
}
