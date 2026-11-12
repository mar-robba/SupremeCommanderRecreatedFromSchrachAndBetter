using Raylib_cs;
using Rts.Client.Input;
using Rts.Client.UI;

namespace Rts.Client.Menus;

/// <summary>
/// (10b-1) The "Keys" tab of <see cref="SettingsScreen"/>: the hotkey table by help group; click an entry's key →
/// "press a key" capture (Shift / Ctrl / Alt held = modifiers; a row of slots asks for each key in turn; Esc cancels);
/// a key used by another entry is refused with a prompt that offers to swap (<see cref="Hotkeys.TrySwap"/>); right
/// click or "Reset" = that entry's default; "Reset all". Entries on Esc are fixed. The bindings are saved with the
/// other settings when the settings screen closes (<see cref="Changed"/>).
/// </summary>
public sealed class KeyBindingsScreen
{
    /// <summary><c>--keys-capture ID</c>: open the capture prompt for that entry (screenshots).</summary>
    public static string? CaptureCli;

    /// <summary>A binding changed (the settings screen saves <see cref="Hotkeys.ExportBindings"/>).</summary>
    public bool Changed { get; private set; }
    /// <summary>The keyboard belongs to the capture / clash prompt (Esc must not close the settings).</summary>
    public bool Capturing => _cap != null || _pending != null;

    private Hotkey? _cap;
    private readonly List<KeyboardKey> _capKeys = [];
    private KeyMods _capMods;
    private Hotkey? _pending;
    private KeyboardKey[] _pendKeys = [];
    private KeyMods _pendMods;
    private List<Hotkey> _pendClash = [];
    private string _status = "";
    private Color _statusColor = Ui.TextDim;
    private float _scroll;

    private static readonly (string? Group, Hotkey? Key)[] Items =
        Hotkeys.Groups.SelectMany(g => new (string?, Hotkey?)[] { (g, null) }.Concat(Hotkeys.All.Where(h => h.Group == g).Select(h => ((string?)null, (Hotkey?)h)))).ToArray();

    public KeyBindingsScreen()
    {
        if (CaptureCli != null && Hotkeys.ById(CaptureCli) is Hotkey h) Begin(h);
        CaptureCli = null;
    }

    public void Draw(Rectangle panel)
    {
        float pad = Ui.P(14);
        int fs = Ui.F(12);
        Ui.Text("Click a key to change it (hold Shift / Ctrl / Alt for a combination). Right click: default.", panel.X + pad, panel.Y + Ui.P(12), Ui.F(11.5f), Ui.TextDim);
        var resetAll = Ui.R(panel.X + panel.Width - pad - Ui.P(100), panel.Y + Ui.P(7), Ui.P(100), Ui.P(24));
        if (MenuUi.BigButton(resetAll, "Reset all", !Capturing, false, 12) && !Capturing)
        {
            Hotkeys.ResetAll();
            Changed = true;
            SetStatus("Every key is back to its default.", Ui.Good);
        }

        var list = Ui.R(panel.X + Ui.P(6), panel.Y + Ui.P(38), panel.Width - Ui.P(12), panel.Height - Ui.P(38) - Ui.P(30));
        float rowH = Ui.P(23);
        float content = Items.Length * rowH;
        if (!Capturing && Ui.Hover(list)) _scroll -= Raylib.GetMouseWheelMove() * rowH * 3;
        _scroll = Math.Clamp(_scroll, 0, MathF.Max(0, content - list.Height));

        Raylib.BeginScissorMode((int)list.X, (int)list.Y, (int)list.Width, (int)list.Height);
        bool inList = Ui.Hover(list) && !Capturing;
        float keyX = list.X + list.Width - Ui.P(300), keyW = Ui.P(210);
        for (int i = 0; i < Items.Length; i++)
        {
            float y = list.Y + i * rowH - _scroll;
            if (y + rowH < list.Y || y > list.Y + list.Height) continue;
            var (group, h) = Items[i];
            if (group != null)
            {
                Ui.Text(group.ToUpperInvariant(), list.X + Ui.P(8), y + Ui.P(6), Ui.F(11), Ui.Accent, true);
                continue;
            }
            var row = Ui.R(list.X, y, list.Width - Ui.P(10), rowH - Ui.P(2));
            if (inList && Ui.Hover(row)) Raylib.DrawRectangleRec(row, new Color(40, 56, 76, 120));
            Ui.Text(Ui.Fit(h!.Action, keyX - list.X - Ui.P(30), fs), list.X + Ui.P(20), y + (rowH - fs) * 0.5f - 1, fs, Ui.TextColor);
            var kr = Ui.R(keyX, y + Ui.P(1), keyW, rowH - Ui.P(4));
            bool active = ReferenceEquals(_cap, h) || ReferenceEquals(_pending, h);
            if (!h.Rebindable)
            {
                Ui.TextCentered(h.Label + "  (fixed)", kr, fs, Ui.TextFaint);
                continue;
            }
            var st = inList ? Ui.Button(kr, true, active) : default;
            if (!inList) Ui.Button(kr, false, active);
            Ui.TextCentered(active && _cap != null ? "press a key…" : h.Label, kr, fs, h.IsDefault ? Ui.TextColor : Ui.Warn, !h.IsDefault);
            if (st.Left) Begin(h);
            else if (st.Right) ResetOne(h);
            if (!h.IsDefault)
            {
                var rr = Ui.R(keyX + keyW + Ui.P(8), y + Ui.P(1), Ui.P(64), rowH - Ui.P(4));
                if (MenuUi.BigButton(rr, "Reset", inList, false, 11) && inList) ResetOne(h);
            }
        }
        Raylib.EndScissorMode();
        if (content > list.Height)
        {
            float bh = list.Height * list.Height / content, by = list.Y + (list.Height - bh) * (_scroll / (content - list.Height));
            Raylib.DrawRectangleRec(Ui.R(list.X + list.Width - Ui.P(4), by, Ui.P(3), bh), Ui.PanelBorder);
        }
        Ui.Text(_status, panel.X + pad, panel.Y + panel.Height - Ui.P(22), Ui.F(11.5f), _statusColor);

        if (_cap != null) CapturePrompt(panel);
        else if (_pending != null) ClashPrompt(panel);
    }

