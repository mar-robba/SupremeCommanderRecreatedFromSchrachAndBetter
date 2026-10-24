using Raylib_cs;

namespace Rts.Client.Input;

/// <summary>Where a hotkey is active. See <see cref="Hotkeys.Overlaps"/> for which scopes can be active together.</summary>
public enum HotkeyScope : byte
{
    /// <summary>Always in a game or replay (camera, fog, help, sound).</summary>
    Game,
    /// <summary>A game you play (not a replay): speed, pause, saves, idle buttons.</summary>
    Play,
    /// <summary>Own units selected: orders and toggles.</summary>
    Units,
    /// <summary>The engineer build menu is shown (engineers / commander selected).</summary>
    BuildMenu,
    /// <summary>The factory panel is shown (factories selected, no build menu).</summary>
    Factory,
    /// <summary>Placing a structure.</summary>
    Placement,
    /// <summary>A modal dialog (self-destruct confirmation): every other key is blocked.</summary>
    Dialog,
    /// <summary>The replay viewer.</summary>
    Replay,
    /// <summary>Sandbox mode (panel, console).</summary>
    Sandbox,
    /// <summary>Debug keys (effect tests), off in the sandbox.</summary>
    Debug,
}

[Flags]
public enum KeyMods : byte { None = 0, Shift = 1, Ctrl = 2, Alt = 4 }

/// <summary>
/// One entry of the hotkey table: keys (several = a row of slots, e.g. Z X C V B N, or alternatives such as = and
/// keypad +), modifiers, scope, help group and text. <see cref="Share"/>: keys deliberately bound to several actions
/// in overlapping scopes (what they do depends on the selection, e.g. D = dive AND dock, see SharedHotkeys) carry the
/// same Share tag; anything else with the same key + modifiers in overlapping scopes is a clash (HotkeyTableTests).
/// </summary>
public sealed class Hotkey
{
    public readonly KeyboardKey[] Keys;
    public readonly KeyMods Mods;
    public readonly HotkeyScope Scope;
    public readonly string Group;
    public readonly string Action;
    public readonly string? Share;
    private readonly string? _label;

    public Hotkey(KeyboardKey[] keys, KeyMods mods, HotkeyScope scope, string group, string action, string? share = null, string? label = null)
    {
        Keys = keys;
        Mods = mods;
        Scope = scope;
        Group = group;
        Action = action;
        Share = share;
        _label = label;
    }

    public Hotkey(KeyboardKey key, KeyMods mods, HotkeyScope scope, string group, string action, string? share = null, string? label = null)
        : this([key], mods, scope, group, action, share, label) { }

    public KeyboardKey Key => Keys[0];

    /// <summary>Text for badges, tooltips and the help overlay, e.g. "S", "Ctrl+K", "Z X C V B N".</summary>
    public string Label => _label ?? Hotkeys.ModsPrefix(Mods) + string.Join(" ", Keys.Select(Hotkeys.KeyName));

    /// <summary>
    /// Pressed this frame with the right modifiers: Ctrl must match exactly; Shift / Alt must be held when
    /// <see cref="Mods"/> has them, otherwise they are free (Shift = queue / add / reverse for most keys).
    /// </summary>
    public bool Pressed() => PressedIndex() >= 0;

    /// <summary>Index in <see cref="Keys"/> of the key pressed this frame (with the right modifiers), or -1.</summary>
    public int PressedIndex()
    {
        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        if (ctrl != ((Mods & KeyMods.Ctrl) != 0)) return -1;
        if ((Mods & KeyMods.Shift) != 0 && !(Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift))) return -1;
        if ((Mods & KeyMods.Alt) != 0 && !(Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt))) return -1;
        for (int i = 0; i < Keys.Length; i++)
            if (Raylib.IsKeyPressed(Keys[i])) return i;
        return -1;
    }
}

/// <summary>A mouse action listed in the help overlay (not a key; never part of the clash check).</summary>
public readonly record struct MouseHelp(string Group, string Input, string Action);

