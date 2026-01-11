namespace AstrumLoom;

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

        Platform.Time.TargetFps = config.TargetFps;
        _runner = new GameRunner(platform, game, config.ShowFpsOverlay, config.ShowMouse);
    }

    public void Run() => _runner.Run();

    public void Dispose() => Platform.Dispose();
}

public abstract class AsyncLoadableBase
{
    // IResource 状態管理 (-1=Failed/Disposed, 0=Loading, 1=Ready)
    private int _asyncState = (int)LoadState.Failed;
    private bool _deferred;
    private long _startTicks;
    private const int DefaultTimeoutMs = 60000;
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;
    protected static bool IsMainThread => Environment.CurrentManagedThreadId == AstrumCore.MainThreadId;

    protected enum LoadState { Failed = -1, Loading = 0, Ready = 1, Disposed = -2 }
    protected LoadState State
    {
        get
        {
            PumpAsync(); // 呼び忘れ対策
            return (LoadState)_state;
        }
    }
    private int _state => Volatile.Read(ref _asyncState);
    protected bool LoadReady => State == LoadState.Ready;
    protected bool LoadFailed => State == LoadState.Failed;
    protected bool LoadFinished => State != LoadState.Loading && !Disposed;
    protected bool Disposed => (LoadState)_state == LoadState.Disposed;
    protected bool Loading => (LoadState)_state == LoadState.Loading;

    protected const int State_Failed = (int)LoadState.Failed;
    protected const int State_Loading = (int)LoadState.Loading;
    protected const int State_Success = (int)LoadState.Ready;
    protected const int State_Disposed = (int)LoadState.Disposed;
    protected void WriteState(int state) => Volatile.Write(ref _asyncState, state);

    private IDisposable? _obj;
    private Func<bool>? _disposefunc;
    protected void DisposeAsync(Func<bool>? disposeAction = null)
    {
        if (disposeAction != null)
            _disposefunc = disposeAction;

        if (_obj == null || Disposed) return;
        if (!IsMainThread)
        {
            // メインスレッドで後から Dispose
            //Log.Debug(GetType().Name + " Disposing on Main thread.");
            AstrumCore.RequestDispose(_obj);
            return;
        }
        // Dispose 処理をここに実装
        try
        {
            if (_disposefunc != null)
            {
                bool result = _disposefunc();
                if (!result)
                {
                    Log.Warning(GetType().Name + " Dispose returned false.");
                    WriteState(State_Failed);
                    return;
                }
            }
            //Log.Debug(GetType().Name + " Disposed.");
            WriteState(State_Disposed);
        }
        catch (Exception ex)
        {
            Log.Error(GetType().Name + " Dispose Failed: " + ex.Message);
            WriteState(State_Failed);
        }
    }

    private Func<bool>? _loadfunc;
    private (Func<bool>? Load, Func<bool>? Check)? _bgloadfuncs;
    protected void LoadAsync(IDisposable obj, Func<bool>? loadAction,
        Func<bool>? bgloadAction, Func<bool>? bgcheckFunc = null)
    {
        _bgloadfuncs = (bgloadAction, bgcheckFunc);
        LoadAsync(obj, loadAction);
    }
    protected void LoadAsync(IDisposable obj, Func<bool>? loadAction = null)
    {
        _obj = obj;
        LoadAsync(loadAction);
    }
    public void LoadAsync(Func<bool>? loadAction = null)
    {
        if (loadAction != null)
            _loadfunc = loadAction;

        if (LoadReady) return; // 既に Ready
        if (!IsMainThread)
        {
            // メインスレッドで後からロード
            if (_bgloadfuncs != null && _bgloadfuncs?.Load != null)
            {
                Task.Run(() =>
                {
                    bool result = _bgloadfuncs?.Load() ?? false;
                    if (result)
                    {
                        WriteState(State_Loading); // Loading
                        return;
                    }
                });
            }
            if (!_deferred)
            {
                _deferred = true;
                WriteState(State_Loading); // Loading
            }
            return;
        }

        if (_loadfunc == null)
        {
            WriteState(State_Success);
            return;
        }
        // 実際のロード処理をここに実装
        // 例: テクスチャの読み込み、サイズの設定など
        try
        {
            _startTicks = Environment.TickCount64;
            bool result = _loadfunc();
            if (!result)
            {
                WriteState(State_Failed);
                return;
            }
            if (_state != State_Loading)
                WriteState(State_Success);
        }
        catch (Exception ex)
        {
            Log.Error(GetType().Name + " Load Failed: " + ex.Message);
            WriteState(State_Failed);
            return;
        }
    }

    protected void PumpAsync()
    {
        // メインスレッドのみが状態更新
        if (!IsMainThread) return;

        // Deferred ロード実行
        if (_deferred)
        {
            _deferred = false;
            LoadAsync();
            return;
        }

        // Loading 中のタイムアウト監視
        if (Volatile.Read(ref _asyncState) == 0)
        {
            long elapsed = Environment.TickCount64 - _startTicks;
            if (TimeoutMs > 0 && elapsed >= TimeoutMs)
            {
                DisposeAsync();
            }
        }
    }

    protected bool FileCheck(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (File.Exists(path))
            return true;
        else
        {
            if (Path.GetFileName(path).Length > 0)
                Log.Debug($"{GetType().Name}: not found: {path}");
            WriteState(State_Failed);
            return false;
        }
    }
}
public interface IResourse : IDisposable
{
    bool IsReady { get; }
    bool IsFailed { get; }
    bool Loaded { get; }
    bool Enable { get; }
    void Pump();

    string Path { get; }
}