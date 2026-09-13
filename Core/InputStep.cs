namespace AstrumLoom;

/// <summary>入力確定1回につきエッジを最初の論理ステップだけに公開する。</summary>
public static class InputStep
{
    [ThreadStatic] private static bool _edgesSuppressed;
    public static bool EdgesSuppressed { get => _edgesSuppressed; internal set => _edgesSuppressed = value; }
}