/// <summary>
/// THE hotkey table: every key the game reacts to is listed here once. Input code reads its keys from these entries
/// (<see cref="Hotkey.Pressed"/>, <see cref="Hotkey.Key"/>), the order buttons / tooltips show <see cref="Hotkey.Label"/>,
/// and the F1 help overlay (UI/HelpOverlay) is generated from <see cref="All"/> + <see cref="Mouse"/>.
/// Adding a key: add an entry here (a free key: see the help overlay or <c>HotkeyTableTests</c>, which fails on clashes
/// and on any <c>KeyboardKey</c> polled in the client sources that is missing from this table).
/// Menus and text fields (Enter, Backspace, arrows in lists, Ctrl+V…) are not listed.
/// </summary>
public static class Hotkeys
{
    private const KeyMods N = KeyMods.None, C = KeyMods.Ctrl, S = KeyMods.Shift;
    private const HotkeyScope G = HotkeyScope.Game, Pl = HotkeyScope.Play, U = HotkeyScope.Units;

    // ---------------------------------------------------------------- camera
    public static readonly Hotkey Pan = new([KeyboardKey.Up, KeyboardKey.Down, KeyboardKey.Left, KeyboardKey.Right], N, G, "Camera", "Pan the camera (also screen edge / middle drag)", label: "Arrows");
    public static readonly Hotkey Rotate = new([KeyboardKey.PageUp, KeyboardKey.PageDown], N, G, "Camera", "Rotate the camera (also Alt + middle drag)", label: "PgUp / PgDn");
    public static readonly Hotkey ResetRotation = new(KeyboardKey.Home, N, G, "Camera", "Reset the camera rotation");

    // ---------------------------------------------------------------- selection
    public static readonly Hotkey GroupRecall = new(Digits(), N, G, "Selection", "Recall control group (Shift: add; press twice: center the camera on it)", label: "0-9");
    public static readonly Hotkey GroupSet = new(Digits(), C, G, "Selection", "Set control group to the selection", label: "Ctrl+0-9");
    public static readonly Hotkey IdleEngineer = new(KeyboardKey.Period, N, Pl, "Selection", "Next idle engineer (Shift: all idle engineers)", label: ".");
    public static readonly Hotkey IdleFactory = new(KeyboardKey.Comma, N, Pl, "Selection", "Next idle factory (Shift: all idle factories)", label: ",");

    // ---------------------------------------------------------------- orders
    public static readonly Hotkey Stop = new(KeyboardKey.S, N, U, "Orders", "Stop: clear all orders (also cancels an enhancement)");
    public static readonly Hotkey Move = new(KeyboardKey.M, N, U, "Orders", "Move mode", share: "M");
    public static readonly Hotkey AttackMove = new(KeyboardKey.A, N, U, "Orders", "Attack-move mode (click an enemy: attack it)");
    public static readonly Hotkey Patrol = new(KeyboardKey.P, N, U, "Orders", "Patrol mode (Shift: add points)");
    public static readonly Hotkey Guard = new(KeyboardKey.G, N, U, "Orders", "Guard / assist mode");
    public static readonly Hotkey Launch = new(KeyboardKey.L, N, U, "Orders", "Launch mode (missile launchers: click a ground target)");
    public static readonly Hotkey Teleport = new(KeyboardKey.J, N, U, "Orders", "Teleport mode (commander with Personal Teleporter: click a ground point)");
    public static readonly Hotkey SelfDestruct = new(KeyboardKey.K, C, U, "Orders", "Self-destruct (asks for confirmation)");
    public static readonly Hotkey DeleteWaypoint = new(KeyboardKey.Delete, N, U, "Orders", "Delete the waypoint under the cursor (selected units' queued orders)", label: "Del");

    // ---------------------------------------------------------------- toggles
    public static readonly Hotkey FireState = new(KeyboardKey.Q, N, U, "Toggles", "Fire state: fire at will → return fire → hold fire (Shift: back)");
    public static readonly Hotkey Pause = new(KeyboardKey.H, N, U, "Toggles", "Pause / resume production, construction, upgrades, upkeep");
    public static readonly Hotkey Dive = new(KeyboardKey.D, N, U, "Toggles", "Dive / surface (submarines, Atlantis)", share: "D");
    public static readonly Hotkey Overcharge = new(KeyboardKey.O, N, U, "Toggles", "Overcharge on / off (commander: shots spend stored energy, auto-fire while on)");
    public static readonly Hotkey Capture = new(KeyboardKey.E, N, U, "Toggles", "Capture mode (engineers / commander; with transports selected E = transport to)", share: "E");

