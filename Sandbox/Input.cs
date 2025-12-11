using System;
using System.Collections.Generic;
using System.Linq;
using AstrumLoom;

namespace Sandbox;

internal sealed class InputTestScene : Scene
{
    private static readonly MouseButton[] MouseButtons = Enum.GetValues<MouseButton>();

    private readonly Queue<string> _eventLog = new();
    private readonly List<Key> _pressedKeys = new();
    private readonly TextInputOptions _textOptions = new() { MaxLength = 64 };

    private string _textBuffer = string.Empty;
    private bool _textActive;
    private int _selectedPad;
    private double _mouseSpeedPeak;

    public override void Enable()
    {
        _eventLog.Clear();
        _pressedKeys.Clear();
        _textBuffer = string.Empty;
        _textActive = false;
        _selectedPad = 0;
        _mouseSpeedPeak = 0;
    }

    public override void Update()
    {
        CaptureKeyboardState();
        UpdateMouseMetrics();
        UpdateControllerState();
        UpdateTextInput();
    }

    private void CaptureKeyboardState()
    {
        _pressedKeys.Clear();
        foreach (var key in KeyInput.GetPressedKeys())
        {
            _pressedKeys.Add(key);
        }

        foreach (var key in KeyInput.GetAllKeys())
        {
            if (key.Push())
            {
                AddLog($"KeyDown {key}");
            }
            else if (key.Left())
            {
                AddLog($"KeyUp {key}");
            }
        }
    }

    private void UpdateMouseMetrics()
    {
        double speed = Mouse.Speed;
        _mouseSpeedPeak = Math.Max(speed, _mouseSpeedPeak * 0.95);
        if (Mouse.Wheel != 0)
        {
            AddLog($"Wheel {(Mouse.Wheel > 0 ? "Up" : "Down")} ({Mouse.WheelTotal:0.##})");
        }
    }

    private void UpdateControllerState()
    {
        int count = Pad.Count;
        if (count <= 0)
        {
            _selectedPad = 0;
            return;
        }

        if (Key.Left.Push())
        {
            _selectedPad = (_selectedPad - 1 + count) % count;
            AddLog($"Pad {_selectedPad} selected");
        }
        else if (Key.Right.Push())
        {
            _selectedPad = (_selectedPad + 1) % count;
            AddLog($"Pad {_selectedPad} selected");
        }

        if (_selectedPad >= count)
        {
            _selectedPad = count - 1;
        }

        var pad = Pad.GetJoyPad(_selectedPad);
        if (pad == null)
            return;

        if (Key.Space.Push())
        {
            pad.Vibrate(0f, 0.35f, 400);
            AddLog($"Pad {_selectedPad} vibrate");
        }

        if (pad.NowPushedButton() is int button)
        {
            AddLog($"Pad {_selectedPad} Button {button}")
;
        }
    }

    private void UpdateTextInput()
    {
        if (!_textActive && Key.T.Push())
        {
            KeyInput.ActivateText(ref _textBuffer, _textOptions);
            _textActive = true;
            AddLog("Text input started");
        }

        if (_textActive && KeyInput.Enter(ref _textBuffer, out var committed))
        {
            if (!string.IsNullOrEmpty(committed))
            {
                AddLog($"Entered \"{committed}\"");
            }
            _textActive = false;
        }
    }

    public override void Draw()
    {
        Drawing.Fill(Color.DarkSlateGray);

        DrawKeyboardPanel();
        DrawMousePanel();
        DrawControllerPanel();
        DrawLogPanel();

        Drawing.Text(50, 640, "[T] text input  [←/→] select controller  [Space] vibrate", Color.White);
    }

    private void DrawKeyboardPanel()
    {
        Drawing.Box(40, 40, 700, 140, Color.Black);
        Drawing.Text(50, 50, "Keyboard", Color.White);
        string pressed = _pressedKeys.Count == 0
            ? "None"
            : string.Join(", ", _pressedKeys.Take(12));
        Drawing.Text(50, 80, $"Pressed: {pressed}", Color.White);
        Drawing.Text(50, 100, $"Shift: {FormatBool(KeyInput.Shift)}  Ctrl: {FormatBool(KeyInput.Ctrl)}  Alt: {FormatBool(KeyInput.Alt)}", Color.White);
        Drawing.Text(50, 120, $"Typing: {FormatBool(KeyInput.Typing)} Buffer: {_textBuffer}", Color.White);
    }

