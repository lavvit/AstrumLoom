using AstrumLoom;

namespace Sandbox;

internal class SoundDemoScene : Scene
{
    private Sound? _bgm;
    private Sound? _sfx;
    public override void Enable()
    {
        _bgm = new Sound("Assets/バナナのナナチ.ogg", stream: true);
        _sfx = new Sound("Assets/Cancel.ogg");
        if (_bgm != null)
        {
            _bgm.Loop = true;
            _bgm.Volume = 0.6;
            _bgm.Play();
        }
    }
    public override void Disable()
    {
        base.Disable();
        _bgm?.Dispose();
        _sfx?.Dispose();
    }
    public override void Update()
    {
        if (Key.Esc.Push()) AstrumCore.End();
        if (Key.Space.Push()) _sfx?.Play();

        // BGM の簡易コントロール
        _bgm?.PlayStream(); // 一度だけ呼ぶ
        if (Key.Up.Repeat(4, 12)) _bgm!.Volume = Math.Min(4.0, _bgm!.Volume + 0.02);
        if (Key.Down.Repeat(4, 12)) _bgm!.Volume = Math.Max(0.0, _bgm!.Volume - 0.02);
        if (Key.Left.Repeat(6, 12)) _bgm!.Pan = Math.Max(-1.0, _bgm!.Pan - 0.05);
        if (Key.Right.Repeat(6, 12)) _bgm!.Pan = Math.Min(1.0, _bgm!.Pan + 0.05);
        if (Key.F1.Push()) _bgm!.Pitch = Math.Max(0.05, _bgm!.Pitch - 0.05);
        if (Key.F2.Push()) _bgm!.Pitch = Math.Min(8.0, _bgm!.Pitch + 0.05);
        if (Key.R.Push()) _bgm!.Time = 0.0;
        if (Key.E.Push())
        {
            _bgm!.Volume = 1;
            _bgm!.Pan = 0;
            _bgm!.Pitch = 1;
        }

    }

    public override void Draw()
    {
        Drawing.Fill(Color.DarkSlateGray);
        Drawing.Text(20, 20, "Sound Play Demo Scene");
        Drawing.Text(20, 50, "Space: Play SFX");
        Drawing.Text(20, 80, "Up/Down: Volume");
        Drawing.Text(20, 110, "Left/Right: Pan");
        Drawing.Text(20, 140, "F1/F2: Pitch");
        Drawing.Text(20, 170, "R: Restart BGM");
        Drawing.Text(20, 200, "E: Reset BGM Params");


        double x = 20;
        double y = AstrumCore.Height - 140;
        Drawing.Box(x - 12, y - 12, 420, 130, new Color(0, 0, 0, 120));

        if (_bgm != null)
        {
            Drawing.Box(x, y - 4, 400 * _bgm.Progress, 8, Color.LimeGreen);
            Drawing.Text(x, y, $"BGM Vol: {_bgm.Volume:0.00} Pan: {_bgm.Pan:0.00} Pitch: {_bgm.Pitch:0.00}", Color.White);
            Drawing.Text(x, y + 22, $"Time: {_bgm.Time / 1000.0:0.0}s / {_bgm.Length / 1000.0:0.0}s", Color.White);
        }
    }
}