using System.Numerics;

using AstrumLoom;
using AstrumLoom.Audio;
using AstrumLoom.Audio.Bgm;

namespace Sandbox;

/// <summary>
/// テーマ「音の工房」。AstrumLoom.Audio（合成音モジュール）を、素材ファイル 0 個のゲームで確かめるシーン。
///
/// GameTemplateScene が「描画と当たり判定の雛形」なのに対して、こちらは
/// 「その雛形に音を全部載せたらどうなるか」を見るためのもの。鳴っている音はすべて
/// 実行時に合成して .audiocache へ焼いたもので、Assets\*.wav は一切使っていない。
///
/// 確かめたいこと:
///   Audio.Play(id, volume, pitch, pan) … 効果音。pan は画面上の x、pitch は速さに結びつけている
///   Audio.PlayBgm(BgmScore)           … 手で組んだ譜面から BGM を合成。レベルが上がると組み直して速くなる
///   MasterVolume / SfxVolume / BgmVolume / Muted … 3 段のボリュームとミュートが本当に効くか
///   Audio.Update()                    … Draw の先頭で毎フレーム呼ぶ約束。これを外すと一切鳴らなくなる
///   AudioSelfCheck.Verify()           … 合成結果の検算。バックエンドに依らないので裏で 1 回だけ回す
///
/// 音が鳴っているかは耳でしか分からないので、右側に「今どの SfxId を要求したか」の履歴と
/// ボリュームの生値を出している。「鳴っているつもりで鳴っていない」をここで見抜く。
/// </summary>
internal sealed class AudioGameScene : Scene
{
    private const float PlayerRadius = 20f;
    private const float PlayerSpeed = 420f;
    private const float BulletRadius = 5f;
    private const float BulletSpeed = 720f;
    private const float BlockRadius = 20f;
    private const double PanelX = 900;
    private const double FloorY = 620;

    private readonly Random _random = new();

    // 更新スレッド専用の作業用リスト。描画スレッドは *View の配列しか読まない
    // （GameTemplateScene と同じ約束。素の List を共有すると列挙中の RemoveAt で落ちる）。
    private readonly List<Block> _blocks = [];
    private readonly List<Bullet> _bullets = [];
    private readonly List<Shard> _shards = [];
    private Block[] _blocksView = [];
    private Bullet[] _bulletsView = [];
    private Shard[] _shardsView = [];

    // 鳴らした効果音の履歴。新しいものが先頭。表示用なので Draw から読めるよう配列で公開する。
    private readonly List<string> _log = [];
    private string[] _logView = [];

    private Vector2 _playerPos;
    private double _spawnTimer;
    private double _shootCooldown;
    private double _chargeHeld;      // Space を押しっぱなしにしている秒数
    private double _warnTimer;       // HP 1 のときの警報の間隔
    private double _invincible;
    private int _score;
    private int _level = 1;
    private int _broken;             // 撃破数。10 でレベルアップ
    private int _hp;
    private bool _gameOver;
    private bool _bgmOn = true;

    // --- セルフテストから覗くための値 ---------------------------------------

    /// <summary>Audio.Play を要求した回数。「1 度も鳴らそうとしていない」を見抜くために数える。</summary>
    public int SfxRequests { get; private set; }

    /// <summary>直近に要求した効果音。</summary>
    public SfxId? LastSfx { get; private set; }

    /// <summary>Audio.PlayBgm を要求済みか。</summary>
    public bool BgmStarted { get; private set; }

    /// <summary>AudioSelfCheck.Verify の結果。まだ終わっていなければ null。</summary>
    public bool? SelfCheckOk { get; private set; }

    /// <summary>AudioSelfCheck.Verify の説明文。</summary>
    public string SelfCheckDetail { get; private set; } = "検算中…";

    public int Score => _score;
    public int Hp => _hp;
    public int Level => _level;
    public bool IsGameOver => _gameOver;

