using Raylib_cs;

using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

/// <summary>
/// IController の raylib 実装。接続中のゲームパッドを毎フレーム走査し、RayLibPad の集合として管理する。
/// </summary>
public class RayLibController : IController
{
    // Count/List/GetJoyPad は Buffer()/Update() と同じ _lock 経由で読む。
    // ここを素通しにすると、接続/切断で _joyPads が書き換わる瞬間に
    // 別スレッドから列挙して InvalidOperationException を踏みうる。
    public int Count { get { lock (_lock) return _joyPads.Count; } }
    public string[] List { get { lock (_lock) return [.. _joyPads.Select(p => $"{p.Index}:{p.Name}")]; } }
    public IJoyPad? GetJoyPad(int index) { lock (_lock) return _joyPads.FirstOrDefault(p => p.Index == index); }

    private List<IJoyPad> _joyPads = [];
    private readonly object _lock = new();

    /// <summary>接続中のゲームパッドをスキャンし、新規接続分の追加・切断分の削除を行います。</summary>
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
    /// <summary>接続状態の更新と、各パッドの生入力の取り込みをスレッドセーフに行います。</summary>
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
    /// <summary>Buffer() で取り込んだ生入力から、各パッドの押下/スティック状態を確定させます。</summary>
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
/// <summary>
/// IJoyPad の raylib 実装。1台分のゲームパッドを表し、Buffer() で生の軸/ボタン値を取り込み、
/// Update() でそれを押下エッジ付きの状態に変換する。
/// </summary>
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
    /// <summary>raylibから今フレームの生のボタン押下・軸移動量を取得して保持します（状態の確定はUpdate()で行う）。</summary>
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

    /// <summary>Buffer()で取り込んだ生入力から、各ボタンの押下エッジ状態とスティック/トリガー値を確定します。</summary>
    public void Update()
    {
        // Update button, trigger, and stick states
        // Button[i] の値: 1=押した瞬間, 2=押しっぱなし, -1=離した瞬間, 0=無入力
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

    /// <summary>指定ボタンが押された瞬間かどうか。</summary>
    public bool IsPushed(int buttonIndex) => Button[buttonIndex] == 1;
    /// <summary>指定ボタンが押されている（押した瞬間・押しっぱなし含む）かどうか。</summary>
    public bool IsHeld(int buttonIndex) => Button[buttonIndex] > 0;
    /// <summary>指定ボタンが離された瞬間かどうか。</summary>
    public bool IsReleased(int buttonIndex) => Button[buttonIndex] < 0;
    /// <summary>現在押されているボタンのうち最初に見つかったもののインデックスを返します。無ければ null。</summary>
    public int? NowPushedButton() => Button.ToList().FindIndex(b => b > 0) is int idx and >= 0 ? idx : null;

    /// <summary>左右モーターの強さをパン(左右バランス)から算出し、指定時間だけ振動させます。</summary>
    public void Vibrate(float pan, float strength, float length)
    {
        float leftMotor = strength * (pan <= 0 ? 1.0f : 1.0f - pan);
        float rightMotor = strength * (pan >= 0 ? 1.0f : 1.0f + pan);
        SetGamepadVibration(Index, leftMotor, rightMotor, length / 1000.0f);
    }

    // DxLib/DxLibPad.cs の GetJoypadInputState ビット順（DOWN,LEFT,RIGHT,UP,A,B,C,X,Y,Z,L,R,START,M,...）に
    // 合わせるためのテーブル。raylib の GamepadButton 列挙値をそのまま index として cast すると
    // 全く別の物理ボタンを指してしまうため、DxLib 側と同じ index が同じ物理ボタンを指すよう変換する。
    // A/B/X/Y は XInput パッドでの一般的な対応（A=下, B=右, X=左, Y=上）に、L/R はショルダー(1段目)に、
    // START/M はメニュー系ボタンに寄せてある。C/Z や左右スティック押し込みなど raylib 側に
    // 対応する列挙値が無いものは Unknown のままにする。
    private static readonly GamepadButton[] DxOrderMap =
    [
        GamepadButton.LeftFaceDown,   // 0: DOWN
        GamepadButton.LeftFaceLeft,   // 1: LEFT
        GamepadButton.LeftFaceRight,  // 2: RIGHT
        GamepadButton.LeftFaceUp,     // 3: UP
        GamepadButton.RightFaceDown,  // 4: A
        GamepadButton.RightFaceRight, // 5: B
        GamepadButton.Unknown,        // 6: C（対応ボタン無し）
        GamepadButton.RightFaceLeft,  // 7: X
        GamepadButton.RightFaceUp,    // 8: Y
        GamepadButton.Unknown,        // 9: Z（対応ボタン無し）
        GamepadButton.LeftTrigger1,   // 10: L
        GamepadButton.RightTrigger1,  // 11: R
        GamepadButton.Middle,         // 12: START
        GamepadButton.MiddleLeft,     // 13: M
    ];
    private static GamepadButton GetButton(int index) =>
        index >= 0 && index < DxOrderMap.Length ? DxOrderMap[index] : GamepadButton.Unknown;
    /// <summary>パッド名の文字列に含まれるキーワードから、対応するコントローラー種別を推定します。</summary>
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