    private void DrawMousePanel()
    {
        Drawing.Box(40, 200, 700, 150, Color.Black);
        Drawing.Text(50, 210, "Mouse", Color.White);
        Drawing.Text(50, 230, $"Position: ({Mouse.X:0.##}, {Mouse.Y:0.##}) Speed: {Mouse.Speed:0.##} Peak: {_mouseSpeedPeak:0.##}", Color.White);
        Drawing.Text(50, 250, $"Wheel: {Mouse.Wheel:+0.##;-0.##;0} (Total {Mouse.WheelTotal:0.##}) TouchPad: {FormatBool(Mouse.IsTouchPad)}", Color.White);
        var states = MouseButtons.Select(button => $"{button}:{DescribeMouseButton(button)}");
        Drawing.Text(50, 270, $"Buttons: {string.Join("  ", states)}", Color.White);
    }

    private void DrawControllerPanel()
    {
        Drawing.Box(40, 360, 1200, 220, Color.Black);
        Drawing.Text(50, 370, $"Controllers: {Pad.Count}", Color.White);

        var names = Pad.List ?? Array.Empty<string>();
        for (int i = 0; i < names.Length; i++)
        {
            string marker = i == _selectedPad ? "> " : "  ";
            Drawing.Text(60, 390 + i * 18, $"{marker}[{i}] {names[i]}", Color.White);
        }

        if (Pad.Count == 0)
        {
            Drawing.Text(500, 390, "No controller detected", Color.White);
            return;
        }

        var pad = Pad.GetJoyPad(_selectedPad);
        if (pad == null)
        {
            Drawing.Text(500, 390, "No controller selected", Color.White);
            return;
        }

        Drawing.Text(500, 390, $"Pad {_selectedPad}: {pad.Name}", Color.White);
        Drawing.Text(500, 410, $"Product: {pad.Product}  Type: {pad.Type}", Color.White);

        var buttonStates = pad.Button
            .Select((value, index) => value != 0 ? index.ToString() : null)
            .Where(static value => value != null)
            .ToArray();
        var triggerStates = pad.Trigger
            .Select((value, index) => $"T{index}:{value:0.00}")
            .ToArray();
        var stickStates = pad.Stick
            .Select((state, index) => $"S{index}:(X:{state.X:0.00},Y:{state.Y:0.00})")
            .ToArray();
        var deadZones = pad.Stick
            .Select((state, index) => $"S{index}:{state.DeadZone}")
            .ToArray();

        Drawing.Text(500, 430, $"Buttons: {(buttonStates.Length == 0 ? "None" : string.Join(", ", buttonStates))}", Color.White);
        Drawing.Text(500, 450, $"Triggers: {string.Join(", ", triggerStates)}", Color.White);
        Drawing.Text(500, 470, $"Sticks: {string.Join("  ", stickStates)}", Color.White);
        Drawing.Text(500, 490, $"DeadZone: {string.Join(", ", deadZones)}", Color.White);
    }

    private void DrawLogPanel()
    {
        Drawing.Box(800, 40, 440, 310, Color.Black);
        Drawing.Text(810, 50, "Event Log", Color.White);
        int y = 70;
        foreach (var entry in _eventLog.Reverse())
        {
            Drawing.Text(810, y, entry, Color.White);
            y += 18;
        }
    }

    private static string DescribeMouseButton(MouseButton button)
    {
        if (Mouse.Push(button)) return "Push";
        if (Mouse.Left(button)) return "Release";
        if (Mouse.Hold(button)) return "Hold";
        return "Idle";
    }

    private void AddLog(string message)
    {
        const int maxEntries = 14;
        _eventLog.Enqueue(message);
        while (_eventLog.Count > maxEntries)
        {
            _eventLog.Dequeue();
        }
    }

    private static string FormatBool(bool value) => value ? "ON" : "OFF";
}