    public override void Enable()
    {
        base.Enable();

        // 初回の Play は合成 → WAV 書き出し → ロードを踏むので、その場では間に合わないことがある
        // （AudioEngine は未ロードの要求を 2 秒だけ持ち越して再挑戦する）。先に全部焼いておく。
        Audio.PrewarmAll();
        Audio.Muted = false;
        Audio.MasterVolume = 1.0;
        Audio.SfxVolume = 0.9;
        Audio.BgmVolume = 0.5;

        // 合成結果の検算は全プリセットを PCM まで作るので重い。描画を止めないよう裏で回す。
        // Sound にもバックエンドにも触らない（PCM を調べるだけ）ので、別スレッドから呼んでよい。
        _ = Task.Run(() =>
        {
            try
            {
                bool ok = AudioSelfCheck.Verify(out string detail);
                SelfCheckDetail = detail;
                SelfCheckOk = ok;
                if (!ok) Log.Warning("AudioSelfCheck: " + detail);
            }
            catch (Exception e)
            {
                SelfCheckDetail = "検算が例外で止まりました: " + e.Message;
                SelfCheckOk = false;
            }
        });

        ResetGame();
    }

    public override void Disable()
    {
        base.Disable();
        // シーンを抜けたら必ず止める。止め忘れると次のシーンでも BGM が鳴り続ける。
        Audio.StopBgm();
        _blocks.Clear();
        _bullets.Clear();
        _shards.Clear();
        _blocksView = [];
        _bulletsView = [];
        _shardsView = [];
    }

    public override void Update()
    {
        double delta = DemoUi.Delta;

        HandleAudioKeys();

        if (_gameOver)
        {
            if (Key.R.Push())
            {
                Play(SfxId.Decide);
                ResetGame();
            }
            PublishViews();
            return;
        }

        HandleMovement(delta);
        HandleShooting(delta);
        UpdateBullets(delta);
        UpdateBlocks(delta);
        UpdateShards(delta);
        ResolveHits();
        ResolveCatch();

        _spawnTimer -= delta;
        if (_spawnTimer <= 0) SpawnBlock();

        _shootCooldown = Math.Max(0, _shootCooldown - delta);
        _invincible = Math.Max(0, _invincible - delta);

        // HP が残り 1 になったら警報を鳴らし続ける。持続音のプリセットが本当に持続しているかの確認も兼ねる。
        if (_hp == 1)
        {
            _warnTimer -= delta;
            if (_warnTimer <= 0)
            {
                Play(SfxId.Warning, volume: 0.45);
                _warnTimer = 1.6;
            }
        }

        PublishViews();
    }

    public override void Draw()
    {
        // 約束: 毎フレーム、Draw の先頭で 1 回。Audio.Play が積んだジョブはここでしか掃けない
        // （raylib の音 API はメインスレッド専用で、Draw だけが必ずメインスレッドで走る）。
        Audio.Update();

        Drawing.Fill(new Color(12, 14, 26));
        DrawArena();
        DrawShards();
        DrawBlocks();
        DrawBullets();
        DrawPlayer();
        DrawHud();
        DrawPanel();
    }

    // --- 音まわりの操作 -----------------------------------------------------

    /// <summary>効果音を要求しつつ履歴に残す。表示のためにここを必ず通す。</summary>
    private void Play(SfxId id, double volume = 1, double pitch = 1, double pan = 0)
    {
        Audio.Play(id, volume, pitch, pan);
        SfxRequests++;
        LastSfx = id;
        _log.Insert(0, $"{id}  vol {volume:0.00}  pit {pitch:0.00}  pan {pan: 0.00;-0.00}");
        if (_log.Count > 12) _log.RemoveRange(12, _log.Count - 12);
    }

    /// <summary>画面上の x を pan（-1..+1）へ。音が見た目と同じ側から出るかを確かめるための対応づけ。</summary>
    private static double PanOf(double x) => Math.Clamp(x / PanelX * 2 - 1, -1, 1);

