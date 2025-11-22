namespace AstrumLoom;

public interface IGamePlatform : IDisposable
{
    GraphicsBackendKind BackendKind { get; }

    IGraphics Graphics { get; }
    IInput Input { get; }
    ITime Time { get; }
    TextEnter TextInput { get; }
    IMouse Mouse { get; }

    bool ShouldClose { get; }

    void PollEvents();

    void Close();

    ITexture LoadTexture(string path);
    ISound LoadSound(string path, bool streaming);
}

public enum GraphicsBackendKind
{
    DxLib,
    RayLib,
    // 将来: Vulkan, OpenGL など
}