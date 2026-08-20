using System.Text;

using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

/// <summary>DxLibバックエンドでの複数ゲームパッド管理。接続/切断を毎フレーム検知してIJoyPadを出し入れする。</summary>
internal class DxLibController : IController
{
    public int Count => _joyPads.Count;
    public string[] List => [.. _joyPads.Select(p => $"{p.Index}:{p.Name}")];
    // DxLibのパッド番号は1始まりだが、IController.GetJoyPad の index は0始まりで受け取る前提のため+1する。
    public IJoyPad? GetJoyPad(int index) => _joyPads.FirstOrDefault(p => p.Index == index + 1);

    private List<IJoyPad> _joyPads = [];
    private readonly object _lock = new();

    /// <summary>接続中のパッドを検出し、_joyPadsを実際の接続状況に同期させる。DateTime.Now.Millisecond&lt;100の判定で毎フレームではなく間欠的にReSetupJoypad（デバイス再列挙）を行う。</summary>
    public void SetController()
    {
        // 接続されているコントローラーを取得
        int maxPads = 10; // 最大コントローラー数
        if (DateTime.Now.Millisecond < 100)
            ReSetupJoypad();
        int connectedPads = GetJoypadNum();
        for (int i = 1; i <= maxPads; i++)
        {
            int j = GetJoypadInputState(i);
            if (i <= connectedPads || j > 0)
            {
                // コントローラーが接続されている場合、JoyPad オブジェクトを作成
                if (!_joyPads.Any(p => p.Index == i))
                    _joyPads.Add(new DxLibPad(i));
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
/// <summary>DxLibバックエンドでの1台分のゲームパッド実装。ButtonはPush/Hold/Left相当の状態遷移(1押下開始/2保持/-1離鍵/0非押下)を持つ。</summary>
internal class DxLibPad : IJoyPad
{
    public int Index { get; }
    public string Name { get; }
    public string Product { get; }
    public ControllerType Type { get; }
    public int[] Button { get; } = new int[24];
    public float[] Trigger { get; } = new float[2];
    public StickState[] Stick { get; } = new StickState[2];

    private bool[] _pressed = [];
    private float[] _axis = new float[6];

    public DxLibPad(int index)
    {
        Index = index;
        (Name, Product) = GetName(index);
        Type = GetControllerType();
    }

    /// <summary>DxLibのビットマスク/アナログ値を読み出し、_pressed/_axisへ生値として書き込む。実際の状態遷移計算はUpdateで行う。</summary>
    public void Buffer()
    {
        if (_pressed.Length != Button.Length)
            Array.Resize(ref _pressed, Button.Length);
        int input = GetJoypadInputState(Index);
        for (int i = 0; i < Button.Length; i++)
        {
            _pressed[i] = (input & (1 << i)) > 0;
        }
        // トリガーとスティックの状態を更新
        GetJoypadAnalogInput(out int lx, out int ly, Index);
        GetJoypadAnalogInputRight(out int rx, out int ry, Index);
        _axis[0] = lx / 1000.0f;
        _axis[1] = ly / 1000.0f;
        _axis[2] = rx / 1000.0f;
        _axis[3] = ry / 1000.0f;
        GetJoypadXInputState(Index, out var xinput);
        _axis[4] = xinput.LeftTrigger / 255.0f;
        _axis[5] = xinput.RightTrigger / 255.0f;
        // トリガーもボタンの1つとして扱えるよう、一定量踏み込んだらButtonビットにも反映する
        _pressed[14] |= _axis[4] > 0.1f;
        _pressed[15] |= _axis[5] > 0.1f;
    }

    /// <summary>Bufferで取得した生の押下状態からButton配列を1(押下開始)/2(保持)/-1(離鍵)/0(非押下)の状態遷移に変換する。</summary>
    public void Update()
    {
        for (int i = 0; i < Button.Length; i++)
        {
            bool pressed = _pressed[i];
            Button[i] = pressed ? (Button[i] < 1 ? 1 : 2) : (Button[i] > 0 ? -1 : 0);
        }
        Trigger[0] = _axis[4];
        Trigger[1] = _axis[5];

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

    /// <summary>左右モーターの強さを計算するが、DxLibのStartJoypadVibrationは左右独立制御を持たないため、結局strengthのみを使い左右差(pan)は反映されていない。</summary>
    public void Vibrate(float pan, float strength, float length)
    {
        float leftMotor = strength * (pan <= 0 ? 1.0f : 1.0f - pan);
        float rightMotor = strength * (pan >= 0 ? 1.0f : 1.0f + pan);
        float str = strength * 1000;
        StartJoypadVibration(Index, (int)str, (int)length);
    }

    private static (string, string) GetName(int index)
    {
        var str = new StringBuilder(65535);
        var prd = new StringBuilder(65535);
        GetJoypadName(index, str, prd);
        return (str.ToString(), prd.ToString());
    }

    /// <summary>デバイス名の文字列から簡易的にメーカー種別を推測する（DxLib自体はコントローラ種別を直接教えてくれないため）。</summary>
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