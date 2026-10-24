using Xunit;

namespace Rts.Client.Tests;

/// <summary>Crash log append/trim and the version label (pure parts of CrashLog / AppVersion / AppIcon).</summary>
public class CrashLogTests
{
    [Fact]
    public void AppendCreatesTheFolderAndKeepsNewestEntriesWhenTrimming()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rts_crashlog_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "sub", "crash.log");
        try
        {
            for (int i = 0; i < 40; i++)
                CrashLog.Append(path, $"{CrashLog.EntryHeader}entry {i:D2} =====\n" + new string('x', 200) + "\n\n", 2000);
            string text = File.ReadAllText(path);
            Assert.True(text.Length <= 2000 + 300, "trimmed to about the limit: " + text.Length);
            Assert.StartsWith(CrashLog.EntryHeader, text);          // cut at an entry boundary
            Assert.Contains("entry 39", text);                       // newest kept
            Assert.DoesNotContain("entry 00", text);                 // oldest dropped
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TrimFallsBackToARawCutWithoutHeaders()
    {
        string text = new string('a', 100) + new string('b', 100);
        Assert.Equal(text, CrashLog.Trim(text, 500));
        string cut = CrashLog.Trim(text, 100);
        Assert.Equal(75, cut.Length);
        Assert.All(cut, ch => Assert.Equal('b', ch));
    }

    [Fact]
    public void EntryHasTimeVersionAndStackTrace()
    {
        Exception e;
        try { throw new InvalidOperationException("boom"); }
        catch (Exception ex) { e = ex; }
        string entry = CrashLog.FormatEntry(new DateTime(2026, 10, 24, 13, 5, 9), "v0.7.0 (abc1234)", "main thread", true, e);
        Assert.StartsWith("===== 2026-10-24 13:05:09 v0.7.0 (abc1234) (main thread) =====\n", entry);
        Assert.Contains("InvalidOperationException: boom", entry);
        Assert.Contains(nameof(EntryHasTimeVersionAndStackTrace), entry);
        Assert.Contains("non-fatal", CrashLog.FormatEntry(DateTime.Now, "v", "task", false, e));
    }

    [Fact]
    public void BadCommandLineArgumentsAreNotCrashes()
    {
        Exception? parse = Record(() => ClientOptions.Parse(["--no-such-flag"]));
        Assert.IsType<ArgumentException>(parse);
        Assert.True(CrashLog.IsArgumentError(parse!));
        Exception? format = Record(() => ClientOptions.Parse(["--seed", "abc"]));
        Assert.True(CrashLog.IsArgumentError(format!));
        Assert.False(CrashLog.IsArgumentError(Record(() => throw new ArgumentException("elsewhere"))!));
    }

    private static Exception? Record(Action a)
    {
        try { a(); return null; }
        catch (Exception e) { return e; }
    }

    [Theory]
    [InlineData("0.7.0+3fd01cf9f6bd5117fae3b6b25383f38815b99a21", false, "v0.7.0 (3fd01cf) dev")]
    [InlineData("0.7.0+4cee893-dirty", true, "v0.7.0 (4cee893-dirty)")]
    [InlineData("0.7.0", true, "v0.7.0")]
    public void VersionLabel(string informational, bool release, string expected) =>
        Assert.Equal(expected, AppVersion.Format(informational, release));

    [Fact]
    public void ClientVersionComesFromTheProject()
    {
        Assert.StartsWith("0.7.0", AppVersion.Informational);
        Assert.EndsWith(" dev", AppVersion.Label); // tests run without the release marker
    }

    [Fact]
    public void IconIsOpaqueInsideAndTransparentInTheCorners()
    {
        Assert.Equal(0, AppIcon.Pixel(0, 0, AppIcon.Size).A);
        Assert.Equal(255, AppIcon.Pixel(AppIcon.Size / 2, AppIcon.Size - 30, AppIcon.Size).A);
        var chevron = AppIcon.Pixel(AppIcon.Size / 2, (int)(AppIcon.Size * 0.36f), AppIcon.Size);
        Assert.True(chevron.R > 200 && chevron.G > 150, $"gold chevron at the apex: {chevron.R},{chevron.G},{chevron.B}");
    }
}