    // ------------------------------------------------------------------ capture

    private void Begin(Hotkey h)
    {
        _cap = h;
        _capKeys.Clear();
        _pending = null;
        while (Raylib.GetKeyPressed() != 0) { } // drop keys queued before the click
    }

    private void CapturePrompt(Rectangle panel)
    {
        var h = _cap!;
        int need = h.IsRow ? h.DefaultKeys.Length : 1;
        if (!MenuUi.Automated)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                _cap = null;
                SetStatus("Cancelled.", Ui.TextDim);
                return;
            }
            for (int k = Raylib.GetKeyPressed(); k != 0 && _cap != null; k = Raylib.GetKeyPressed())
            {
                var key = (KeyboardKey)k;
                if (!Hotkeys.Bindable(key)) continue;
                if (_capKeys.Count == 0) _capMods = HeldMods();
                if (_capKeys.Contains(key)) { SetStatus(Hotkeys.KeyName(key) + " is already in this row.", Ui.Warn); continue; }
                _capKeys.Add(key);
                if (_capKeys.Count == need) Finish(h, [.. _capKeys], _capMods);
            }
        }
        if (_cap == null) return;
        var box = Modal(panel, Ui.P(150));
        Ui.TextCentered("Press the new key for", Ui.R(box.X, box.Y + Ui.P(14), box.Width, Ui.P(16)), Ui.F(12), Ui.TextDim);
        Ui.TextCentered(Ui.Fit(h.Action, box.Width - Ui.P(30), Ui.F(14), true), Ui.R(box.X, box.Y + Ui.P(36), box.Width, Ui.P(18)), Ui.F(14), Ui.TextColor, true);
        string sofar = need > 1
            ? $"Row of {need} keys, key {_capKeys.Count + 1} of {need}" + (_capKeys.Count > 0 ? ":  " + Preview([.. _capKeys], _capMods) : "")
            : "Now: " + h.Label + (h.IsDefault ? "" : "   (default " + DefaultLabel(h) + ")");
        Ui.TextCentered(sofar, Ui.R(box.X, box.Y + Ui.P(66), box.Width, Ui.P(16)), Ui.F(12), Ui.Accent);
        Ui.TextCentered("Hold Shift / Ctrl / Alt for a combination.  Esc cancels.", Ui.R(box.X, box.Y + Ui.P(96), box.Width, Ui.P(16)), Ui.F(11.5f), Ui.TextFaint);
        if (Hotkeys.Groups.Contains(h.Group))
            Ui.TextCentered("Group: " + h.Group, Ui.R(box.X, box.Y + Ui.P(118), box.Width, Ui.P(16)), Ui.F(11), Ui.TextFaint);
    }

    private void Finish(Hotkey h, KeyboardKey[] keys, KeyMods mods)
    {
        _cap = null;
        switch (Hotkeys.TryBind(h, keys, mods, out var clashes))
        {
            case BindResult.Ok:
                Changed = true;
                SetStatus($"{h.Action}: {h.Label}", Ui.Good);
                break;
            case BindResult.Clash:
                (_pending, _pendKeys, _pendMods, _pendClash) = (h, keys, mods, clashes);
                break;
            default:
                SetStatus("That key cannot be bound.", Ui.Bad);
                break;
        }
    }

    private void ResetOne(Hotkey h)
    {
        if (h.IsDefault) return;
        Finish(h, h.DefaultKeys, h.DefaultMods);
    }

    private void ClashPrompt(Rectangle panel)
    {
        var h = _pending!;
        var box = Modal(panel, Ui.P(170));
        string keys = Preview(_pendKeys, _pendMods);
        Ui.TextCentered($"{keys} is already used by", Ui.R(box.X, box.Y + Ui.P(14), box.Width, Ui.P(16)), Ui.F(12.5f), Ui.Warn, true);
        float y = box.Y + Ui.P(38);
        foreach (var c in _pendClash.Take(3))
        {
            Ui.TextCentered(Ui.Fit($"{c.Group}: {c.Action}", box.Width - Ui.P(30), Ui.F(12)), Ui.R(box.X, y, box.Width, Ui.P(16)), Ui.F(12), Ui.TextColor);
            y += Ui.P(18);
        }
        Ui.TextCentered($"Swap: they get {h.Label}, \"{Ui.Fit(h.Action, Ui.P(200), Ui.F(11))}\" gets {keys}.", Ui.R(box.X, box.Y + Ui.P(98), box.Width, Ui.P(16)), Ui.F(11), Ui.TextFaint);
        float bw = Ui.P(110), bx = box.X + box.Width * 0.5f - bw - Ui.P(6), byy = box.Y + box.Height - Ui.P(42);
        bool swap = MenuUi.BigButton(Ui.R(bx, byy, bw, Ui.P(30)), "Swap", true, true, 13);
        bool cancel = MenuUi.BigButton(Ui.R(bx + bw + Ui.P(12), byy, bw, Ui.P(30)), "Cancel", true, false, 13) || MenuUi.Key(KeyboardKey.Escape);
        if (swap)
        {
            _pending = null;
            if (Hotkeys.TrySwap(h, _pendKeys, _pendMods, out var why)) { Changed = true; SetStatus($"Swapped: {h.Action} = {h.Label}", Ui.Good); }
            else SetStatus("Cannot swap: " + why, Ui.Bad);
        }
        else if (cancel)
        {
            _pending = null;
            SetStatus($"Not changed ({keys} is taken).", Ui.TextDim);
        }
    }

    private static Rectangle Modal(Rectangle panel, float h)
    {
        Raylib.DrawRectangleRec(panel, new Color(4, 7, 11, 150));
        var box = Ui.R(panel.X + panel.Width * 0.5f - Ui.P(230), panel.Y + panel.Height * 0.5f - h * 0.5f, Ui.P(460), h);
        Ui.Panel(box);
        return box;
    }

    private static KeyMods HeldMods() =>
        (Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl) ? KeyMods.Ctrl : 0)
        | (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift) ? KeyMods.Shift : 0)
        | (Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt) ? KeyMods.Alt : 0);

    private static string Preview(KeyboardKey[] keys, KeyMods mods) => Hotkeys.ModsPrefix(mods) + string.Join(" ", keys.Select(Hotkeys.KeyName));

    private static string DefaultLabel(Hotkey h) => Preview(h.DefaultKeys, h.DefaultMods);

    private void SetStatus(string s, Color c) => (_status, _statusColor) = (s, c);
}