    // ---------------------------------------------------------------- transports
    public static readonly Hotkey Unload = new(KeyboardKey.U, N, U, "Transports", "Unload (air carrier: launch); structures selected: upgrade", share: "U");
    public static readonly Hotkey TransportTo = new(KeyboardKey.E, N, U, "Transports", "Transport to (shares E with capture: transports win)", share: "E");
    public static readonly Hotkey Ferry = new(KeyboardKey.F, N, U, "Transports", "Ferry: pickup beacon, then drop point");
    public static readonly Hotkey Dock = new(KeyboardKey.D, N, U, "Transports", "Dock aircraft at the nearest air staging / carrier (shares D with dive)", share: "D");

    // ---------------------------------------------------------------- building
    public static readonly Hotkey BuildSlots = new([KeyboardKey.Z, KeyboardKey.X, KeyboardKey.C, KeyboardKey.V, KeyboardKey.B, KeyboardKey.N], N, HotkeyScope.BuildMenu, "Building", "Pick a structure of the current category");
    public static readonly Hotkey BuildCategory = new(KeyboardKey.Tab, N, HotkeyScope.BuildMenu, "Building", "Next structure category (Shift: previous)");
    public static readonly Hotkey BuildTier = new(KeyboardKey.T, N, HotkeyScope.BuildMenu, "Building", "Next tech tier (Shift: previous)");
    public static readonly Hotkey Rotate90 = new(KeyboardKey.R, N, HotkeyScope.Placement, "Building", "Rotate the structure (placing; Shift+drag lays a line)");
    public static readonly Hotkey Upgrade = new(KeyboardKey.U, N, U, "Building", "Upgrade the selected structures (factory, mex, radar…)", share: "U");

    // ---------------------------------------------------------------- factory
    public static readonly Hotkey FactorySlots = new([KeyboardKey.Z, KeyboardKey.X, KeyboardKey.C, KeyboardKey.V, KeyboardKey.B, KeyboardKey.N], N, HotkeyScope.Factory, "Factory", "Queue a unit (Shift: 5)");
    public static readonly Hotkey FactoryRepeat = new(KeyboardKey.R, N, HotkeyScope.Factory, "Factory", "Repeat build on / off");
    public static readonly Hotkey FactoryTier = new([KeyboardKey.T, KeyboardKey.Tab], N, HotkeyScope.Factory, "Factory", "Next tech tier (Shift: previous)", label: "T / Tab");

    // ---------------------------------------------------------------- game
    public static readonly Hotkey Help = new(KeyboardKey.F1, N, G, "Game", "This help (F1 or Esc closes it)");
    public static readonly Hotkey PauseGame = new(KeyboardKey.Space, N, Pl, "Game", "Pause / resume the game");
    public static readonly Hotkey SpeedUp = new([KeyboardKey.Equal, KeyboardKey.KpAdd], N, Pl, "Game", "Game speed up", label: "+");
    public static readonly Hotkey SpeedDown = new([KeyboardKey.Minus, KeyboardKey.KpSubtract], N, Pl, "Game", "Game speed down", label: "-");
    public static readonly Hotkey Menu = new(KeyboardKey.Escape, N, G, "Game", "Cancel the order mode / placement / help; nothing selected: menu");
    public static readonly Hotkey QuickSave = new(KeyboardKey.F5, N, Pl, "Game", "Quicksave");
    public static readonly Hotkey QuickLoad = new(KeyboardKey.F6, N, Pl, "Game", "Quickload");
    public static readonly Hotkey Diplomacy = new(KeyboardKey.F4, N, Pl, "Game", "Diplomacy: players, teams; give units / resources to allies");
    public static readonly Hotkey Chat = new([KeyboardKey.Enter, KeyboardKey.KpEnter], N, Pl, "Game", "Multiplayer: chat (Shift+Enter: to allies; Enter sends, Esc cancels)", label: "Enter");
    public static readonly Hotkey Fog = new(KeyboardKey.F7, N, G, "Game", "Fog of war on / off (reveal all)");
    public static readonly Hotkey Perf = new(KeyboardKey.F8, N, G, "Game", "Performance overlay");
    public static readonly Hotkey Mute = new(KeyboardKey.M, N, G, "Game", "Mute (nothing selected; Ctrl+M always)", share: "M");
    public static readonly Hotkey MuteCtrl = new(KeyboardKey.M, C, G, "Game", "Mute / unmute the sound");
    public static readonly Hotkey Music = new(KeyboardKey.M, C | S, G, "Game", "Music on / off");

