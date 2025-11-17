namespace AstrumLoom;

public interface ITime
{
    float DeltaTime { get; }
    float TotalTime { get; }
    float CurrentFps { get; }
    float TargetFps { get; set; }

    void BeginFrame();
    void EndFrame();
}