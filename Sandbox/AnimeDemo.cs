using AstrumLoom;
using AstrumLoom.Exo;

namespace Sandbox;

/// <summary>
/// AviUtl2 の .aup2 プロジェクトを読み込んで再生する確認用シーン。
/// Extend/Exo 分離後、Exo が exo と同じ new(path, isLoop) で aup2 も読めることを確認する。
/// </summary>
internal sealed class AnimeDemoScene : Scene
{
    private ExoAnimation? _exo;

    public override void Enable()
    {
        _exo = new("Assets/demo/Anime.aup2", isLoop: true);
        _exo.Start();
    }

    public override void Disable()
    {
        _exo?.Stop();
    }

    public override void Draw()
    {
        Drawing.Fill(Color.Black);

        if (_exo == null || !_exo.Enable)
        {
            Drawing.DefaultText(20, 20, "Anime.aup2 の読み込みに失敗しました", Color.Red);
            return;
        }

        _exo.Draw(AstrumCore.WindowWidth / 2f, AstrumCore.WindowHeight / 2f);

        // 現在フレーム / 総フレーム / 読み込んだオブジェクト数
        Drawing.DefaultText(20, 20,
            $"Frame: {_exo.GetNowFrame()} / {_exo.LengthFrames}", Color.White);
        Drawing.DefaultText(20, 40,
            $"Images: {_exo.ImageObjectCount}  Sounds: {_exo.SoundObjectCount}", Color.White);
    }
}
