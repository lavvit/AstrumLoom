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