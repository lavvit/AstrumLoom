using Raylib_cs;

using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

public class RayLibController : IController
{
    public int Count => _joyPads.Count;
    public string[] List => [.. _joyPads.Select(p => $"{p.Index}:{p.Name}")];
    public IJoyPad? GetJoyPad(int index) => _joyPads.FirstOrDefault(p => p.Index == index);

    private List<IJoyPad> _joyPads = [];
    private readonly object _lock = new();

    public void SetController()
    {
        // 接続されているコントローラーを取得
        int maxPads = 32; // 最大コントローラー数
        for (int i = 0; i < maxPads; i++)
        {
            if (IsGamepadAvailable(i))
            {
                // コントローラーが接続されている場合、JoyPad オブジェクトを作成
                if (!_joyPads.Any(p => p.Index == i))
                    _joyPads.Add(new RayLibPad(i));
            }
            else
            {
                // コントローラーが切断されている場合、JoyPad オブジェクトを削除
                _joyPads.RemoveAll(p => p.Index == i);
            }
        }
    }
    public void Buffer()
    {
        lock (_lock)
        {
            SetController();
            // 各コントローラーの状態を更新
            foreach (var pad in _joyPads)
            {
                pad.Buffer();
            }
        }
    }
    public void Update()
    {
        lock (_lock)
        {
            foreach (var pad in _joyPads)
            {
                pad?.Update();
            }
        }
    }
}
public class RayLibPad : IJoyPad
{
    public int Index { get; }
    public string Name { get; }
    public string Product { get; } = "RayLib Gamepad";
    public ControllerType Type { get; } = ControllerType.Generic;
    public int[] Button { get; } = new int[24];
    public float[] Trigger { get; } = new float[2];
    public StickState[] Stick { get; } = new StickState[2];

    private bool[] _pressed = [];
    private float[] _axis = new float[6];
    public RayLibPad(int index)
    {
        Index = index;
        Name = GetGamepadName_(index);
        Product = "RayLib Gamepad";
        Type = GetControllerType();
    }
    public void Buffer()
    {
        // Buffer button, trigger, and stick states
        if (_pressed.Length != Button.Length)
            Array.Resize(ref _pressed, Button.Length);
        for (int i = 0; i < Button.Length; i++)
        {
            _pressed[i] = IsGamepadButtonDown(Index, GetButton(i));
        }
        for (int i = 0; i < _axis.Length; i++)
        {
            _axis[i] = GetGamepadAxisMovement(Index, (GamepadAxis)i);
        }
    }

    public void Update()
    {
        // Update button, trigger, and stick states
        for (int i = 0; i < Button.Length; i++)
        {
            bool pressed = _pressed[i];
            Button[i] = pressed ? (Button[i] < 1 ? 1 : 2) : (Button[i] > 0 ? -1 : 0);
        }
        // Update triggers
        Trigger[0] = (float)Easing.Ease(_axis[4] + 1, 2, 0, 1, EEasing.Sine, EInOut.Out); // Left Trigger
        Trigger[1] = (float)Easing.Ease(_axis[5] + 1, 2, 0, 1, EEasing.Sine, EInOut.Out); // Right Trigger

        // Update sticks
        Stick[0] = new StickState
        {
            X = _axis[0],
            Y = _axis[1],
            DeadZone = 0
        };
        Stick[1] = new StickState
        {
            X = _axis[2],
            Y = _axis[3],
            DeadZone = 0
        };
    }

    public bool IsPushed(int buttonIndex) => Button[buttonIndex] == 1;
    public bool IsHeld(int buttonIndex) => Button[buttonIndex] > 0;
    public bool IsReleased(int buttonIndex) => Button[buttonIndex] < 0;
    public int? NowPushedButton() => Button.ToList().FindIndex(b => b > 0) is int idx and >= 0 ? idx : null;

    public void Vibrate(float pan, float strength, float length)
    {
        float leftMotor = strength * (pan <= 0 ? 1.0f : 1.0f - pan);
        float rightMotor = strength * (pan >= 0 ? 1.0f : 1.0f + pan);
        SetGamepadVibration(Index, leftMotor, rightMotor, length / 1000.0f);
    }

    private static GamepadButton GetButton(int index) => (GamepadButton)index;
    private ControllerType GetControllerType()
    {
        string name = Name;
        return name.Contains("Xbox", StringComparison.OrdinalIgnoreCase)
            ? ControllerType.Xbox
            : name.Contains("DualShock", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PS4", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PS5", StringComparison.OrdinalIgnoreCase)
            ? ControllerType.PlayStation
            : name.Contains("Switch", StringComparison.OrdinalIgnoreCase) ? ControllerType.NintendoSwitch : ControllerType.Generic;
    }
}
