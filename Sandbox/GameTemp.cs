using System;
using System.Collections.Generic;
using System.Numerics;
using AstrumLoom;

namespace Sandbox;

internal class GameTemplateScene : Scene
{
    private const float PlayerRadius = 18f;
    private const float PlayerSpeed = 320f;
    private const float BulletRadius = 6f;
    private const float BulletSpeed = 640f;
    private const float EnemyRadius = 22f;

    private readonly Random _random = new();
    private readonly List<Enemy> _enemies = new();
    private readonly List<Bullet> _bullets = new();
    private readonly List<Item> _items = new();

    private Vector2 _playerPos;
    private float _enemySpawnTimer;
    private float _shootCooldown;
    private float _invincibleTimer;
    private int _score;
    private int _itemsCollected;
    private int _playerHealth;
    private bool _gameOver;

    public override void Enable()
    {
        base.Enable();
        ResetGame();
    }

    public override void Disable()
    {
        base.Disable();
        _enemies.Clear();
        _bullets.Clear();
        _items.Clear();
    }

    public override void Update()
    {
        float delta = AstrumCore.Platform?.UTime.DeltaTime ?? (1f / 60f);
        delta = MathF.Min(delta, 0.05f);

        if (_gameOver)
        {
            if (Key.R.Push())
                ResetGame();
            return;
        }

        HandleMovement(delta);
        HandleShooting();

        UpdateBullets(delta);
        UpdateEnemies(delta);
        UpdateItems(delta);

        ResolveBulletHits();
        ResolvePlayerEnemyCollisions();
        ResolveItemPickup();

        _enemySpawnTimer -= delta;
        if (_enemySpawnTimer <= 0f)
            SpawnEnemy();

        _shootCooldown = MathF.Max(0f, _shootCooldown - delta);
        _invincibleTimer = MathF.Max(0f, _invincibleTimer - delta);
    }

    public override void Draw()
    {
        Drawing.Fill(new Color(16, 20, 32));
        DrawArena();
        DrawItems();
        DrawEnemies();
        DrawBullets();
        DrawPlayer();
        DrawHud();
    }

    private void ResetGame()
    {
        _playerPos = new Vector2(AstrumCore.Width / 2f, AstrumCore.Height / 2f);
        _enemySpawnTimer = 1.2f;
        _shootCooldown = 0f;
        _invincibleTimer = 0f;
        _score = 0;
        _itemsCollected = 0;
        _playerHealth = 3;
        _gameOver = false;
        _enemies.Clear();
        _bullets.Clear();
        _items.Clear();
    }

    private void HandleMovement(float delta)
    {
        Vector2 input = Vector2.Zero;
        if (Key.W.Hold() || Key.Up.Hold()) input.Y -= 1f;
        if (Key.S.Hold() || Key.Down.Hold()) input.Y += 1f;
        if (Key.A.Hold() || Key.Left.Hold()) input.X -= 1f;
        if (Key.D.Hold() || Key.Right.Hold()) input.X += 1f;

        if (input != Vector2.Zero)
        {
            input = Vector2.Normalize(input);
            _playerPos += input * PlayerSpeed * delta;
        }

        var min = new Vector2(48 + PlayerRadius, 48 + PlayerRadius);
        var max = new Vector2(AstrumCore.Width - 48 - PlayerRadius, AstrumCore.Height - 48 - PlayerRadius);
        _playerPos = Vector2.Clamp(_playerPos, min, max);
    }

    private void HandleShooting()
    {
        if (_shootCooldown > 0f)
            return;

        bool pressed = Key.Space.Push() || Key.Z.Push() || Mouse.Push(MouseButton.Left);
        if (!pressed)
            return;

        Vector2 target = new((float)Mouse.X, (float)Mouse.Y);
        Vector2 direction = target - _playerPos;
        direction = direction.LengthSquared() < 0.001f ? new Vector2(1f, 0f) : Vector2.Normalize(direction);

        _bullets.Add(new Bullet
        {
            Position = _playerPos,
            Direction = direction,
            Life = 1.2f
        });
        _shootCooldown = 0.18f;
    }

