using System.Text;

namespace AstrumLoom;

/// <summary>
/// バックエンド非依存のテキスト編集バッファ。
/// 文字列本体・キャレット位置・選択範囲を保持し、挿入/削除/カーソル移動/選択といった
/// 「どのプラットフォームでも同じであるべき」編集操作をここに集約する。
/// IME まわり（未確定文字列や変換候補）は各 ITextInput 実装側の責務で、ここでは扱わない。
/// </summary>
public sealed class TextEditBuffer
{
    private readonly StringBuilder _sb = new();

    /// <summary>キャレット位置（UTF-16 インデックス）。</summary>
    public int Caret { get; private set; }
    /// <summary>選択の開始点。Caret と一致していれば選択なし。</summary>
    public int Anchor { get; private set; }
    /// <summary>入力可能な最大文字数（UTF-16 char 数）。</summary>
    public int MaxLength { get; set => field = System.Math.Max(0, value); } = int.MaxValue;
    /// <summary>改行を許可するか。false なら挿入時に改行を除去する。</summary>
    public bool MultiLine { get; set; }
    /// <summary>挿入時に文字を受け付けるかどうかのフィルタ。null なら全て許可。</summary>
    public Func<char, bool>? Filter { get; set; }

    public int Length => _sb.Length;
    public string Text => _sb.ToString();
    public bool HasSelection => Caret != Anchor;
    /// <summary>選択範囲を [開始, 終了] の昇順で返す。</summary>
    public TextSelection Selection => new(System.Math.Min(Caret, Anchor), System.Math.Max(Caret, Anchor));

    /// <summary>内容・キャレット・選択をまとめて初期化する。</summary>
    public void Reset(string text)
    {
        _sb.Clear();
        _sb.Append(text);
        if (!MultiLine) StripNewLines();
        if (_sb.Length > MaxLength)
        {
            _sb.Length = MaxLength;
            if (_sb.Length > 0 && char.IsHighSurrogate(_sb[^1])) _sb.Length--;
        }
        Caret = Anchor = _sb.Length;
    }

    private void StripNewLines()
    {
        for (int i = _sb.Length - 1; i >= 0; i--)
            if (_sb[i] is '\r' or '\n') _sb.Remove(i, 1);
    }

    /// <summary>選択範囲を解除してキャレットだけを残す。</summary>
    public void ClearSelection() => Anchor = Caret;
    /// <summary>全選択する。</summary>
    public void SelectAll()
    {
        Anchor = 0;
        Caret = _sb.Length;
    }

    /// <summary>キャレット位置を直接指定する（範囲外は丸める）。</summary>
    public void SetCaret(int index, bool select = false)
    {
        Caret = System.Math.Clamp(index, 0, _sb.Length);
        // サロゲートペアの途中にキャレットが入らないよう、下位サロゲート側なら1つ戻す。
        if (Caret > 0 && Caret < _sb.Length && char.IsLowSurrogate(_sb[Caret])) Caret--;
        if (!select) Anchor = Caret;
    }

    /// <summary>選択中ならその範囲を削除する。削除したら true。</summary>
    public bool DeleteSelection()
    {
        if (!HasSelection) return false;
        var sel = Selection;
        _sb.Remove(sel.Start, sel.End - sel.Start);
        Caret = Anchor = sel.Start;
        return true;
    }

