namespace AstrumLoom;

public interface IMouse
{
    /// <summary>マウスのX座標を取得します。</summary>
    double X { get; set; }
    /// <summary>マウスのY座標を取得します。</summary>
    double Y { get; set; }

    /// <summary>マウスホイールの回転量を取得します。</summary>
    double Wheel { get; }
    /// <summary>マウスホイールの総回転量を取得します。</summary>
    double WheelTotal { get; }

    void Init(bool visible);
    void Update();

    /// <summary>指定したボタンが押されたかどうかを取得します。</summary>
    bool Push(MouseButton button);
    /// <summary>指定したボタンを押しているかどうかを取得します。</summary>
    bool Hold(MouseButton button);
    /// <summary>指定したボタンが離されたかどうかを取得します。</summary>
    bool Left(MouseButton button);
}
public enum MouseState { None, Pressed, Held, Released }
public enum MouseButton { Left, Right, Middle }