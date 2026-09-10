using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AstrumLoom;

/// <summary>Windowsの移動・サイズ変更モーダルループ中に、所有スレッドで描画を続ける。</summary>
internal sealed class WindowsModalRenderer : IDisposable
{
    private readonly nint _window;
    private readonly SubclassProc _proc;
    private readonly Action _draw;
    private readonly Action<Exception> _onError;
    private nuint _timer;
    private bool _disposed;
    private const nuint Id = 0x41535452;

    public static WindowsModalRenderer? Attach(nint window, Action draw, Action<Exception> onError)
        => OperatingSystem.IsWindows() && window != 0 ? new(window, draw, onError) : null;

    private WindowsModalRenderer(nint window, Action draw, Action<Exception> onError)
    {
        _window = window;
        _draw = draw;
        _onError = onError;
        _proc = WindowProc;
        if (!SetWindowSubclass(window, _proc, Id, 0))
            throw new Win32Exception("SetWindowSubclass failed.");
    }

    private nint WindowProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        // 例外をネイティブのコールバック境界へ漏らさない。
        try
        {
            if (message == 0x0231) // WM_ENTERSIZEMOVE
            {
                _timer = SetTimer(window, Id, 16, 0);
                if (_timer == 0) throw new Win32Exception("Modal draw timer failed.");
            }
            else if (message == 0x0232) StopTimer(); // WM_EXITSIZEMOVE
            else if (message == 0x0113 && _timer != 0 && wParam == _timer)
            {
                _draw();
                return 0;
            }
            else if (message == 0x0082) Dispose(); // WM_NCDESTROY
        }
        catch (Exception ex)
        {
            StopTimer();
            _onError(ex);
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void StopTimer()
    {
        if (_timer != 0) KillTimer(_window, _timer);
        _timer = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopTimer();
        RemoveWindowSubclass(_window, _proc, Id);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern nuint SetTimer(nint window, nuint id, uint interval, nint callback);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(nint window, nuint id);
}