    private void HandleAudioKeys()
    {
        if (Key.M.Push())
        {
            Audio.Muted = !Audio.Muted;
            // 切り替えた直後に鳴らす。Muted が効いていれば聞こえない＝そこが確認になる。
            Play(Audio.Muted ? SfxId.Cancel : SfxId.Decide);
        }
        if (Key.Q.Push()) { Audio.MasterVolume = Math.Max(0, Audio.MasterVolume - 0.1); Play(SfxId.Cursor); }
        if (Key.W.Push()) { Audio.MasterVolume = Math.Min(1, Audio.MasterVolume + 0.1); Play(SfxId.Cursor); }
        if (Key.Z.Push()) { Audio.SfxVolume = Math.Max(0, Audio.SfxVolume - 0.1); Play(SfxId.Cursor); }
        if (Key.X.Push()) { Audio.SfxVolume = Math.Min(1, Audio.SfxVolume + 0.1); Play(SfxId.Cursor); }
        if (Key.C.Push()) Audio.BgmVolume = Math.Max(0, Audio.BgmVolume - 0.1);
        if (Key.V.Push()) Audio.BgmVolume = Math.Min(1, Audio.BgmVolume + 0.1);

        if (Key.B.Push())
        {
            _bgmOn = !_bgmOn;
            if (_bgmOn) StartBgm();
            else Audio.StopBgm();
        }
    }

    private void StartBgm()
    {
        // レベルが上がるたびに譜面を組み直して速くする。BgmScore が変われば AudioCache のハッシュも
        // 変わるので、その場で新しい WAV が焼かれて差し替わる（2 回目以降はキャッシュに当たる）。
        Audio.PlayBgm(BuildScore(_level), Audio.BgmVolume);
        BgmStarted = true;
    }

    /// <summary>
    /// 16 拍（4 小節）でちょうどループする譜面を手で組む。
    /// 声部 2 つ＋ドラム 3 つで、InstrumentKind の声部側とドラム側の両方を通す。
    /// </summary>
    private static BgmScore BuildScore(int level)
    {
        // 速さと調をレベルで動かす。上げすぎると耳に痛いので頭打ちにする。
        double bpm = Math.Min(168, 100 + level * 8);
        int shift = Math.Min(5, level - 1); // 半音単位の移調

        (string? Note, double LengthBeats)[] bassSteps =
        [
            ("A2", 1), ("A2", 0.5), (null, 0.5), ("A2", 1), ("E2", 1),
            ("F2", 1), ("F2", 0.5), (null, 0.5), ("F2", 1), ("C3", 1),
            ("G2", 1), ("G2", 0.5), (null, 0.5), ("G2", 1), ("D3", 1),
            ("E2", 1), ("E2", 0.5), (null, 0.5), ("E2", 1), ("B2", 1),
        ];

        (string? Note, double LengthBeats)[] leadSteps =
        [
            ("A4", 0.5), ("C5", 0.5), ("E5", 1), ("D5", 0.5), ("C5", 0.5), ("A4", 1),
            ("F4", 0.5), ("A4", 0.5), ("C5", 1), ("A4", 0.5), ("G4", 0.5), ("F4", 1),
            ("G4", 0.5), ("B4", 0.5), ("D5", 1), ("B4", 0.5), ("A4", 0.5), ("G4", 1),
            ("E4", 0.5), ("G4", 0.5), ("B4", 1), (null, 0.5), ("E5", 0.5), ("E4", 1),
        ];

        return new BgmScore
        {
            Bpm = bpm,
            Beats = 16,
            Swing = 0.08,
            Tracks =
            [
                new BgmTrack
                {
                    Instrument = InstrumentKind.SawBass,
                    Volume = 0.75,
                    Pan = -0.15,
                    Notes = Transpose(BgmBuild.Melody(bassSteps, velocity: 0.9), shift),
                },
                new BgmTrack
                {
                    Instrument = InstrumentKind.SquareLead,
                    Volume = 0.5,
                    Pan = 0.2,
                    // エコーはリードにだけ掛ける。掛かっているかどうかは耳ですぐ分かる。
                    EchoDelayBeats = 0.75,
                    EchoGain = 0.28,
                    Notes = Transpose(BgmBuild.Melody(leadSteps, velocity: 0.85), shift),
                },
                new BgmTrack
                {
                    Instrument = InstrumentKind.Kick,
                    Volume = 0.9,
                    Notes = Repeat("X...x...X..x....", 0.25, 4),
                },
                new BgmTrack
                {
                    Instrument = InstrumentKind.Snare,
                    Volume = 0.6,
                    Notes = Repeat("....x.......x...", 0.25, 4),
                },
                new BgmTrack
                {
                    Instrument = InstrumentKind.HihatClosed,
                    Volume = 0.35,
                    Pan = 0.3,
                    Notes = Repeat("x.x.x.x.x.x.x.x.", 0.25, 4),
                },
            ],
        };
    }

