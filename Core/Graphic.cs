namespace AstrumLoom;

public interface ITexture
{
    int Width { get; }
    int Height { get; }
}

public interface IGraphics
{
    ITexture LoadTexture(string path);
    void UnloadTexture(ITexture texture);

    void BeginFrame();
    void Clear(Color color);

    void DrawTexture(
        ITexture texture,
        float x, float y,
        float scaleX = 1f,
        float scaleY = 1f,
        float rotationRad = 0f);

    void EndFrame();
}