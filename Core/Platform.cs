namespace AstrumLoom;

/// <summary>
/// バックエンド（DxLib/raylib等）が実装するプラットフォーム層の統合インターフェース。
/// 描画・入力・時間・音・パッド等、フレームワークが必要とする全てのプラットフォーム依存機能をここに集約する。
/// </summary>
public interface IGamePlatform : IDisposable
{
    GraphicsBackendKind BackendKind { get; }

    IGraphics Graphics { get; }
    IInput Input { get; }
    ITime Time { get; }
    ITime UTime { get; }// Update 用
    TextEnter TextInput { get; }
    IMouse Mouse { get; }
    IController Controller { get; }

    bool ShouldClose { get; }

    void PollEvents();

    void Close();

    ITexture LoadTexture(string path);
    ITexture CreateTexture(int width, int height, Action callback);
    /// <summary>
    /// メモリ上のエンコード済み画像バイト列（PNG等）からテクスチャを作る。
    /// SkiaSharp等、外部の描画モジュールでオフスクリーンに焼いた絵をそのまま取り込むための入口。
    /// 既定実装は未対応（NotSupportedException）。対応するバックエンドだけオーバーライドする。
    /// </summary>
    ITexture LoadTextureFromMemory(byte[] data, string ext)
        => throw new NotSupportedException($"{BackendKind} は LoadTextureFromMemory に対応していません。");
    /// <summary>
    /// 生のRGBA32ピクセル列（幅×高さ×4バイト、エンコード無し）から直接テクスチャを作る。
    /// PNG等のエンコード/デコードを介さない分、LoadTextureFromMemory より高速。
    /// 既定実装は未対応（NotSupportedException）。対応するバックエンドだけオーバーライドする。
    /// </summary>
    ITexture LoadTextureFromPixels(int width, int height, byte[] rgba)
        => throw new NotSupportedException($"{BackendKind} は LoadTextureFromPixels に対応していません。");
    ISound LoadSound(string path, bool streaming);
    IMovie LoadMovie(string path);

    void SetVSync(bool enabled);
    void SetDragDrop(bool enabled);
    string[] DropFiles { get; }

    bool IsActive { get; }
    double? SystemFPS { get; }
}

public enum GraphicsBackendKind
{
    DxLib,
    RayLib,
    // 将来: Vulkan, OpenGL など
}