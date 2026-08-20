namespace AstrumLoom;

/// <summary>接続中のゲームコントローラ全体を管理するプラットフォーム実装のインターフェース。</summary>
public interface IController
{
    int Count { get; }
    string[] List { get; }
    IJoyPad? GetJoyPad(int index);

    void Update();
    void Buffer();
}

/// <summary>1台分のゲームパッド。ボタン/トリガー/スティックの状態取得と振動出力を提供する。</summary>
public interface IJoyPad
{
    int Index { get; }
    string Name { get; }
    string Product { get; }
    ControllerType Type { get; }

    int[] Button { get; }
    float[] Trigger { get; }
    StickState[] Stick { get; }

    void Update();
    void Buffer();

    bool IsPushed(int buttonIndex);
    bool IsHeld(int buttonIndex);
    bool IsReleased(int buttonIndex);

    int? NowPushedButton();

    void Vibrate(float pan, float strength, float length);
}
/// <summary>アナログスティックの入力値。DeadZoneはこのスティック固有の不感帯設定。</summary>
public struct StickState
{
    public float X;
    public float Y;
    public int DeadZone;
}
public enum ControllerType
{
    Unknown,
    Xbox,
    PlayStation,
    NintendoSwitch,
    Generic
}

/// <summary>ゲームパッド入力へのstaticアクセス窓口。</summary>
public class Pad
{
    public static IController ControllerInstance => AstrumCore.Platform.Controller;
    public static void Update() => ControllerInstance.Update();
    public static int Count => ControllerInstance.Count;
    public static string[] List => ControllerInstance.List;
    public static IJoyPad? GetJoyPad(int index) => ControllerInstance.GetJoyPad(index);
}