    private void UpdateBullets(float delta)
    {
        var bounds = new Vector2(AstrumCore.Width - 40, AstrumCore.Height - 40);
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            var bullet = _bullets[i];
            bullet.Position += bullet.Direction * BulletSpeed * delta;
            bullet.Life -= delta;
            if (bullet.Life <= 0f || bullet.Position.X < 40 || bullet.Position.X > bounds.X || bullet.Position.Y < 40 || bullet.Position.Y > bounds.Y)
            {
                _bullets.RemoveAt(i);
            }
            else
            {
                _bullets[i] = bullet;
            }
        }
    }

    private void UpdateEnemies(float delta)
    {
        var min = new Vector2(48 + EnemyRadius, 48 + EnemyRadius);
        var max = new Vector2(AstrumCore.Width - 48 - EnemyRadius, AstrumCore.Height - 48 - EnemyRadius);
        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            var enemy = _enemies[i];
            Vector2 toPlayer = _playerPos - enemy.Position;
            if (toPlayer.LengthSquared() > 0.001f)
            {
                toPlayer = Vector2.Normalize(toPlayer);
                enemy.Position += toPlayer * enemy.Speed * delta;
            }
            enemy.Position = Vector2.Clamp(enemy.Position, min, max);
            enemy.Age += delta;
            _enemies[i] = enemy;
        }
    }

    private void UpdateItems(float delta)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            item.Life -= delta;
            item.Age += delta;
            if (item.Life <= 0f)
            {
                _items.RemoveAt(i);
            }
            else
            {
                _items[i] = item;
            }
        }
    }

    private void ResolveBulletHits()
    {
        for (int b = _bullets.Count - 1; b >= 0; b--)
        {
            var bullet = _bullets[b];
            bool consumed = false;
            for (int e = _enemies.Count - 1; e >= 0; e--)
            {
                var enemy = _enemies[e];
                float range = enemy.Radius + BulletRadius;
                if (Vector2.DistanceSquared(enemy.Position, bullet.Position) <= range * range)
                {
                    _enemies.RemoveAt(e);
                    _score += 15;
                    SpawnItem(enemy.Position);
                    consumed = true;
                    break;
                }
            }
            if (consumed)
                _bullets.RemoveAt(b);
        }
    }

    private void ResolvePlayerEnemyCollisions()
    {
        if (_invincibleTimer > 0f)
            return;

        float danger = (EnemyRadius + PlayerRadius) * (EnemyRadius + PlayerRadius);
        foreach (var enemy in _enemies)
        {
            if (Vector2.DistanceSquared(enemy.Position, _playerPos) <= danger)
            {
                _playerHealth--;
                _invincibleTimer = 1.2f;
                if (_playerHealth <= 0)
                    _gameOver = true;
                break;
            }
        }
    }

    private void ResolveItemPickup()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            float range = item.Radius + PlayerRadius;
            if (Vector2.DistanceSquared(item.Position, _playerPos) <= range * range)
            {
                _score += item.Value;
                _itemsCollected++;
                if (_playerHealth < 4)
                    _playerHealth++;
                _items.RemoveAt(i);
            }
        }
    }

    private void SpawnEnemy()
    {
        float x, y;
        if (_random.Next(2) == 0)
        {
            x = (float)(48 + _random.NextDouble() * (AstrumCore.Width - 96));
            y = _random.Next(2) == 0 ? 48 : AstrumCore.Height - 48;
        }
        else
        {
            x = _random.Next(2) == 0 ? 48 : AstrumCore.Width - 48;
            y = (float)(48 + _random.NextDouble() * (AstrumCore.Height - 96));
        }

        _enemies.Add(new Enemy
        {
            Position = new Vector2(x, y),
            Radius = EnemyRadius,
            Speed = 130f + (float)_random.NextDouble() * 70f + _score * 0.35f,
            Age = 0f
        });

        float delay = MathF.Max(0.35f, 1.4f - _score * 0.012f);
        _enemySpawnTimer = delay;
    }

    private void SpawnItem(Vector2 position)
    {
        float offsetX = (float)(_random.NextDouble() - 0.5) * 20f;
        float offsetY = (float)(_random.NextDouble() - 0.5) * 20f;
        _items.Add(new Item
        {
            Position = position + new Vector2(offsetX, offsetY),
            Radius = 12f,
            Life = 8f,
            Age = 0f,
            Value = 25 + _random.Next(4) * 5
        });
    }

    private void DrawArena()
    {
        double arenaX = 32;
        double arenaY = 32;
        double arenaW = AstrumCore.Width - 64;
        double arenaH = AstrumCore.Height - 64;
        Drawing.Box(arenaX, arenaY, arenaW, arenaH, new Color(26, 32, 52));
        Drawing.Box(arenaX, arenaY, arenaW, arenaH, new Color(70, 92, 138), thickness: 4);
    }

    private void DrawItems()
    {
        foreach (var item in _items)
        {
            float pulse = 1f + MathF.Sin(item.Age * 8f) * 0.2f;
            float radius = item.Radius * pulse;
            var points = new (double x, double y)[]
            {
                (item.Position.X, item.Position.Y - radius),
                (item.Position.X + radius, item.Position.Y),
                (item.Position.X, item.Position.Y + radius),
                (item.Position.X - radius, item.Position.Y)
            };
            Drawing.Polygon(points, new Color(110, 216, 164));
            Drawing.Polygon(points, new Color(24, 72, 52), thickness: 2);
        }
    }

    private void DrawEnemies()
    {
        foreach (var enemy in _enemies)
        {
            float pulse = 1f + MathF.Sin(enemy.Age * 6f) * 0.1f;
            float radius = enemy.Radius * pulse;
            Drawing.Circle(enemy.Position.X, enemy.Position.Y, radius, new Color(204, 68, 68));
            Drawing.Circle(enemy.Position.X, enemy.Position.Y, radius, new Color(255, 180, 120), thickness: 4);
        }
    }

    private void DrawBullets()
    {
        foreach (var bullet in _bullets)
        {
            Drawing.Circle(bullet.Position.X, bullet.Position.Y, BulletRadius, new Color(255, 220, 120));
        }
    }

    private void DrawPlayer()
    {
        Color baseColor = _invincibleTimer > 0f ? Color.Cyan : new Color(90, 190, 255);
        Drawing.Circle(_playerPos.X, _playerPos.Y, PlayerRadius, baseColor);
        Drawing.Circle(_playerPos.X, _playerPos.Y, PlayerRadius + 4, new Color(255, 255, 255, 80), thickness: 3);
    }

    private void DrawHud()
    {
        Drawing.Text(44, 40, $"Score {_score:0000}  Items {_itemsCollected:000}  HP {_playerHealth}", Color.White);
        Drawing.Text(44, 68, "Move: WASD / Arrow  Shoot: Space or Click  Collect shards for points", Color.LightGray);

        if (_gameOver)
        {
            Drawing.Text(AstrumCore.Width / 2, AstrumCore.Height / 2 - 20, "GAME OVER", Color.White, ReferencePoint.Center);
            Drawing.Text(AstrumCore.Width / 2, AstrumCore.Height / 2 + 10, "Press R to restart", Color.LightGray, ReferencePoint.Center);
        }
        else
        {
            Drawing.Text(AstrumCore.Width - 48, AstrumCore.Height - 40, $"Enemies {_enemies.Count}", Color.LightGray, ReferencePoint.BottomRight);
        }
    }

    private struct Enemy
    {
        public Vector2 Position;
        public float Speed;
        public float Radius;
        public float Age;
    }

    private struct Bullet
    {
        public Vector2 Position;
        public Vector2 Direction;
        public float Life;
    }

    private struct Item
    {
        public Vector2 Position;
        public float Radius;
        public float Life;
        public float Age;
        public int Value;
    }
}
