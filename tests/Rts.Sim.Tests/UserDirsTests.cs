using Rts.Sim.Persistence;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Dev vs release user folders (<see cref="UserDirs.Resolve"/>; docs/DEV_GUIDE.md "Packaging").</summary>
public class UserDirsTests
{
    private const string Home = "/home/u";
    private const string Exe = "/opt/rts";

    private static Func<string, string?> Env(params (string Key, string? Value)[] vars) =>
        k => vars.FirstOrDefault(v => v.Key == k).Value;

    private static Func<string, string?> Marker(string? text) =>
        p => p == Path.Combine(Exe, UserDirs.ReleaseMarker) ? text : null;

    [Fact]
    public void DevModeKeepsRelativeFoldersAndXdgDefaults()
    {
        var l = UserDirs.Resolve(Exe, Env(), windows: false, Home, Marker(null));
        Assert.False(l.Release);
        Assert.Null(l.Version);
        Assert.Equal("saves", l.SavesDir);
        Assert.Equal("replays", l.ReplaysDir);
        Assert.Equal("screenshots", l.ScreenshotsDir);
        Assert.Equal("/home/u/.config/RtsGame", l.ConfigDir);
        Assert.Equal("/home/u/.local/share/RtsGame", l.UserDataDir);
    }

    [Fact]
    public void ReleaseMarkerMovesFilesToTheUserDataFolder()
    {
        var l = UserDirs.Resolve(Exe, Env(), windows: false, Home, Marker("0.7.0+4cee893\n"));
        Assert.True(l.Release);
        Assert.Equal("0.7.0+4cee893", l.Version);
        Assert.Equal("/home/u/.local/share/RtsGame/saves", l.SavesDir);
        Assert.Equal("/home/u/.local/share/RtsGame/replays", l.ReplaysDir);
        Assert.Equal("/home/u/.local/share/RtsGame/screenshots", l.ScreenshotsDir);
        Assert.Equal("/home/u/.config/RtsGame", l.ConfigDir);
    }

    [Fact]
    public void XdgVariablesAreHonoredAndEmptyOnesIgnored()
    {
        var set = UserDirs.Resolve(Exe, Env(("XDG_DATA_HOME", "/d"), ("XDG_CONFIG_HOME", "/c")), false, Home, Marker(""));
        Assert.True(set.Release);
        Assert.Equal("", set.Version);
        Assert.Equal("/d/RtsGame/saves", set.SavesDir);
        Assert.Equal("/c/RtsGame", set.ConfigDir);
        Assert.Equal("/d/RtsGame", set.UserDataDir);

        var empty = UserDirs.Resolve(Exe, Env(("XDG_DATA_HOME", ""), ("XDG_CONFIG_HOME", " ")), false, Home, Marker("v"));
        Assert.Equal("/home/u/.local/share/RtsGame/saves", empty.SavesDir);
        Assert.Equal("/home/u/.config/RtsGame", empty.ConfigDir);
    }

    [Fact]
    public void WindowsUsesAppData()
    {
        var env = Env(("APPDATA", @"C:\Users\u\AppData\Roaming"), ("XDG_DATA_HOME", "/ignored"));
        var rel = UserDirs.Resolve(Exe, env, windows: true, @"C:\Users\u", Marker("0.7.0"));
        string app = Path.Combine(@"C:\Users\u\AppData\Roaming", "RtsGame");
        Assert.Equal(app, rel.ConfigDir);
        Assert.Equal(app, rel.UserDataDir);
        Assert.Equal(Path.Combine(app, "saves"), rel.SavesDir);

        var dev = UserDirs.Resolve(Exe, env, windows: true, @"C:\Users\u", Marker(null));
        Assert.Equal("saves", dev.SavesDir);
        Assert.Equal(app, dev.ConfigDir);
    }

    [Fact]
    public void UserDirOverrideAppliesInBothModes()
    {
        var l = UserDirs.Resolve(Exe, Env(("RTS_USER_DIR", "/tmp/rts")), false, Home, Marker(null));
        Assert.False(l.Release);
        Assert.Equal("/tmp/rts", l.UserDataDir);
        Assert.Equal("/tmp/rts/saves", l.SavesDir);
        Assert.Equal("/tmp/rts/screenshots", l.ScreenshotsDir);
        Assert.Equal("/home/u/.config/RtsGame", l.ConfigDir);
    }

    [Fact]
    public void ReadsTheRealMarkerFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rts_userdirs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.False(UserDirs.Resolve(dir, Env(), false, Home).Release);
            File.WriteAllText(Path.Combine(dir, UserDirs.ReleaseMarker), "\n0.7.0+abc\n");
            var l = UserDirs.Resolve(dir, Env(), false, Home);
            Assert.True(l.Release);
            Assert.Equal("0.7.0+abc", l.Version);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TestRunsAreDevMode()
    {
        // No release.txt next to the test binaries: tests keep the relative folders (they never call Apply).
        Assert.False(UserDirs.ResolveForProcess().Release);
        Assert.Equal("saves", SaveStore.SavesDir);
    }
}