    /// <summary>キャレット位置に文字列を挿入する。選択中なら置き換える。</summary>
    public void Insert(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '\r') continue;
            if (c is '\n')
            {
                if (!MultiLine) continue;
            }
            // 上記以外の制御文字（Tab や IME が混ぜる 0x00 等）は取り込まない。
            else if (c < ' ' || c == 0x7F) continue;
            else if (Filter is not null && !Filter(c)) continue;
            sb.Append(c);
        }
        if (sb.Length == 0) return;

        int room = MaxLength - (_sb.Length - (Selection.End - Selection.Start));
        if (room <= 0) return;
        if (sb.Length > room)
        {
            sb.Length = room;
            // 切り詰めでサロゲートペアを割らない。
            if (sb.Length > 0 && char.IsHighSurrogate(sb[^1])) sb.Length--;
            if (sb.Length == 0) return;
        }

        DeleteSelection();
        _sb.Insert(Caret, sb);
        Caret += sb.Length;
        Anchor = Caret;
    }

    /// <summary>Backspace 相当。選択中なら選択を消し、そうでなければキャレット直前の1文字を消す。</summary>
    public void Backspace()
    {
        if (DeleteSelection()) return;
        if (Caret <= 0) return;
        int count = Caret >= 2 && char.IsLowSurrogate(_sb[Caret - 1]) && char.IsHighSurrogate(_sb[Caret - 2]) ? 2 : 1;
        _sb.Remove(Caret - count, count);
        Caret -= count;
        Anchor = Caret;
    }

    /// <summary>Delete 相当。選択中なら選択を消し、そうでなければキャレット直後の1文字を消す。</summary>
    public void Delete()
    {
        if (DeleteSelection()) return;
        if (Caret >= _sb.Length) return;
        int count = Caret + 1 < _sb.Length && char.IsHighSurrogate(_sb[Caret]) && char.IsLowSurrogate(_sb[Caret + 1]) ? 2 : 1;
        _sb.Remove(Caret, count);
        Anchor = Caret;
    }

    /// <summary>キャレットを左右に動かす。select が true ならアンカーを残して選択を伸ばす。</summary>
    public void Move(int delta, bool select)
    {
        // 選択中に矢印キー（非選択）を押したら、選択の端へ寄せるだけにする（一般的なテキストボックスの挙動）。
        if (!select && HasSelection)
        {
            var sel = Selection;
            Caret = Anchor = delta < 0 ? sel.Start : sel.End;
            return;
        }

        int next = Caret + delta;
        if (next < 0 || next > _sb.Length) return;
        // サロゲートペアはまとめて跨ぐ。
        if (delta < 0 && next > 0 && char.IsLowSurrogate(_sb[next])) next--;
        else if (delta > 0 && next < _sb.Length && char.IsLowSurrogate(_sb[next])) next++;
        Caret = next;
        if (!select) Anchor = Caret;
    }

    /// <summary>キャレットを行頭へ動かす。</summary>
    public void MoveHome(bool select) => SetCaret(LineStart(Caret), select);
    /// <summary>キャレットを行末へ動かす。</summary>
    public void MoveEnd(bool select) => SetCaret(LineEnd(Caret), select);

    /// <summary>キャレットを上下の行へ動かす（単一行なら行頭/行末へ）。</summary>
    public void MoveLine(int delta, bool select)
    {
        int start = LineStart(Caret);
        int column = Caret - start;
        if (delta < 0)
        {
            if (start == 0) { SetCaret(0, select); return; }
            int prevStart = LineStart(start - 1);
            SetCaret(System.Math.Min(prevStart + column, start - 1), select);
        }
        else
        {
            int end = LineEnd(Caret);
            if (end >= _sb.Length) { SetCaret(_sb.Length, select); return; }
            int nextStart = end + 1;
            int nextEnd = LineEnd(nextStart);
            SetCaret(System.Math.Min(nextStart + column, nextEnd), select);
        }
    }

    private int LineStart(int index)
    {
        for (int i = System.Math.Min(index, _sb.Length) - 1; i >= 0; i--)
            if (_sb[i] == '\n') return i + 1;
        return 0;
    }

    private int LineEnd(int index)
    {
        for (int i = System.Math.Clamp(index, 0, _sb.Length); i < _sb.Length; i++)
            if (_sb[i] == '\n') return i;
        return _sb.Length;
    }

    /// <summary>選択中の文字列を返す。選択が無ければ空文字。</summary>
    public string SelectedText()
    {
        var sel = Selection;
        return sel.End > sel.Start ? _sb.ToString(sel.Start, sel.End - sel.Start) : "";
    }
}
