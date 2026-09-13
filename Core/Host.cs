namespace AstrumLoom;

/// <summary>ゲーム設定・プラットフォーム・GameRunnerをまとめて保持し、起動と後始末（Dispose）の窓口になる。</summary>
public sealed class GameHost : IDisposable
{
    public GameConfig Config { get; }
    public IGamePlatform Platform { get; }
    public IGame Game { get; }

    private readonly GameRunner _runner;

    public GameHost(
        GameConfig config,
        IGamePlatform platform,
        IGame game)
    {
        Config = config;
        Platform = platform;
        Game = game;

        // 更新の上限は描画・VSyncから独立。単一スレッドでは描画側だけで待つ。
        Platform.Time.TargetFps = config.TargetFps;
        Platform.UTime.TargetFps = config.UseMultiThreadUpdate ? config.UpdateTargetFps : 0f;
        _runner = new GameRunner(platform, game, config);
    }

    public void Run() => _runner.Run();

    public void Dispose() => Platform.Dispose();
}

/// <summary>
/// テクスチャ・サウンドなど、非同期でロード/破棄しうるリソースの共通基盤。
/// 実際のロード/破棄はメインスレッドで行う必要があるバックエンド（DxLib/raylib等）が多いため、
/// 別スレッドから呼ばれた場合は「メインスレッドで後から実行」するよう遅延させる。
/// </summary>
public abstract class AsyncLoadableBase
{
    private readonly object _gate = new();
    private static readonly System.Collections.Concurrent.ConcurrentQueue<AsyncLoadableBase> Pending = new();
    private int _queued;
    private int _asyncState = State_Failed;
    private bool _disposeRequested, _released, _started, _loadInvoked, _explicitLoading;
    private long _startTicks;
    private IDisposable? _obj;
    private Func<bool>? _loadfunc, _disposefunc, _background, _backgroundCheck;
    private Task<bool>? _preparation;
    public int TimeoutMs { get; set; } = 60000;
    protected static bool IsMainThread => Environment.CurrentManagedThreadId == AstrumCore.MainThreadId;
    protected enum LoadState { Failed = -1, Loading = 0, Ready = 1, Disposed = -2 }
    protected const int State_Failed = -1, State_Loading = 0, State_Success = 1, State_Disposed = -2;
    protected LoadState State => (LoadState)Volatile.Read(ref _asyncState);
    protected bool LoadReady => State == LoadState.Ready;
    protected bool LoadFailed => State == LoadState.Failed;
    // Loaded means the request has settled; callers must use IsReady to test success.
    protected bool LoadFinished => State is LoadState.Ready or LoadState.Failed;
    protected bool Disposed => State == LoadState.Disposed;
    protected bool Loading => State == LoadState.Loading;

    protected void WriteState(int state)
    {
        lock (_gate)
        {
            if (_disposeRequested) return;
            if (state == State_Loading && _loadInvoked) _explicitLoading = true;
            Volatile.Write(ref _asyncState, state);
        }
    }

    protected void DisposeAsync(Func<bool>? disposeAction = null)
    {
        lock (_gate)
        {
            if (disposeAction != null) _disposefunc = disposeAction;
            _disposeRequested = true;
            Volatile.Write(ref _asyncState, State_Disposed);
            if (_released) return;
            if (!IsMainThread)
            {
                if (_obj != null) AstrumCore.RequestDispose(_obj);
                return;
            }
            _released = true;
            try
            {
                if (_disposefunc?.Invoke() == false) Log.Warning(GetType().Name + " Dispose returned false.");
            }
            catch (Exception ex) { Log.Error(GetType().Name + " Dispose failed: " + ex.Message); }
        }
    }

    protected void LoadAsync(IDisposable obj, Func<bool>? loadAction, Func<bool>? bgloadAction, Func<bool>? bgcheckFunc = null)
    {
        lock (_gate) { _background = bgloadAction; _backgroundCheck = bgcheckFunc; }
        LoadAsync(obj, loadAction);
    }
    protected void LoadAsync(IDisposable obj, Func<bool>? loadAction = null)
    {
        lock (_gate) _obj = obj;
        LoadAsync(loadAction);
    }
    public void LoadAsync(Func<bool>? loadAction = null)
    {
        lock (_gate)
        {
            if (_started || _disposeRequested) return;
            _started = true;
            _loadfunc = loadAction;
            _startTicks = Environment.TickCount64;
            Volatile.Write(ref _asyncState, State_Loading);
            if (!IsMainThread && _background != null && AstrumCore.WindowConfig?.AsyncResourceLoad != false)
            {
                var prepare = _background;
                _preparation = Task.Run(() =>
                {
                    try { return prepare(); }
                    catch (Exception ex) { Log.Error(GetType().Name + " Preparation failed: " + ex.Message); return false; }
                });
            }
        }
        PumpAsync();
        if (Loading) Schedule();
    }
    private void Schedule()
    {
        if (Interlocked.Exchange(ref _queued, 1) == 0) Pending.Enqueue(this);
    }
    /// <summary>状態getterとは独立して、メインループが保留中リソースを進める。</summary>
    internal static void PumpPending()
    {
        if (!IsMainThread) return;
        int count = System.Math.Min(Pending.Count, System.Math.Max(1, AstrumCore.WindowConfig?.MainThreadActionsPerFrame ?? 128));
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < count && Pending.TryDequeue(out var resource); i++)
        {
            Interlocked.Exchange(ref resource._queued, 0);
            if (resource.Loading)
            {
                if (resource._obj is IResourse r) r.Pump(); else resource.PumpAsync();
                if (resource.Loading) resource.Schedule();
            }
            if (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds >=
                (AstrumCore.WindowConfig?.MainThreadActionBudgetMs ?? 2)) break;
        }
    }
    internal static void CancelPending()
    {
        while (Pending.TryDequeue(out var resource)) resource.Cancel();
    }
    private void Cancel()
    {
        if (_obj != null) _obj.Dispose(); else DisposeAsync();
    }
    protected void PumpAsync()
    {
        if (!IsMainThread) return;
        lock (_gate)
        {
            if (_disposeRequested || !Loading) return;
            if (TimeoutMs > 0 && Environment.TickCount64 - _startTicks >= TimeoutMs)
            {
                Cancel();
                return;
            }
            if (_loadInvoked) return;
            if (_preparation != null)
            {
                if (!_preparation.IsCompleted) return;
                if (!_preparation.GetAwaiter().GetResult()) { WriteState(State_Failed); return; }
            }
            try
            {
                if (_backgroundCheck?.Invoke() == false) return;
                _loadInvoked = true;
                _explicitLoading = false;
                bool ok = _loadfunc?.Invoke() ?? true;
                if (!ok) WriteState(State_Failed);
                else if (!_explicitLoading) WriteState(State_Success);
            }
            catch (Exception ex) { WriteState(State_Failed); Log.Error(GetType().Name + " Load failed: " + ex.Message); }
        }
    }
    protected bool FileCheck(string path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return true;
        if (!string.IsNullOrEmpty(path)) Log.Debug($"{GetType().Name}: not found: {path}");
        return false;
    }
}
/// <summary>Texture/Soundなど、非同期ロード可能なリソースが実装する共通インターフェース。</summary>
public interface IResourse : IDisposable
{
    bool IsReady { get; }
    bool IsFailed { get; }
    bool Loaded { get; }
    bool Enable { get; }
    void Pump();

    string Path { get; }
}
