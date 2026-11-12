using System.Reflection;
using System.Text.RegularExpressions;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Client.UI;

namespace Rts.Client.Tests;

/// <summary>
/// The hotkey table (Input/Hotkeys.cs) is the single source of keys: no clashes, every public entry listed in
/// <see cref="Hotkeys.All"/>, and every key polled in the game's input code is in the table (so the F1 help overlay is
/// complete). Menus and text fields are not checked (their keys are not game hotkeys).
/// </summary>
[Collection("HotkeyTable")] // (10b-1) KeyBindingTests rebinds the shared table
public class HotkeyTableTests
{
    [Fact]
    public void TableHasNoClashes()
    {
        var clashes = Hotkeys.Clashes(Hotkeys.All);
        Assert.True(clashes.Count == 0,
            "Hotkey clashes: " + string.Join("; ", clashes.Select(c => $"{c.Key}: '{c.A.Action}' vs '{c.B.Action}'")));
    }

    [Fact]
    public void ClashCheckFindsSameKeyInOverlappingScopes()
    {
        var a = new Hotkey(KeyboardKey.J, KeyMods.None, HotkeyScope.Units, "Orders", "a");
        var b = new Hotkey(KeyboardKey.J, KeyMods.None, HotkeyScope.Game, "Game", "b");
        var shared = new Hotkey(KeyboardKey.J, KeyMods.None, HotkeyScope.Units, "Orders", "c", share: "J");
        var shared2 = new Hotkey(KeyboardKey.J, KeyMods.None, HotkeyScope.Units, "Orders", "d", share: "J");
        var ctrl = new Hotkey(KeyboardKey.J, KeyMods.Ctrl, HotkeyScope.Units, "Orders", "e");
        var dialog = new Hotkey(KeyboardKey.J, KeyMods.None, HotkeyScope.Dialog, "Dialogs", "f");
        var replay = new Hotkey(KeyboardKey.J, KeyMods.None, HotkeyScope.Replay, "Replay", "g");

        Assert.Single(Hotkeys.Clashes([a, b]));
        Assert.Empty(Hotkeys.Clashes([shared, shared2]));
        Assert.Empty(Hotkeys.Clashes([a, ctrl]));
        Assert.Empty(Hotkeys.Clashes([a, dialog]));
        Assert.Empty(Hotkeys.Clashes([a, replay]));
        Assert.Single(Hotkeys.Clashes([b, replay])); // Game keys are live in replays too
        // A row of slots clashes on any of its keys.
        var row = new Hotkey([KeyboardKey.H, KeyboardKey.J], KeyMods.None, HotkeyScope.Units, "Orders", "row");
        Assert.Single(Hotkeys.Clashes([row, a]));
    }

    [Fact]
    public void ScopeOverlapIsSymmetric()
    {
        foreach (HotkeyScope x in Enum.GetValues<HotkeyScope>())
        foreach (HotkeyScope y in Enum.GetValues<HotkeyScope>())
            Assert.Equal(Hotkeys.Overlaps(x, y), Hotkeys.Overlaps(y, x));
        Assert.False(Hotkeys.Overlaps(HotkeyScope.BuildMenu, HotkeyScope.Factory));
        Assert.True(Hotkeys.Overlaps(HotkeyScope.Units, HotkeyScope.BuildMenu));
    }

