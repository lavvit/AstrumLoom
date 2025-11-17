namespace AstrumLoom;

/// <summary>
/// シーンクラス。
/// </summary>
public class Scene
{
    /// <summary>
    /// シーンの初期化を行います。
    /// </summary>
    public Scene()
    {
        Enabled = true;
        ChildScene = [];
    }

    ~Scene()
    {
        Enabled = false;
        ChildScene?.Clear();
    }

    /// <summary>
    /// アクティブ化する。
    /// </summary>
    public virtual void Enable()
    {
        if (!Enabled) return;
        foreach (var item in ChildScene)
        {
            item.Enable();
        }
    }

    /// <summary>
    /// 非アクティブ化する。
    /// </summary>
    public virtual void Disable()
    {
        if (!Enabled) return;
        foreach (var item in ChildScene)
        {
            item.Disable();
        }
        ChildScene.Clear();
    }

    /// <summary>
    /// DXLibが必要な処理(描画など)を行う。
    /// </summary>
    public virtual void Draw()
    {
        if (!Enabled) return;
    }
    /// <summary>
    /// デバッグ時の描画を行う。
    /// </summary>
    public virtual void Debug()
    {

        if (!Enabled) return;
#if !DEBUG
return;
#endif
    }
    /// <summary>
    /// 更新を行う。
    /// </summary>
    public virtual void Update()
    {
        if (!Enabled) return;
    }
    /// <summary>
    /// DXLibが必要な処理(タイマーやキーなど)を行う。
    /// </summary>
    public virtual void KeyUpdate()
    {
        if (!Enabled) return;
    }
    /// <summary>
    /// ファイルがドロップされた時の処理。
    /// </summary>
    public virtual void Drag(string str)
    {
        if (!Enabled) return;
    }

    /// <summary>
    /// そのシーンの名前(名前空間付き)を返します。
    /// </summary>
    /// <returns>そのシーンの名前(名前空間付き)。</returns>
    public override string ToString() => GetType().ToString();

    /// <summary>
    /// 利用可能かどうか。
    /// </summary>
    public bool Enabled { get; private set; }

    /// <summary>
    /// 子シーン。
    /// </summary>
    public List<Scene> ChildScene { get; private set; }

    /// <summary>
    /// 子シーンを追加します。
    /// </summary>
    /// <param name="scene">子シーン。</param>
    public void AddChildScene(Scene scene) => ChildScene.Add(scene);

    public static void Set(Scene scene, Scene[]? child = null)
    {
        GC.Collect();
        NowScene = scene;
        if (child != null)
        {
            foreach (var c in child)
            {
                NowScene.AddChildScene(c);
            }
        }
    }
    public static void Start() => NowScene.Enable();
    public static void Change(Scene scene, Scene[]? child = null)
    {
        GC.Collect();
        scene.Enable();
        NowScene.Disable();
        NowScene = scene;
        if (child != null)
        {
            foreach (var c in child)
            {
                NowScene.AddChildScene(c);
            }
        }
    }
    public static Scene NowScene { get; private set; } = new Scene();
}