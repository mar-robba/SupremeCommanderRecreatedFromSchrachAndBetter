using System.Reflection;
using Raylib_cs;

namespace Rts.Client.Input;

/// <summary>Outcome of <see cref="Hotkeys.TryBind"/>.</summary>
public enum BindResult : byte { Ok, Clash, Fixed, Invalid }

/// <summary>
/// (10b-1) The player's key bindings: an override layer on the table (the table's defaults never change). Every entry
/// has a stable <see cref="Hotkey.Id"/> (its field name); <see cref="Hotkey.Keys"/> / <see cref="Hotkey.Mods"/> /
/// <see cref="Hotkey.Label"/> read the effective binding. Rules: a single key or alternatives (= / keypad +) rebind to
/// ONE key + modifiers; a row of slots (arrows, 0-9, 1-5, Z X C V B N, Ctrl+Shift+L A N E) rebinds as a whole row (the
/// same number of distinct keys, one modifier set); entries on Esc stay fixed. The table is kept clash-free
/// (<see cref="Clashes"/>: same key + mods in overlapping scopes without a common Share tag). Persisted as one string in
/// <c>AppSettings.KeyBindings</c>: <c>"v1;Stop=Ctrl+X;BuildSlots=Q W E R T Y"</c> (only non-default entries; raylib key
/// names, digits also "1"); unknown ids, bad keys and clashing entries are ignored, never a crash.
/// </summary>
public static partial class Hotkeys
{
    public const int BindingsVersion = 1;

    /// <summary>Keyboard source of <see cref="Hotkey.Pressed"/> (tests inject a fake).</summary>
    public static IKeyState Input = RaylibKeyState.Instance;

    /// <summary>Bumped on every binding change (for caches of labels).</summary>
    public static int Revision { get; private set; }

    private static Dictionary<string, Hotkey>? _byId;