    [Fact]
    public void EveryPublicEntryIsInAllOnceWithAKnownGroup()
    {
        var fields = typeof(Hotkeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Hotkey)).Select(f => (Name: f.Name, Key: (Hotkey)f.GetValue(null)!)).ToList();
        Assert.NotEmpty(fields);
        foreach (var (name, key) in fields)
            Assert.True(Hotkeys.All.Count(k => ReferenceEquals(k, key)) == 1, $"Hotkeys.{name} must be in Hotkeys.All exactly once");
        Assert.Equal(fields.Count, Hotkeys.All.Length);
        foreach (var k in Hotkeys.All)
        {
            Assert.Contains(k.Group, Hotkeys.Groups);
            Assert.False(string.IsNullOrWhiteSpace(k.Action));
            Assert.NotEmpty(k.Keys);
        }
        foreach (var m in Hotkeys.Mouse) Assert.Contains(m.Group, Hotkeys.Groups);
    }

    [Fact]
    public void LabelsShowModifiersAndKeys()
    {
        Assert.Equal("S", Hotkeys.Stop.Label);
        Assert.Equal("Q", Hotkeys.FireState.Label);
        Assert.Equal("Ctrl+K", Hotkeys.SelfDestruct.Label);
        Assert.Equal("Ctrl+Shift+M", Hotkeys.Music.Label);
        Assert.Equal("Z X C V B N", Hotkeys.BuildSlots.Label);
        Assert.Equal("Ctrl+F1", Hotkeys.DebugSpawnOwn.Label);
        Assert.Equal(".", Hotkeys.IdleEngineer.Label);
    }

    private static readonly HashSet<string> Modifiers =
        ["LeftShift", "RightShift", "LeftControl", "RightControl", "LeftAlt", "RightAlt", "Null"];

    /// <summary>Files whose keys are not game hotkeys: menus (except the in-game save UI and the replay viewer),
    /// text fields, generic widgets, and the table itself.</summary>
    private static bool Skipped(string rel)
    {
        rel = rel.Replace('\\', '/');
        if (rel.StartsWith("Menus/")) return !(rel.EndsWith("/InGameSaveUi.cs") || rel.EndsWith("/ReplayScreen.cs"));
        return rel is "Sandbox/SandboxTextField.cs" or "Sandbox/SandboxConsole.cs" or "UI/Ui.cs" or "Input/Hotkeys.cs" or "Input/Hotkeys.Bindings.cs";
    }

    [Fact]
    public void EveryKeyPolledInGameInputIsInTheTable()
    {
        string src = Path.Combine(RepoRoot(), "src", "Rts.Client");
        var inTable = Hotkeys.All.SelectMany(k => k.Keys).Select(k => k.ToString()).ToHashSet();
        var missing = new List<string>();
        int files = 0;
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(src, file);
            if (rel.StartsWith("bin") || rel.StartsWith("obj") || Skipped(rel)) continue;
            files++;
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"KeyboardKey\.(\w+)"))
            {
                string key = m.Groups[1].Value;
                if (Modifiers.Contains(key) || inTable.Contains(key)) continue;
                missing.Add($"{rel}: {key}");
            }
        }
        Assert.True(files > 20, "client sources not found");
        Assert.True(missing.Count == 0, "Keys polled but missing from Input/Hotkeys.cs: " + string.Join(", ", missing.Distinct()));
    }

    [Fact]
    public void HelpOverlayListsKeysAndMouseActions()
    {
        var play = HelpOverlay.Content(replay: false, sandbox: false);
        Assert.NotEmpty(play);
        var lines = play.SelectMany(g => g.Lines).ToList();
        Assert.Contains(lines, l => l.Keys == "Q" && l.Action.StartsWith("Fire state"));
        Assert.Contains(lines, l => l.Mouse);
        Assert.DoesNotContain(play, g => g.Group is "Replay" or "Sandbox");
        Assert.Contains(play, g => g.Group == "Debug");

        var replay = HelpOverlay.Content(replay: true, sandbox: false);
        Assert.DoesNotContain(replay, g => g.Group is "Orders" or "Building" or "Factory");
        Assert.Contains(replay, g => g.Group == "Replay");
        Assert.DoesNotContain(replay.SelectMany(g => g.Lines), l => l.Keys == Hotkeys.QuickSave.Label);

        var sandbox = HelpOverlay.Content(replay: false, sandbox: true);
        Assert.Contains(sandbox, g => g.Group == "Sandbox");
        Assert.DoesNotContain(sandbox.SelectMany(g => g.Lines), l => l.Action == Hotkeys.DebugExplosion.Action);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Rts.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
