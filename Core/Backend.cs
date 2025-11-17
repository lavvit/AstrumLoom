namespace AstrumLoom;

public interface IGamePlatform : IDisposable
{
    GraphicsBackendKind BackendKind { get; }

    IGraphics Graphics { get; }
    IInput Input { get; }
    ITime Time { get; }

    bool ShouldClose { get; }

    void PollEvents();
}

public enum GraphicsBackendKind
{
    DxLib,
    RayLib,
    // 将来: Vulkan, OpenGL など
}