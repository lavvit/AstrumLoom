using System.Diagnostics;

using DxLibDLL;

using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

public sealed class DxLibPlatform : IGamePlatform
{
    public GraphicsBackendKind BackendKind => GraphicsBackendKind.DxLib;

    public IGraphics Graphics { get; }
    public IInput Input { get; }
    public ITime Time { get; }

    public bool ShouldClose { get; private set; }
    public DxLibPlatform()
    {
        ChangeWindowMode(TRUE);                                 // ウィンドウモード
        SetGraphMode(1280, 720, 32); // 解像度
        SetBackgroundColor(0, 0, 0);                // デフォルト背景
        SetDrawScreen(DX_SCREEN_BACK);                  // 裏画面へ描画
        // 必要な設定いろいろ…
        if (DxLib_Init() < 0)
        {
            throw new Exception("DxLib_Init failed");
        }

        Time = new SimpleTime();
        Graphics = new DxLibGraphics(); // DummyGraphics の代わり
        Input = new DxLibInput();
    }

    public void PollEvents()
    {
        if (ShouldClose) return;

        // ウィンドウの×が押されたら != 0 になるので終了
        if (ProcessMessage() != 0)
        {
            ShouldClose = true;
            return;
        }

        if (CheckHitKey(KEY_INPUT_ESCAPE) != 0)
        {
            ShouldClose = true;
        }
    }

    public void Dispose() => DxLib_End();

    // --- 以下 stub 実装たち ---

    private sealed class DxLibTexture : ITexture
    {
        public int Handle { get; }
        public int Width { get; }
        public int Height { get; }

        public DxLibTexture(int handle, int width, int height)
        {
            Handle = handle;
            Width = width;
            Height = height;
        }
    }

    private sealed class DxLibGraphics : IGraphics
    {
        public ITexture LoadTexture(string path)
        {
            // 画像読み込み
            int handle = LoadGraph(path);
            if (handle < 0)
            {
                throw new Exception($"LoadGraph failed: {path}");
            }

            // サイズ取得
            if (GetGraphSize(handle, out int w, out int h) != 0)
            {
                // 失敗してもとりあえず 0 のまま返す
                w = h = 0;
            }

            return new DxLibTexture(handle, w, h);
        }

        public void UnloadTexture(ITexture texture)
        {
            if (texture is DxLibTexture tex)
            {
                DeleteGraph(tex.Handle);
            }
        }

        public void BeginFrame()
        {
            // 今は特に何もしない（必要ならここで状態リセット）
        }

        public void Clear(Color color)
        {
            // AstrumLoom.Color → System.Drawing.Color にキャストできるのでそれを使う
            var c = (System.Drawing.Color)color;
            SetBackgroundColor(c.R, c.G, c.B);
            ClearDrawScreen();
        }

        public void DrawTexture(
            ITexture texture,
            float x, float y,
            float scaleX = 1f,
            float scaleY = 1f,
            float rotationRad = 0f)
        {
            if (texture is not DxLibTexture tex) return;

            int ix = (int)x;
            int iy = (int)y;

            bool noRotate = Math.Abs(rotationRad) < 0.0001f;

            // 1. 拡大縮小も回転もなし → DrawGraph
            if (Math.Abs(scaleX - 1f) < 0.0001f &&
                Math.Abs(scaleY - 1f) < 0.0001f &&
                noRotate)
            {
                DrawGraph(ix, iy, tex.Handle, TRUE);
                return;
            }

            // 2. 回転なし・拡大縮小あり → DrawExtendGraph
            if (noRotate)
            {
                int x2 = ix + (int)(tex.Width * scaleX);
                int y2 = iy + (int)(tex.Height * scaleY);
                DrawExtendGraph(ix, iy, x2, y2, tex.Handle, TRUE);
                return;
            }

            // 3. 回転あり → 中心回りに回転させる
            double cx = tex.Width * 0.5;
            double cy = tex.Height * 0.5;
            double rad = rotationRad;

            DrawRotaGraph2F(
                (float)(ix + cx),
                (float)(iy + cy),
                (float)cx,
                (float)cy,
                scaleX,
                (float)rad,
                tex.Handle,
                TRUE);
        }

        public void EndFrame() => ScreenFlip();
    }

    private sealed class DxLibInput : IInput
    {
        public bool GetKey(Key key) => key switch
        {
            Key.Space => DX.CheckHitKey(DX.KEY_INPUT_SPACE) != 0,
            Key.Left => DX.CheckHitKey(DX.KEY_INPUT_LEFT) != 0,
            Key.Right => DX.CheckHitKey(DX.KEY_INPUT_RIGHT) != 0,
            Key.Up => DX.CheckHitKey(DX.KEY_INPUT_UP) != 0,
            Key.Down => DX.CheckHitKey(DX.KEY_INPUT_DOWN) != 0,
            Key.Escape => DX.CheckHitKey(DX.KEY_INPUT_ESCAPE) != 0,
            _ => false
        };

        // とりあえず GetKeyDown/Up は後でちゃんと実装
        public bool GetKeyDown(Key key) => GetKey(key);
        public bool GetKeyUp(Key key) => false;
    }

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
                if (DeltaTime > 0)
                    CurrentFps = 1f / DeltaTime;
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