    // ---------------------------------------------------------------- dialog / replay / sandbox / debug
    public static readonly Hotkey DialogConfirm = new([KeyboardKey.Enter, KeyboardKey.KpEnter, KeyboardKey.Y], N, HotkeyScope.Dialog, "Dialogs", "Confirm (self-destruct: Ctrl+K again too)", label: "Enter / Y");
    public static readonly Hotkey DialogCancel = new([KeyboardKey.Escape, KeyboardKey.N], N, HotkeyScope.Dialog, "Dialogs", "Cancel", label: "Esc / N");
    public static readonly Hotkey ReplayPause = new(KeyboardKey.Space, N, HotkeyScope.Replay, "Replay", "Play / pause");
    public static readonly Hotkey ReplaySpeedUp = new([KeyboardKey.Equal, KeyboardKey.KpAdd], N, HotkeyScope.Replay, "Replay", "Faster (up to 16x)", label: "+");
    public static readonly Hotkey ReplaySpeedDown = new([KeyboardKey.Minus, KeyboardKey.KpSubtract], N, HotkeyScope.Replay, "Replay", "Slower (down to 0.5x)", label: "-");
    public static readonly Hotkey SandboxPanel = new(KeyboardKey.F9, N, HotkeyScope.Sandbox, "Sandbox", "Sandbox panel (sandbox, or a game with cheats on)");
    public static readonly Hotkey SandboxConsole = new([KeyboardKey.F10, KeyboardKey.Grave], N, HotkeyScope.Sandbox, "Sandbox", "Sandbox console (sandbox, or a game with cheats on)", label: "F10 / `");
    public static readonly Hotkey DebugSpawnOwn = new(KeyboardKey.F1, C, Pl, "Debug", "Spawn 10 own tanks at the cursor (cheats on)");
    public static readonly Hotkey DebugSpawnEnemy = new(KeyboardKey.F2, C, Pl, "Debug", "Spawn 10 enemy tanks at the cursor (cheats on)");
    public static readonly Hotkey DebugFakeBattle = new(KeyboardKey.F9, N, HotkeyScope.Debug, "Debug", "Fake battle effects (only with --fxtest)");
    public static readonly Hotkey DebugExplosion = new(KeyboardKey.F10, N, HotkeyScope.Debug, "Debug", "Explosion at the cursor (only with --fxtest)");

    /// <summary>Every hotkey, in help order.</summary>
    public static readonly Hotkey[] All =
    [
        Pan, Rotate, ResetRotation,
        GroupRecall, GroupSet, IdleEngineer, IdleFactory,
        Stop, Move, AttackMove, Patrol, Guard, Launch, Teleport, SelfDestruct, DeleteWaypoint,
        FireState, Pause, Dive, Overcharge, Capture,
        Unload, TransportTo, Ferry, Dock,
        BuildSlots, BuildCategory, BuildTier, Rotate90, Upgrade,
        FactorySlots, FactoryRepeat, FactoryTier,
        Help, PauseGame, SpeedUp, SpeedDown, Menu, QuickSave, QuickLoad, Diplomacy, Chat, Fog, Perf, Mute, MuteCtrl, Music,
        DialogConfirm, DialogCancel, ReplayPause, ReplaySpeedUp, ReplaySpeedDown,
        SandboxPanel, SandboxConsole, DebugSpawnOwn, DebugSpawnEnemy, DebugFakeBattle, DebugExplosion,
    ];

