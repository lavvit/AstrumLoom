namespace AstrumLoom;

public interface IGamePlatform : IDisposable
{
    GraphicsBackendKind BackendKind { get; }

    IGraphics Graphics { get; }
    IInput Input { get; }
    ITime Time { get; }
    ITime UTime { get; }// Update 用
    TextEnter TextInput { get; }
    IMouse Mouse { get; }

    bool ShouldClose { get; }

    void PollEvents();

    void Close();

    ITexture LoadTexture(string path);
    ITexture CreateTexture(int width, int height, Action callback);
    ISound LoadSound(string path, bool streaming);
    IMovie LoadMovie(string path);

    void SetVSync(bool enabled);
    bool IsActive { get; }
}

public enum GraphicsBackendKind
{
    DxLib,
    RayLib,
    // 将来: Vulkan, OpenGL など
}