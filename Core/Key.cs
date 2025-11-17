namespace AstrumLoom;

public interface IInput
{
    bool GetKey(Key key);
    bool GetKeyDown(Key key);
    bool GetKeyUp(Key key);
}
public enum Key
{
    Escape,
    Space,
    Left,
    Right,
    Up,
    Down,
    // 必要になったら増やす
}