    private static int AssignIds()
    {
        int n = 0;
        foreach (var f in typeof(Hotkeys).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (f.FieldType == typeof(Hotkey) && f.GetValue(null) is Hotkey h) { h.Id = f.Name; n++; }
        return n;
    }

    /// <summary>The entry with this id (case-sensitive field name), or null.</summary>
    public static Hotkey? ById(string id)
    {
        _byId ??= All.ToDictionary(h => h.Id, StringComparer.Ordinal);
        return _byId.GetValueOrDefault(id);
    }

    /// <summary>Same effective keys and modifiers (shared keys such as D / E / U / M resolve by selection only then).</summary>
    public static bool SameBinding(Hotkey a, Hotkey b) => a.Mods == b.Mods && a.Keys.AsSpan().SequenceEqual(b.Keys);

    /// <summary>Keys that can never be bound (modifiers alone, Esc, Super).</summary>
    public static bool Bindable(KeyboardKey k) => k is not (KeyboardKey.Null or KeyboardKey.Escape
        or KeyboardKey.LeftShift or KeyboardKey.RightShift or KeyboardKey.LeftControl or KeyboardKey.RightControl
        or KeyboardKey.LeftAlt or KeyboardKey.RightAlt or KeyboardKey.LeftSuper or KeyboardKey.RightSuper)
        && Enum.IsDefined(k);

    /// <summary>Is this a valid binding for <paramref name="h"/> (count, distinct, bindable keys)?</summary>
    public static bool ValidBinding(Hotkey h, KeyboardKey[] keys)
    {
        if (keys.Length == 0 || keys.Length != (h.IsRow ? h.DefaultKeys.Length : 1) && !(h.Alternatives && keys.Length <= h.DefaultKeys.Length)) return false;
        for (int i = 0; i < keys.Length; i++)
        {
            if (!Bindable(keys[i])) return false;
            for (int j = 0; j < i; j++) if (keys[j] == keys[i]) return false;
        }
        return true;
    }

    private static bool Clash(Hotkey other, KeyboardKey[] keys, KeyMods mods, Hotkey h)
    {
        if (other.Mods != mods || !Overlaps(other.Scope, h.Scope)) return false;
        if (h.Share != null && h.Share == other.Share) return false;
        foreach (var k in keys) if (other.Keys.Contains(k)) return true;
        return false;
    }

    /// <summary>The other entries that would clash if <paramref name="h"/> got this binding.</summary>
    public static List<Hotkey> ClashesWith(Hotkey h, KeyboardKey[] keys, KeyMods mods)
    {
        var res = new List<Hotkey>();
        foreach (var o in All) if (!ReferenceEquals(o, h) && Clash(o, keys, mods, h)) res.Add(o);
        return res;
    }

    /// <summary>Binds <paramref name="h"/> unless the binding is invalid, the entry fixed or another entry clashes
    /// (<paramref name="clashes"/>; nothing changes then: the caller may offer <see cref="TrySwap"/>).</summary>
    public static BindResult TryBind(Hotkey h, KeyboardKey[] keys, KeyMods mods, out List<Hotkey> clashes)
    {
        clashes = [];
        if (!h.Rebindable) return BindResult.Fixed;
        if (!ValidBinding(h, keys)) return BindResult.Invalid;
        clashes = ClashesWith(h, keys, mods);
        if (clashes.Count > 0) return BindResult.Clash;
        Set(h, keys, mods);
        return BindResult.Ok;
    }

    /// <summary>Binds <paramref name="h"/> and gives the clashing entries its old binding. Refused (nothing changes,
    /// <paramref name="why"/> says why) when a clashing entry is fixed or has another number of keys, or when the swap
    /// would leave any clash in the table.</summary>
    public static bool TrySwap(Hotkey h, KeyboardKey[] keys, KeyMods mods, out string? why)
    {
        why = null;
        var r = TryBind(h, keys, mods, out var clashes);
        if (r == BindResult.Ok) return true;
        if (r != BindResult.Clash) { why = r == BindResult.Fixed ? "this key is fixed" : "not a valid key"; return false; }
        var oldKeys = h.Keys;
        var oldMods = h.Mods;
        foreach (var c in clashes)
            if (!c.Rebindable || !ValidBinding(c, oldKeys)) { why = $"'{c.Action}' cannot take {h.Label}"; return false; }
        var undo = clashes.Select(c => (c, c.Keys, c.Mods)).ToList();
        h.SetBinding(keys, mods);
        foreach (var c in clashes) c.SetBinding(oldKeys, oldMods);
        var left = Clashes(All);
        if (left.Count > 0)
        {
            h.SetBinding(oldKeys, oldMods);
            foreach (var (c, k, m) in undo) c.SetBinding(k, m);
            why = $"{left[0].A.Action} / {left[0].B.Action} would clash on {KeyName(left[0].Key)}";
            return false;
        }
        Revision++;
        return true;
    }

    /// <summary>Back to the table's default (refused like <see cref="TryBind"/> when another entry took that key).</summary>
    public static BindResult Reset(Hotkey h, out List<Hotkey> clashes) => TryBind(h, h.DefaultKeys, h.DefaultMods, out clashes);

    public static void ResetAll()
    {
        foreach (var h in All) if (!h.IsDefault) h.SetBinding(h.DefaultKeys, h.DefaultMods);
        Revision++;
    }

    private static void Set(Hotkey h, KeyboardKey[] keys, KeyMods mods)
    {
        h.SetBinding(keys, mods);
        Revision++;
    }

    // ------------------------------------------------------------------ text form (settings file, --bind)

    /// <summary>"Ctrl+Shift+Z", "Q W E R T Y", "Alt+One Two" (raylib names; parsed by <see cref="TryParseSpec"/>).</summary>
    public static string Spec(KeyboardKey[] keys, KeyMods mods) => ModsPrefix(mods) + string.Join(" ", keys.Select(k => k.ToString()));

    public static bool TryParseSpec(string s, out KeyboardKey[] keys, out KeyMods mods)
    {
        keys = [];
        mods = KeyMods.None;
        var tokens = s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        var parts = tokens[0].Split('+');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].Trim().ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= KeyMods.Ctrl; break;
                case "shift": mods |= KeyMods.Shift; break;
                case "alt": mods |= KeyMods.Alt; break;
                default: return false;
            }
        }
        tokens[0] = parts[^1];
        var list = new KeyboardKey[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
            if (!TryParseKey(tokens[i], out list[i])) return false;
        keys = list;
        return true;
    }

    public static bool TryParseKey(string t, out KeyboardKey k)
    {
        k = KeyboardKey.Null;
        if (t.Length == 1 && t[0] is >= '0' and <= '9') { k = KeyboardKey.Zero + (t[0] - '0'); return true; }
        if (t is ".") { k = KeyboardKey.Period; return true; }
        if (t is "`") { k = KeyboardKey.Grave; return true; }
        if (t.Length == 0 || char.IsDigit(t[0]) || t[0] == '-') return false; // Enum.TryParse would accept numbers
        return Enum.TryParse(t, ignoreCase: true, out k) && Bindable(k);
    }

    /// <summary>The non-default bindings as the settings string (empty = all defaults).</summary>
    public static string ExportBindings()
    {
        var parts = All.Where(h => !h.IsDefault).Select(h => h.Id + "=" + Spec(h.Keys, h.Mods)).ToList();
        return parts.Count == 0 ? "" : "v" + BindingsVersion + ";" + string.Join(";", parts);
    }

    /// <summary>Settings file: resets every entry, then applies the saved bindings (see <see cref="ApplyBindings"/>).</summary>
    public static int LoadBindings(string? saved)
    {
        ResetAll();
        return ApplyBindings(saved, out _);
    }

    /// <summary>
    /// Applies "Id=Spec" items (separated by ';' or ','; a "vN" item = format version) on top of the current bindings.
    /// Unknown ids, unparsable or invalid keys and fixed entries are skipped (<paramref name="skipped"/>); then, while
    /// the table has a clash, the later-applied entry of the pair goes back to its default. Returns the entries applied.
    /// </summary>
    public static int ApplyBindings(string? text, out List<string> skipped)
    {
        skipped = [];
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var applied = new List<Hotkey>();
        foreach (var raw in text.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = raw.IndexOf('=');
            if (eq < 0)
            {
                if (!(raw.Length > 1 && raw[0] == 'v' && int.TryParse(raw.AsSpan(1), out _))) skipped.Add(raw);
                continue; // versions > 1 are read best-effort (same item format)
            }
            var h = ById(raw[..eq].Trim());
            if (h == null || !h.Rebindable || !TryParseSpec(raw[(eq + 1)..], out var keys, out var mods) || !ValidBinding(h, keys))
            {
                skipped.Add(raw);
                continue;
            }
            h.SetBinding(keys, mods);
            applied.Remove(h);
            applied.Add(h);
        }
        for (var cl = Clashes(All); cl.Count > 0; cl = Clashes(All))
        {
            var (a, b, _) = cl[0];
            int ia = applied.IndexOf(a), ib = applied.IndexOf(b);
            // The one applied later; an entry still on its default is never the one to undo (defaults never clash).
            var undo = a.IsDefault ? b : b.IsDefault ? a : ia > ib ? a : b;
            skipped.Add(undo.Id + "=" + Spec(undo.Keys, undo.Mods));
            undo.SetBinding(undo.DefaultKeys, undo.DefaultMods);
            applied.Remove(undo);
        }
        Revision++;
        return applied.Count;
    }
}