    /// <summary>Mouse actions for the help overlay.</summary>
    public static readonly MouseHelp[] Mouse =
    [
        new("Selection", "Click / drag", "Select (box: mobile units first; Shift: add / toggle)"),
        new("Selection", "Double-click", "Every unit of that type on screen (also Ctrl+click; Shift or Ctrl+Shift: add them)"),
        new("Selection", "Unit panel icon", "Click: only that type · Shift / right-click: remove it · Ctrl: that type on screen"),
        new("Selection", "Idle buttons", "Above the minimap: click = next idle engineer / factory, Shift+click = all"),
        new("Orders", "Right click", "Move / attack / assist / reclaim / repair / capture / load (Shift: queue)"),
        new("Orders", "Right click (factory)", "Set the rally point"),
        new("Orders", "Right drag", "Line formation along the drag, facing away from the camera when dragged left to right (Shift: queue; attack-move mode: attack-move)"),
        new("Orders", "Drag a waypoint", "Left-drag a selected unit's waypoint marker to move it (Del over it: delete it)"),
        new("Orders", "Minimap", "Left: camera; in an order mode: give it there · right: move"),
        new("Camera", "Wheel", "Zoom to the cursor (zoomed out: strategic icons)"),
        new("Factory", "Right-click", "On a unit button: remove one from the queue (Shift: 5)"),
    ];

    /// <summary>Help group order.</summary>
    public static readonly string[] Groups = ["Camera", "Selection", "Orders", "Toggles", "Transports", "Building", "Factory", "Game", "Dialogs", "Replay", "Sandbox", "Debug"];

    /// <summary>
    /// Can two scopes be active in the same frame? Dialogs are modal; replays have no orders, building or sandbox;
    /// the build menu and the factory panel are never shown together; factory keys are off while placing.
    /// </summary>
    public static bool Overlaps(HotkeyScope a, HotkeyScope b)
    {
        if (a == b) return true;
        if (a == HotkeyScope.Dialog || b == HotkeyScope.Dialog) return false;
        if (a > b) (a, b) = (b, a);
        return (a, b) switch
        {
            (HotkeyScope.BuildMenu, HotkeyScope.Factory) => false,
            (HotkeyScope.Factory, HotkeyScope.Placement) => false,
            (HotkeyScope.Play or HotkeyScope.Units or HotkeyScope.BuildMenu or HotkeyScope.Factory or HotkeyScope.Placement, HotkeyScope.Replay) => false,
            (HotkeyScope.Replay, HotkeyScope.Sandbox) => false,
            (HotkeyScope.Sandbox, HotkeyScope.Debug) => false,
            _ => true,
        };
    }

    /// <summary>Pairs of entries that clash: same key and modifiers, overlapping scopes, no common Share tag.</summary>
    public static List<(Hotkey A, Hotkey B, KeyboardKey Key)> Clashes(IReadOnlyList<Hotkey> table)
    {
        var res = new List<(Hotkey, Hotkey, KeyboardKey)>();
        for (int i = 0; i < table.Count; i++)
        for (int j = i + 1; j < table.Count; j++)
        {
            var a = table[i];
            var b = table[j];
            if (a.Mods != b.Mods || !Overlaps(a.Scope, b.Scope)) continue;
            if (a.Share != null && a.Share == b.Share) continue;
            foreach (var k in a.Keys)
                if (b.Keys.Contains(k)) { res.Add((a, b, k)); break; }
        }
        return res;
    }

    public static string ModsPrefix(KeyMods m) =>
        ((m & KeyMods.Ctrl) != 0 ? "Ctrl+" : "") + ((m & KeyMods.Shift) != 0 ? "Shift+" : "") + ((m & KeyMods.Alt) != 0 ? "Alt+" : "");

    public static string KeyName(KeyboardKey k) => k switch
    {
        >= KeyboardKey.Zero and <= KeyboardKey.Nine => ((int)(k - KeyboardKey.Zero)).ToString(),
        >= KeyboardKey.A and <= KeyboardKey.Z => ((char)('A' + (k - KeyboardKey.A))).ToString(),
        KeyboardKey.Period => ".",
        KeyboardKey.Comma => ",",
        KeyboardKey.Equal or KeyboardKey.KpAdd => "+",
        KeyboardKey.Minus or KeyboardKey.KpSubtract => "-",
        KeyboardKey.Escape => "Esc",
        KeyboardKey.Grave => "`",
        KeyboardKey.PageUp => "PgUp",
        KeyboardKey.PageDown => "PgDn",
        KeyboardKey.KpEnter => "Enter",
        _ => k.ToString(),
    };

    private static KeyboardKey[] Digits() => Enumerable.Range(0, 10).Select(i => KeyboardKey.Zero + i).ToArray();
}
