using AstrumLoom;

namespace Sandbox;

internal class GameTemplateScene : Scene
{
    private int _index = 0;
    public override void Enable()
    {
    }
    public override void Disable() { }
    public override void Update()
    {
        if (Key.Up.Push())
        {
            _index = Math.Max(0, _index - 1);
        }
        if (Key.Down.Push())
        {
            _index = Math.Min(Pad.Count - 1, _index + 1);
        }
        var pad = Pad.GetJoyPad(_index);
        if (pad != null)
        {
            if (Key.Space.Push())
            {
                pad.Vibrate(0f, 1f, 1000);
            }
        }
    }
    public override void Draw()
    {
        Drawing.Fill(Color.DarkSlateGray);

        Drawing.Text(50, 40, "Game Template Scene");
        Drawing.Text(50, 80, $"Connected Controllers: {Pad.Count}");
        Drawing.Text(50, 100, string.Join("\n ", Pad.List));

        var pad = Pad.GetJoyPad(_index);
        if (pad != null)
        {
            Drawing.Text(50, 150, $"Using Controller Index: {_index}");
            Drawing.Text(50, 180, $"Name: {pad.Name}");
            Drawing.Text(50, 200, $"Product: {pad.Product}");
            Drawing.Text(50, 220, $"Type: {pad.Type}");
            Drawing.Text(50, 240, $"Buttons: {string.Join(", ", pad.Button)}");
            Drawing.Text(50, 260, $"Triggers: {string.Join(", ", pad.Trigger)}");
            Drawing.Text(50, 280, $"Sticks: {string.Join(", ",
                pad.Stick.Select(s => $"(X:{s.X:0.####}, Y:{s.Y:0.####})"))}");
        }
        else
        {
            Drawing.Text(50, 150, $"No Controller Connected at Index: {_index}");
        }
    }
}
