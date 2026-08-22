namespace AstrumLoom;

/// <summary>
/// F1〜F6 の個別ホットキーが使えない（<see cref="DebugHotkeyMode.MenuOnly"/> や
/// <see cref="DebugHotkeyMode.Modifier"/>）ときの代替操作パネル。
/// Modifier + <see cref="DebugControl.KeyMenu"/>（既定 Ctrl+F1）で開閉する。
/// </summary>
/// <remarks>
/// 開いている間もゲームは動き続けます（意図的に一時停止しません）。
/// 停止したい場合はメニューから「一時停止」を選んでください。
/// </remarks>
public static class DebugMenu
{
    /// <summary>メニュー 1 項目。<see cref="Status"/> は右側に出す現在状態の文字列（無ければ null）。</summary>
    public sealed record DebugMenuItem(string Label, Action Action, Func<string>? Status = null);

    /// <summary>既定項目。<see cref="Clear"/> しても消えません。</summary>
    private static readonly List<DebugMenuItem> _defaultItems =
    [
        new("オーバーレイ表示切替",
            () => DebugControl.ShowOverlay = !DebugControl.ShowOverlay,
            () => DebugControl.ShowOverlay ? "表示" : "非表示"),
        new("スクリーンショット",
            () => Snapshot.Request("manual")),
        new("スロー切替",
            () =>
            {
                int[] steps = [1, 2, 4, 8];
                int index = Array.IndexOf(steps, DebugControl.SlowFactor);
                DebugControl.SetSlow(steps[(index + 1) % steps.Length]);
            },
            () => DebugControl.SlowFactor > 1 ? $"1/{DebugControl.SlowFactor}" : "等速"),
        new("一時停止・再開",
            () => DebugControl.TogglePause(),
            () => DebugControl.Paused ? "停止中" : "動作中"),
        new("コマ送り",
            () => DebugControl.Step()),
        new("チューニング再読込",
            () =>
            {
                Tune.Poll(force: true);
                if (Tune.LoadCount == 0) Tune.Save();
            }),
        new("閉じる",
            Close),
    ];

    /// <summary>ゲーム側が追加した項目。</summary>
    private static readonly List<DebugMenuItem> _extraItems = [];

    /// <summary>ゲーム側拡張用に項目を追加します。既定項目より下に並びます。</summary>
    public static void Add(string label, Action action, Func<string>? status = null)
        => _extraItems.Add(new DebugMenuItem(label, action, status));

    /// <summary>ゲーム側が追加した項目を、ラベル一致で削除します。既定項目は消せません。</summary>
    public static void Remove(string label)
        => _extraItems.RemoveAll(i => i.Label == label);

    /// <summary>ゲーム側が追加した項目を全て消します。既定項目は残ります。</summary>
    public static void Clear() => _extraItems.Clear();

    private static IEnumerable<DebugMenuItem> Items => _defaultItems.Concat(_extraItems);

    /// <summary>メニューが開いているか。</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>選択中の行インデックス。</summary>
    private static int _selected;

    public static void Open()
    {
        IsOpen = true;
        _selected = 0;
    }
    public static void Close() => IsOpen = false;
    public static void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    private static readonly Color BackColor = Color.Black;
    private static readonly Color HeaderColor = new(230, 240, 255);
    private static readonly Color ItemColor = new(200, 210, 220);
    private static readonly Color StatusColor = new(150, 190, 255);
    private static readonly Color SelectedColor = new(255, 220, 120);

    /// <summary>メニュー操作を処理します。<see cref="DebugControl.PollHotkeys"/> から、開いている間だけ呼ばれます。</summary>
    internal static void Poll(IInput input)
    {
        var items = Items.ToList();
        if (items.Count == 0) return;

        if (input.GetKeyDown(Key.Up))
            _selected = (_selected - 1 + items.Count) % items.Count;
        if (input.GetKeyDown(Key.Down))
            _selected = (_selected + 1) % items.Count;

        if (_selected < 0 || _selected >= items.Count) _selected = 0;

        if (input.GetKeyDown(Key.Enter) || input.GetKeyDown(Key.Space))
            items[_selected].Action();

        if (input.GetKeyDown(Key.Esc))
            Close();
    }

    /// <summary>画面中央付近にメニューを描画します。<c>Game.cs</c> の描画パスから、開いている間だけ呼ばれます。</summary>
    internal static void Draw()
    {
        var items = Items.ToList();
        if (items.Count == 0) return;

        const string header = "DEBUG MENU (↑↓ 選択 / Enter 決定 / Esc 閉じる)";
        int size = Math.Max(8, Drawing.FontSize());

        int width = Drawing.TextSize(header).width;
        var rows = new (string label, string status)[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            string status = items[i].Status?.Invoke() ?? "";
            rows[i] = (items[i].Label, status);
            width = Math.Max(width, Drawing.TextSize($"{items[i].Label}    {status}").width);
        }

        const int pad = 12;
        const int lineGap = 4;
        int lineHeight = size + lineGap;
        int boxWidth = width + pad * 2;
        int boxHeight = lineHeight * (items.Count + 1) + pad * 2;

        double left = (AstrumCore.Width - boxWidth) / 2.0;
        double top = (AstrumCore.Height - boxHeight) / 2.0;

        Drawing.Box(left, top, boxWidth, boxHeight, BackColor, opacity: 0.75);

        double x = left + pad;
        double y = top + pad;
        Drawing.Text(x, y, header, HeaderColor);
        y += lineHeight;

        for (int i = 0; i < rows.Length; i++)
        {
            var (label, status) = rows[i];
            bool selected = i == _selected;
            Color color = selected ? SelectedColor : ItemColor;
            string prefix = selected ? "> " : "  ";
            Drawing.Text(x, y, prefix + label, color);
            if (status.Length > 0)
                Drawing.Text(left + boxWidth - pad, y, status, StatusColor, point: ReferencePoint.TopRight);
            y += lineHeight;
        }
    }
}
