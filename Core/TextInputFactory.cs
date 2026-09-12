namespace AstrumLoom;

/// <summary>
/// バックエンドから TextEnter を組み立てるための入口。
/// Windows では IMM32 実装（<see cref="WindowsImeTextInput"/>）を優先し、
/// 使えない環境ではバックエンド既定の <see cref="ITextInput"/> にフォールバックする。
/// </summary>
public static class TextInputFactory
{
    /// <summary>
    /// テキスト入力セッションを作る。
    /// </summary>
    /// <param name="window">ウィンドウハンドル（HWND）。0 ならフォールバックのみ。</param>
    /// <param name="fallback">IMM32 が使えないときに使うバックエンド既定の実装。</param>
    /// <param name="time">キャレット点滅に使う時間ソース。</param>
    /// <param name="useSystemIme">OS の IME を使うか。GameConfig.UseSystemIme を渡す。</param>
    public static TextEnter Create(nint window, ITextInput fallback, ITime time, bool useSystemIme = true)
    {
        if (useSystemIme && WindowsImeTextInput.TryCreate(window) is { } ime)
            return new TextEnter(ime, time);

        if (useSystemIme && OperatingSystem.IsWindows())
            Log.Warning("OS の IME を利用できないため、バックエンド既定のテキスト入力にフォールバックします。");

        return new TextEnter(fallback, time);
    }
}