    /// <summary>1 小節ぶんのドラムパターンを bars 回並べる。</summary>
    private static List<BgmNote> Repeat(string pattern, double stepBeats, int bars)
    {
        var notes = new List<BgmNote>();
        double bar = pattern.Length * stepBeats;
        for (int i = 0; i < bars; i++)
        {
            notes.AddRange(BgmBuild.DrumPattern(pattern, stepBeats, startBeat: bar * i,
                velocity: 0.8, accentVelocity: 1.0));
        }
        return notes;
    }

    /// <summary>声部を半音単位で移調する。BgmNote は record なので with で作り直す。</summary>
    private static List<BgmNote> Transpose(List<BgmNote> notes, int semitones)
    {
        if (semitones == 0) return notes;
        for (int i = 0; i < notes.Count; i++)
            notes[i] = notes[i] with { MidiNote = notes[i].MidiNote + semitones };
        return notes;
    }

    // --- ゲームの中身 -------------------------------------------------------

    private void ResetGame()
    {
        _playerPos = new Vector2((float)(PanelX / 2), (float)FloorY - 40);
        _spawnTimer = 1.0;
        _shootCooldown = 0;
        _chargeHeld = 0;
        _warnTimer = 0;
        _invincible = 0;
        _score = 0;
        _level = 1;
        _broken = 0;
        _hp = 3;
        _gameOver = false;
        _blocks.Clear();
        _bullets.Clear();
        _shards.Clear();
        PublishViews();
        if (_bgmOn) StartBgm();
    }

    private void HandleMovement(double delta)
    {
        double dir = 0;
        if (Key.Left.Hold() || Key.A.Hold()) dir -= 1;
        if (Key.Right.Hold() || Key.D.Hold()) dir += 1;
        _playerPos.X += (float)(dir * PlayerSpeed * delta);
        _playerPos.X = Math.Clamp(_playerPos.X, (float)(40 + PlayerRadius), (float)(PanelX - 40 - PlayerRadius));
    }

    private void HandleShooting(double delta)
    {
        // 押しっぱなしで溜める。溜め始めに Charge を 1 回、離したときに威力に応じた音を鳴らす。
        if (Key.Space.Hold())
        {
            if (_chargeHeld == 0) Play(SfxId.Charge, volume: 0.7, pan: PanOf(_playerPos.X));
            _chargeHeld += delta;
            return;
        }

        if (_chargeHeld > 0)
        {
            Fire(charged: _chargeHeld >= 0.45);
            _chargeHeld = 0;
            return;
        }

        if (_shootCooldown <= 0 && Mouse.Push(MouseButton.Left)) Fire(charged: false);
    }

