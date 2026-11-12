using Raylib_cs;
using Rts.Client.Input;
using Rts.Client.Menus;

namespace Rts.Client.Tests;

/// <summary>(10b-1) Rebinding: ids, the override layer, clash refuse / swap, reset, the settings string, a fake keyboard.</summary>
[Collection("HotkeyTable")]
public class KeyBindingTests
{
    private sealed class FakeKeys : IKeyState
    {
        public readonly HashSet<KeyboardKey> Held = [], Hit = [];
        public bool Down(KeyboardKey k) => Held.Contains(k) || Hit.Contains(k);
        public bool Pressed(KeyboardKey k) => Hit.Contains(k);
    }

    /// <summary>Runs <paramref name="body"/> and always restores the defaults and raylib input.</summary>
    private static void Clean(Action body)
    {
        try { body(); }
        finally
        {
            Hotkeys.ResetAll();
            Hotkeys.Input = RaylibKeyState.Instance;
        }
    }

    [Fact]
    public void EveryEntryHasAUniqueIdFoundById()
    {
        var ids = Hotkeys.All.Select(h => h.Id).ToList();
        Assert.DoesNotContain("", ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (var h in Hotkeys.All) Assert.Same(h, Hotkeys.ById(h.Id));
        Assert.Equal("Stop", Hotkeys.Stop.Id);
        Assert.Null(Hotkeys.ById("NoSuchKey"));
        Assert.All(Hotkeys.All, h => Assert.True(h.IsDefault));
    }

    [Fact]
    public void ReboundKeyFiresAndTheOldKeyNoLonger() => Clean(() =>
    {
        var keys = new FakeKeys();
        Hotkeys.Input = keys;
        keys.Hit.Add(KeyboardKey.S);
        Assert.True(Hotkeys.Stop.Pressed());
        Assert.Equal(BindResult.Ok, Hotkeys.TryBind(Hotkeys.Stop, [KeyboardKey.F11], KeyMods.Shift, out _));
        Assert.False(Hotkeys.Stop.Pressed());
        Assert.Equal("Shift+F11", Hotkeys.Stop.Label);
        keys.Hit.Clear();
        keys.Hit.Add(KeyboardKey.F11);
        Assert.False(Hotkeys.Stop.Pressed()); // Shift not held
        keys.Held.Add(KeyboardKey.LeftShift);
        Assert.True(Hotkeys.Stop.Pressed());
        // A row rebinds as a whole: slot index follows the new keys.
        Assert.Equal(BindResult.Ok, Hotkeys.TryBind(Hotkeys.BuildSlots, [KeyboardKey.Q, KeyboardKey.W, KeyboardKey.E, KeyboardKey.R, KeyboardKey.T, KeyboardKey.Y], KeyMods.Alt, out _));
        keys.Held.Clear();
        keys.Hit.Clear();
        keys.Held.Add(KeyboardKey.LeftAlt);
        keys.Hit.Add(KeyboardKey.E);
        Assert.Equal(2, Hotkeys.BuildSlots.PressedIndex());
        Assert.Equal("Alt+Q W E R T Y", Hotkeys.BuildSlots.Label);
        Assert.False(Hotkeys.ValidBinding(Hotkeys.BuildSlots, [KeyboardKey.Q])); // a row needs every slot
        Assert.False(Hotkeys.ValidBinding(Hotkeys.Stop, [KeyboardKey.LeftShift]));
    });

    [Fact]
    public void ClashIsRefusedAndSwapExchangesTheKeys() => Clean(() =>
    {
        // A = attack-move (Units scope): binding Stop to A clashes.
        Assert.Equal(BindResult.Clash, Hotkeys.TryBind(Hotkeys.Stop, [KeyboardKey.A], KeyMods.None, out var clashes));
        Assert.Contains(Hotkeys.AttackMove, clashes);
        Assert.True(Hotkeys.Stop.IsDefault);
        Assert.True(Hotkeys.TrySwap(Hotkeys.Stop, [KeyboardKey.A], KeyMods.None, out _));
        Assert.Equal("A", Hotkeys.Stop.Label);
        Assert.Equal("S", Hotkeys.AttackMove.Label);
        Assert.Empty(Hotkeys.Clashes(Hotkeys.All));
        // Reset one refuses while the other entry holds its default; reset all always works.
        Assert.Equal(BindResult.Clash, Hotkeys.Reset(Hotkeys.Stop, out _));
        Assert.True(Hotkeys.TrySwap(Hotkeys.Stop, Hotkeys.Stop.DefaultKeys, Hotkeys.Stop.DefaultMods, out _));
        Assert.True(Hotkeys.Stop.IsDefault && Hotkeys.AttackMove.IsDefault);
        // A row cannot swap with a single key; fixed entries never rebind.
        Assert.False(Hotkeys.TrySwap(Hotkeys.Ferry, [KeyboardKey.Z], KeyMods.None, out var why));
        Assert.NotNull(why);
        Assert.True(Hotkeys.Ferry.IsDefault && Hotkeys.BuildSlots.IsDefault);
        Assert.Equal(BindResult.Fixed, Hotkeys.TryBind(Hotkeys.Menu, [KeyboardKey.F12], KeyMods.None, out _));
        // Shared keys: D stays shared by dive and dock (no clash) while both are on it.
        Assert.True(Hotkeys.SameBinding(Hotkeys.Dive, Hotkeys.Dock));
        Assert.Equal(BindResult.Ok, Hotkeys.TryBind(Hotkeys.Dive, [KeyboardKey.F11], KeyMods.None, out _));
        Assert.False(Hotkeys.SameBinding(Hotkeys.Dive, Hotkeys.Dock));
        Assert.Equal(BindResult.Ok, Hotkeys.TryBind(Hotkeys.Dock, [KeyboardKey.F11], KeyMods.None, out _));
    });

    [Fact]
    public void BindingsRoundTripThroughTheSettingsFile() => Clean(() =>
    {
        Assert.Equal("", Hotkeys.ExportBindings());
        Hotkeys.TryBind(Hotkeys.Stop, [KeyboardKey.F11], KeyMods.Ctrl, out _);
        Hotkeys.TrySwap(Hotkeys.Patrol, [KeyboardKey.G], KeyMods.None, out _); // Patrol <-> Guard
        Hotkeys.TryBind(Hotkeys.FactorySlots, [KeyboardKey.Q, KeyboardKey.W, KeyboardKey.E, KeyboardKey.R, KeyboardKey.T, KeyboardKey.Y], KeyMods.Alt, out _);
        string saved = Hotkeys.ExportBindings();
        Assert.StartsWith("v1;", saved);
        string path = Path.Combine(Path.GetTempPath(), $"rts_keys_{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings { KeyBindings = saved }.SaveTo(path);
            Hotkeys.ResetAll();
            Assert.Equal(4, Hotkeys.LoadBindings(AppSettings.LoadFrom(path).KeyBindings));
        }
        finally { File.Delete(path); }
        Assert.Equal("Ctrl+F11", Hotkeys.Stop.Label);
        Assert.Equal("G", Hotkeys.Patrol.Label);
        Assert.Equal("P", Hotkeys.Guard.Label); // swapped pair loads although each alone would clash
        Assert.Equal("Alt+Q W E R T Y", Hotkeys.FactorySlots.Label);
        Assert.Equal(saved, Hotkeys.ExportBindings());
    });

    [Fact]
    public void CorruptOrUnknownEntriesAreIgnored() => Clean(() =>
    {
        int n = Hotkeys.ApplyBindings("v9; NoSuchId=F11; Stop=NotAKey; Ferry=Ctrl+; Menu=F12; BuildSlots=Q; garbage; Pause=Hyper+X;"
            + " Fog=F11; Perf=F11; Guard=F12; Launch=65", out var skipped);
        Assert.Equal(2, n); // Fog=F11 and Guard=F12; Perf=F11 clashes with Fog and goes back to its default
        Assert.Equal("F11", Hotkeys.Fog.Label);
        Assert.Equal("F12", Hotkeys.Guard.Label);
        Assert.True(Hotkeys.Perf.IsDefault && Hotkeys.Stop.IsDefault && Hotkeys.Menu.IsDefault && Hotkeys.BuildSlots.IsDefault && Hotkeys.Launch.IsDefault);
        Assert.Contains("NoSuchId=F11", skipped);
        Assert.Empty(Hotkeys.Clashes(Hotkeys.All));
        Hotkeys.LoadBindings("not a binding string at all ;;; = = =");
        Assert.All(Hotkeys.All, h => Assert.True(h.IsDefault));
        Assert.Equal(0, Hotkeys.LoadBindings(null));
    });

    [Fact]
    public void SpecParsesNamesDigitsAndModifiers()
    {
        Assert.True(Hotkeys.TryParseSpec("ctrl+shift+z", out var k, out var m));
        Assert.Equal([KeyboardKey.Z], k);
        Assert.Equal(KeyMods.Ctrl | KeyMods.Shift, m);
        Assert.True(Hotkeys.TryParseSpec("Alt+1 Two F3", out k, out m));
        Assert.Equal([KeyboardKey.One, KeyboardKey.Two, KeyboardKey.F3], k);
        Assert.Equal(KeyMods.Alt, m);
        Assert.False(Hotkeys.TryParseSpec("Escape", out _, out _));
        Assert.False(Hotkeys.TryParseSpec("", out _, out _));
        Assert.Equal("Ctrl+Shift+Z", Hotkeys.Spec([KeyboardKey.Z], KeyMods.Ctrl | KeyMods.Shift));
    }
}
