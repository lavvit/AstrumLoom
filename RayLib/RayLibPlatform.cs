using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

using AstrumLoom;

using Raylib_cs;

using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

public sealed class RayLibPlatform : IGamePlatform
{
    public GraphicsBackendKind BackendKind => GraphicsBackendKind.RayLib;

    public IGraphics Graphics { get; }
    public IInput Input { get; }
    public ITime Time { get; }

    public bool ShouldClose { get; private set; }

    public RayLibPlatform(GameConfig config)
    {
        InitWindow(config.Width, config.Height, config.Title);

        if (!config.Resizable)
        {
            SetWindowState(ConfigFlags.UndecoratedWindow); // 例：必要なら調整
        }

        // AstrumLoom 側で FPS を管理するので、Raylib 側のターゲットFPSは 0 にしておく
        SetTargetFPS(0);

        Time = new SimpleTime { TargetFps = config.TargetFps };
        Graphics = new RayLibGraphics();
        Input = new RayLibInput();
    }

    public void PollEvents()
    {
        if (ShouldClose) return;

        if (WindowShouldClose())
        {
            ShouldClose = true;
        }
    }

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // ウィンドウが初期化済みのときだけ閉じる
        if (IsWindowReady())
        {
            CloseWindow();
        }
    }

    // ================================
    //  IGraphics 実装
    // ================================

    private sealed class RayLibTexture : ITexture
    {
        public Texture2D Native { get; }

        public int Width => Native.Width;
        public int Height => Native.Height;

        public RayLibTexture(Texture2D texture)
        {
            Native = texture;
        }
    }

    private sealed class RayLibGraphics : IGraphics
    {
        public ITexture LoadTexture(string path)
        {
            // 失敗時は Raylib が自前のエラーを出すので、ここではそのまま投げる
            Texture2D tex = Raylib.LoadTexture(path);
            return new RayLibTexture(tex);
        }

        public void UnloadTexture(ITexture texture)
        {
            if (texture is RayLibTexture t)
            {
                Raylib.UnloadTexture(t.Native);
            }
        }

        public void BeginFrame()
        {
            BeginDrawing();
        }

        public void Clear(Color color)
        {
            // AstrumLoom.Color → System.Drawing.Color → Raylib.Color
            var dc = (System.Drawing.Color)color;
            var rc = new Raylib_cs.Color(dc.R, dc.G, dc.B, dc.A);
            ClearBackground(rc);
        }

        public void DrawTexture(
            ITexture texture,
            float x, float y,
            float scaleX = 1f,
            float scaleY = 1f,
            float rotationRad = 0f)
        {
            if (texture is not RayLibTexture tex) return;

            // まずは一番シンプルなパス（回転・拡大なし）
            bool noScale =
                Math.Abs(scaleX - 1f) < 0.0001f &&
                Math.Abs(scaleY - 1f) < 0.0001f;
            bool noRotate = Math.Abs(rotationRad) < 0.0001f;

            if (noScale && noRotate)
            {
                Raylib.DrawTexture(tex.Native, (int)x, (int)y, Raylib_cs.Color.White);
                return;
            }

            // 拡大 or 回転あり → DrawTextureEx を使う（scale はとりあえず X を採用）
            float rotationDeg = rotationRad * 180f / (float)Math.PI;
            float scale = scaleX; // scaleY は必要ならあとで対応

            DrawTextureEx(
                tex.Native,
                new System.Numerics.Vector2(x, y),
                rotationDeg,
                scale,
                Raylib_cs.Color.White);
        }

        public void EndFrame()
        {
            EndDrawing();
        }
    }

    // ================================
    //  入力
    // ================================

    private sealed class RayLibInput : IInput
    {
        private static KeyboardKey ToRayKey(Key key) => key switch
        {
            Key.Space => KeyboardKey.Space,
            Key.Left => KeyboardKey.Left,
            Key.Right => KeyboardKey.Right,
            Key.Up => KeyboardKey.Up,
            Key.Down => KeyboardKey.Down,
            Key.Escape => KeyboardKey.Escape,
            _ => KeyboardKey.Null,
        };

        public bool GetKey(Key key)
        {
            var rk = ToRayKey(key);
            return rk != KeyboardKey.Null && IsKeyDown(rk);
        }

        public bool GetKeyDown(Key key)
        {
            var rk = ToRayKey(key);
            return rk != KeyboardKey.Null && IsKeyPressed(rk);
        }

        public bool GetKeyUp(Key key)
        {
            var rk = ToRayKey(key);
            return rk != KeyboardKey.Null && IsKeyReleased(rk);
        }
    }

    // ================================
    //  時間管理（DxLib版と同じノリ）
    // ================================

    private sealed class SimpleTime : ITime
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private long _lastTicks;

        public float DeltaTime { get; private set; }
        public float TotalTime => (float)_sw.Elapsed.TotalSeconds;
        public float CurrentFps { get; private set; }
        public float TargetFps { get; set; } = 60f;

        public void BeginFrame()
        {
            long now = _sw.ElapsedTicks;
            if (_lastTicks == 0)
            {
                DeltaTime = 0f;
            }
            else
            {
                long dtTicks = now - _lastTicks;
                DeltaTime = (float)dtTicks / Stopwatch.Frequency;
                if (DeltaTime > 0f)
                {
                    CurrentFps = 1f / DeltaTime;
                }
            }
            _lastTicks = now;
        }

        public void EndFrame()
        {
            if (TargetFps <= 0) return;

            double ideal = 1.0 / TargetFps;
            double remain = ideal - DeltaTime;
            if (remain > 0)
            {
                int ms = (int)(remain * 1000.0);
                if (ms > 0) Thread.Sleep(ms);
            }
        }
    }
}