    private void Fire(bool charged)
    {
        double pan = PanOf(_playerPos.X);
        _bullets.Add(new Bullet
        {
            Position = _playerPos with { Y = _playerPos.Y - PlayerRadius },
            Speed = (float)(BulletSpeed * (charged ? 1.5 : 1.0)),
            Charged = charged,
            Life = 2f,
        });

        if (charged)
        {
            Play(SfxId.Laser, volume: 0.9, pan: pan);
        }
        else
        {
            // pitch を少しばらけさせる。同じ音を連打しても機械的に聞こえないのはこの 1 行の効果。
            Play(SfxId.Shot, volume: 0.7, pitch: 0.92 + _random.NextDouble() * 0.28, pan: pan);
        }
        _shootCooldown = charged ? 0.35 : 0.14;
    }

    private void UpdateBullets(double delta)
    {
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            var b = _bullets[i];
            b.Position.Y -= (float)(b.Speed * delta);
            b.Life -= (float)delta;
            if (b.Life <= 0 || b.Position.Y < 40) _bullets.RemoveAt(i);
            else _bullets[i] = b;
        }
    }

    private void UpdateBlocks(double delta)
    {
        for (int i = _blocks.Count - 1; i >= 0; i--)
        {
            var blk = _blocks[i];
            blk.Position.Y += (float)(blk.Speed * delta);
            blk.Age += (float)delta;

            if (blk.Position.Y >= FloorY)
            {
                // 取り逃がし。落ちた側から Thud が鳴るので、pan が効いていれば耳で場所が分かる。
                _blocks.RemoveAt(i);
                Play(SfxId.Thud, volume: 0.9, pan: PanOf(blk.Position.X));
                Damage();
                continue;
            }
            _blocks[i] = blk;
        }
    }

    private void UpdateShards(double delta)
    {
        for (int i = _shards.Count - 1; i >= 0; i--)
        {
            var s = _shards[i];
            s.Position.Y += (float)(120 * delta);
            s.Age += (float)delta;
            if (s.Position.Y > FloorY) _shards.RemoveAt(i);
            else _shards[i] = s;
        }
    }

    private void ResolveHits()
    {
        for (int b = _bullets.Count - 1; b >= 0; b--)
        {
            var bullet = _bullets[b];
            bool consumed = false;
            for (int k = _blocks.Count - 1; k >= 0; k--)
            {
                var blk = _blocks[k];
                float range = BlockRadius + BulletRadius;
                if (Vector2.DistanceSquared(blk.Position, bullet.Position) > range * range) continue;

                double pan = PanOf(blk.Position.X);
                blk.Hp -= bullet.Charged ? 2 : 1;
                if (blk.Hp > 0)
                {
                    // まだ割れていない＝手応えだけ返す。落下が速いブロックほど高い音にする。
                    _blocks[k] = blk;
                    Play(SfxId.Hit, volume: 0.7, pitch: 0.9 + blk.Speed / 900.0, pan: pan);
                }
                else
                {
                    _blocks.RemoveAt(k);
                    _score += blk.Tough ? 40 : 15;
                    _broken++;
                    Play(blk.Tough ? SfxId.Break : SfxId.Explode, volume: 0.85, pan: pan);
                    if (_random.Next(3) == 0) _shards.Add(new Shard { Position = blk.Position, Value = 25 });
                    if (_broken % 10 == 0) LevelUp();
                }

                // 溜め撃ちは貫通させる。1 発で複数割れると音が重なるので、ボイスプールの確認にもなる。
                if (!bullet.Charged) { consumed = true; break; }
            }
            if (consumed) _bullets.RemoveAt(b);
        }
    }

    private void ResolveCatch()
    {
        for (int i = _shards.Count - 1; i >= 0; i--)
        {
            var s = _shards[i];
            float range = PlayerRadius + 12f;
            if (Vector2.DistanceSquared(s.Position, _playerPos) > range * range) continue;

            _shards.RemoveAt(i);
            _score += s.Value;
            // 拾うほど音程を上げる定番の演出。pitch がちゃんと効いているかがすぐ分かる。
            Play(SfxId.Coin, volume: 0.8, pitch: 1.0 + Math.Min(6, _score / 200) * 0.06, pan: PanOf(s.Position.X));
            if (_hp < 4)
            {
                _hp++;
                Play(SfxId.Heal, volume: 0.6);
            }
        }
    }

    private void LevelUp()
    {
        _level++;
        Play(SfxId.PowerUp, volume: 0.9);
        // BGM を組み直す。ここで曲が速いものへ差し替わる。
        if (_bgmOn) StartBgm();
    }

    private void Damage()
    {
        if (_invincible > 0) return;
        _hp--;
        _invincible = 1.0;
        if (_hp > 0)
        {
            Play(SfxId.Damage, volume: 0.9);
            return;
        }
        _gameOver = true;
        Play(SfxId.ExplodeBig, volume: 1.0);
        Audio.StopBgm();
    }

    private void SpawnBlock()
    {
        bool tough = _random.Next(4) == 0;
        var position = new Vector2((float)(60 + _random.NextDouble() * (PanelX - 120)), 60f);
        _blocks.Add(new Block
        {
            Position = position,
            Speed = (float)(70 + _random.NextDouble() * 50 + _level * 12),
            Hp = tough ? 3 : 1,
            Tough = tough,
        });
        _spawnTimer = Math.Max(0.35, 1.25 - _level * 0.07);
        Play(SfxId.Whoosh, volume: 0.3, pitch: 1.1, pan: PanOf(position.X));
    }

    private void PublishViews()
    {
        _blocksView = [.. _blocks];
        _bulletsView = [.. _bullets];
        _shardsView = [.. _shards];
        _logView = [.. _log];
    }

    // --- 描画 ---------------------------------------------------------------

    private void DrawArena()
    {
        Drawing.Box(32, 32, PanelX - 64, FloorY - 32, new Color(20, 24, 42));
        Drawing.Box(32, 32, PanelX - 64, FloorY - 32, new Color(64, 84, 132), thickness: 3);
        // 床。ここまで落とすとダメージ。
        Drawing.Box(32, FloorY - 4, PanelX - 64, 8, new Color(150, 70, 70, 180));
    }

    private void DrawBlocks()
    {
        foreach (var blk in _blocksView)
        {
            var color = blk.Tough ? new Color(206, 118, 236) : new Color(232, 96, 96);
            double size = BlockRadius * (1 + Math.Sin(blk.Age * 5) * 0.06);
            Drawing.Box(blk.Position.X - size, blk.Position.Y - size, size * 2, size * 2, color);
            Drawing.Box(blk.Position.X - size, blk.Position.Y - size, size * 2, size * 2,
                new Color(255, 226, 200), thickness: 2);
            if (blk.Tough)
                Drawing.Text(blk.Position.X, blk.Position.Y - 8, blk.Hp.ToString(), Color.White, ReferencePoint.TopCenter);
        }
    }

    private void DrawBullets()
    {
        foreach (var b in _bulletsView)
        {
            double r = b.Charged ? BulletRadius * 2 : BulletRadius;
            Drawing.Circle(b.Position.X, b.Position.Y, r,
                b.Charged ? new Color(150, 240, 255) : new Color(255, 224, 130));
        }
    }

    private void DrawShards()
    {
        foreach (var s in _shardsView)
        {
            double r = 11 + Math.Sin(s.Age * 9) * 2;
            Drawing.Circle(s.Position.X, s.Position.Y, r, new Color(120, 224, 168));
            Drawing.Circle(s.Position.X, s.Position.Y, r, new Color(24, 74, 54), thickness: 2);
        }
    }

    private void DrawPlayer()
    {
        var color = _invincible > 0 ? Color.Cyan : new Color(96, 186, 255);
        Drawing.Circle(_playerPos.X, _playerPos.Y, PlayerRadius, color);
        // 溜め具合を輪で見せる。Charge の音と絵が同じ量を指しているか比べられる。
        if (_chargeHeld > 0)
        {
            double t = Math.Min(1, _chargeHeld / 0.45);
            Drawing.Circle(_playerPos.X, _playerPos.Y, PlayerRadius + 6 + t * 10,
                new Color(150, 240, 255, (int)(60 + t * 160)), thickness: 3);
        }
    }

    private void DrawHud()
    {
        Drawing.Text(44, 40, $"Score {_score:00000}   Lv {_level}   HP {_hp}", Color.White);
        DemoUi.Note(44, 66, PanelX - 100,
            "移動 ←→/AD　撃つ Space（長押しで溜め撃ち・貫通）またはクリック　やり直し R");

        if (_gameOver)
        {
            Drawing.Text(PanelX / 2, FloorY / 2 - 20, "GAME OVER", Color.White, ReferencePoint.Center);
            Drawing.Text(PanelX / 2, FloorY / 2 + 12, "R でやり直し（Decide が鳴ります）",
                Color.LightGray, ReferencePoint.Center);
        }
    }

    private void DrawPanel()
    {
        const double x = PanelX + 8;
        double w = AstrumCore.Width - x - 12;

        DemoUi.Card(x, 32, w, 150, "Audio のボリューム", (cx, cy, cw) =>
        {
            double y = cy + 6;
            y = DemoUi.Note(cx + 10, y, cw - 20, $"Master {Audio.MasterVolume:0.0}   (Q / W)", Color.White);
            y = DemoUi.Note(cx + 10, y, cw - 20, $"Sfx    {Audio.SfxVolume:0.0}   (Z / X)", Color.White);
            y = DemoUi.Note(cx + 10, y, cw - 20, $"Bgm    {Audio.BgmVolume:0.0}   (C / V)", Color.White);
            y = DemoUi.Note(cx + 10, y, cw - 20, $"Muted  {(Audio.Muted ? "ON" : "off")}   (M)",
                Audio.Muted ? new Color(255, 150, 150) : null);
            DemoUi.Note(cx + 10, y, cw - 20, $"Bgm    {(_bgmOn ? "再生中" : "停止")}   (B)　Lv が上がると BPM も上がる");
        });

        DemoUi.Card(x, 190, w, 116, "AudioSelfCheck", (cx, cy, cw) =>
        {
            var color = SelfCheckOk switch
            {
                true => new Color(140, 230, 160),
                false => new Color(255, 140, 140),
                _ => new Color(200, 200, 140),
            };
            string head = SelfCheckOk switch { true => "PASS", false => "FAIL", _ => "検算中…" };
            double y = DemoUi.Note(cx + 10, cy + 6, cw - 20, head, color);
            // 終わるまでは見出しが「検算中…」なので、同じ文言を 2 行並べない。
            if (SelfCheckOk != null) DemoUi.Note(cx + 10, y, cw - 20, SelfCheckDetail);
        });

        DemoUi.Card(x, 314, w, AstrumCore.Height - 314 - 60, $"鳴らした音（{SfxRequests} 回）", (cx, cy, cw) =>
        {
            double y = cy + 6;
            foreach (var line in _logView)
            {
                DemoUi.NoteFont.Draw(cx + 10, y, line, new Color(186, 202, 232));
                y += DemoUi.LineHeight;
            }
            if (_logView.Length == 0)
                DemoUi.Note(cx + 10, y, cw - 20, "まだ何も鳴らしていません。");
        });
    }

    private struct Block
    {
        public Vector2 Position;
        public float Speed;
        public float Age;
        public int Hp;
        public bool Tough;
    }

    private struct Bullet
    {
        public Vector2 Position;
        public float Speed;
        public float Life;
        public bool Charged;
    }

    private struct Shard
    {
        public Vector2 Position;
        public float Age;
        public int Value;
    }
}
