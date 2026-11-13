using Rts.Client.Audio;
using Rts.Client.UI;
using Xunit;

namespace Rts.Client.Tests;

/// <summary>(11b-1) Voice announcer: the line table, the generated files, the queue rules, failure safety.</summary>
public class VoiceTests
{
    private static VoiceLine L(string id, int priority, double cooldown = 0, double maxWait = 4) =>
        new(id, priority, cooldown, maxWait, new Dictionary<string, string> { ["it"] = id });

    private static VoiceQueue Q(params VoiceLine[] lines)
    {
        var d = lines.ToDictionary(l => l.Id);
        return new VoiceQueue(id => d.GetValueOrDefault(id)) { Gap = 0.3 };
    }

    [Fact]
    public void TableMatchesTheIdsTheGameRaises()
    {
        var t = VoiceLineTable.TryLoad(VoiceLineTable.DefaultDir());
        Assert.NotNull(t);
        var ids = t!.Lines.Select(l => l.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(VoiceIds.All.OrderBy(x => x), ids.OrderBy(x => x));
        Assert.Contains(VoiceLineTable.DefaultLanguage, t.Languages);
        foreach (var l in t.Lines)
        {
            foreach (var lang in t.Languages) Assert.False(string.IsNullOrWhiteSpace(l.Text.GetValueOrDefault(lang)), $"{l.Id}: no {lang} text");
            Assert.True(l.Cooldown >= 0 && l.MaxWait > 0, l.Id);
        }
        Assert.Equal("Comandante sotto attacco.", t[VoiceIds.CommanderUnderAttack]!.Text["it"]);
        Assert.Equal("Lancio nucleare rilevato.", t[VoiceIds.NukeLaunchDetected]!.Text["it"]);
    }

    [Fact]
    public void EveryLineHasAValidFile()
    {
        string dir = VoiceLineTable.DefaultDir();
        var t = VoiceLineTable.TryLoad(dir)!;
        long total = 0;
        foreach (var lang in t.Languages)
            foreach (var l in t.Lines)
            {
                string path = VoiceLineTable.FilePath(dir, lang, l.Id);
                var w = VoiceWav.TryRead(path);
                Assert.True(w != null, $"missing or unreadable: {path}");
                Assert.Equal(VoiceLineTable.SampleRate, w!.SampleRate);
                Assert.Equal(1, w.Channels);
                Assert.InRange(w.Duration, 0.6, 4.0);
                Assert.True(w.Rms() > 0.02, $"{l.Id}: too quiet (RMS {w.Rms():F3})");
                Assert.True(w.Peak() < 0.999, $"{l.Id}: clipped");
                total += new FileInfo(path).Length;
            }
        Assert.True(total < 2_500_000, $"voice files {total} bytes");
    }

    [Fact]
    public void NeverTalksOverItself()
    {
        var q = Q(L("a", 1), L("b", 1));
        Assert.True(q.Enqueue("a", 0));
        Assert.True(q.Enqueue("b", 0));
        var first = q.Next(0)!;
        Assert.Equal("a", first.Id);
        q.Started(first, 0, 1.5);
        Assert.True(q.Busy(1.0));
        Assert.Null(q.Next(1.0));
        Assert.Null(q.Next(1.7)); // the gap after the line
        Assert.Equal("b", q.Next(1.9)!.Id);
    }

    [Fact]
    public void HigherPriorityCutsInFrontButNeverInterrupts()
    {
        var q = Q(L("low", 1), L("mid", 5), L("high", 9));
        q.Enqueue("mid", 0);
        var m = q.Next(0)!;
        q.Started(m, 0, 2);
        q.Enqueue("low", 0.5);
        q.Enqueue("high", 1.0);
        Assert.Equal("mid", q.Speaking!.Id);
        Assert.Null(q.Next(1.5)); // "high" waits for the line being spoken
        var h = q.Next(2.4)!;
        Assert.Equal("high", h.Id);
        q.Started(h, 2.4, 1);
        Assert.Equal("low", q.Next(3.8)!.Id); // waited 3.3 s, still fresh
    }

    [Fact]
    public void StaleLinesAreDropped()
    {
        var q = Q(L("long", 5), L("news", 1, maxWait: 3));
        q.Enqueue("long", 0);
        q.Started(q.Next(0)!, 0, 5);
        q.Enqueue("news", 0.5);
        Assert.Single(q.WaitingLines);
        Assert.Null(q.Next(5.5)); // waited 5 s > 3 s
        Assert.Empty(q.WaitingLines);
    }

    [Fact]
    public void EachLineHasItsCooldown()
    {
        var q = Q(L("a", 1, cooldown: 10), L("b", 1, cooldown: 10));
        Assert.True(q.Enqueue("a", 0));
        Assert.False(q.Enqueue("a", 0.1)); // already waiting and cooling down
        q.Started(q.Next(0)!, 0, 1);
        Assert.False(q.Enqueue("a", 5));
        Assert.True(q.Enqueue("b", 5)); // another line is independent
        Assert.True(q.Enqueue("a", 10.5));
        Assert.False(q.Enqueue("unknown", 0));
    }

    [Fact]
    public void FullQueuePushesOutTheLeastImportant()
    {
        var q = Q(L("p1", 1), L("p2", 2), L("p3", 3), L("p4", 4), L("p5", 5), L("p0", 0));
        q.MaxWaiting = 4;
        foreach (var id in new[] { "p1", "p2", "p3", "p4" }) Assert.True(q.Enqueue(id, 0));
        Assert.False(q.Enqueue("p0", 0)); // less important than everything waiting
        Assert.True(q.Enqueue("p5", 0));
        Assert.DoesNotContain(q.WaitingLines, w => w.Line.Id == "p1");
        Assert.Equal("p5", q.Next(0)!.Id);
    }

    [Fact]
    public void MissingFilesAndTablesAreSilentNotFatal()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rts-voice-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "it"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "lines.json"),
                "{ \"languages\": [\"it\"], \"lines\": [ { \"id\": \"a\", \"priority\": 1, \"cooldown\": 0, \"text\": { \"it\": \"uno\" } },"
                + " { \"id\": \"b\", \"priority\": 1, \"cooldown\": 0, \"text\": { \"it\": \"due\" } } ] }");
            File.WriteAllBytes(Path.Combine(dir, "it", "b.wav"), [1, 2, 3, 4, 5]); // broken file
            var settings = new AudioSettings();
            using var v = new VoiceAnnouncer(settings, device: false, voiceDir: dir, loadNow: true);
            var started = new List<(string, double)>();
            v.LineStarted += (l, d) => started.Add((l.Id, d));
            Assert.True(v.Say("a", 0));
            v.Update(0);
            Assert.True(v.Say("b", 0.1));
            v.Update(0.1); // no file: duration 0, the next line is not held back
            Assert.Equal([("a", 0.0), ("b", 0.0)], started);
            Assert.Equal(0, v.LoadedCount);

            settings.VoiceOn = false;
            Assert.False(v.Say("a", 50));

            using var none = new VoiceAnnouncer(new AudioSettings(), device: false, voiceDir: Path.Combine(dir, "nope"), loadNow: true);
            Assert.Null(none.Table);
            Assert.False(none.Say("a", 0));
            none.Update(0);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void WavReaderRejectsGarbage()
    {
        Assert.Null(VoiceWav.Parse([]));
        Assert.Null(VoiceWav.Parse("RIFF\0\0\0\0WAVEjunk"u8));
        Assert.Null(VoiceWav.TryRead(Path.Combine(Path.GetTempPath(), "no-such-voice.wav")));
    }

    [Fact]
    public void AlertsMapToTheirLines()
    {
        Assert.Equal(VoiceIds.BaseUnderAttack, GameAudio.VoiceFor(AlertKind.UnderAttack, AlertMonitor.StructuresUnderAttackText));
        Assert.Equal(VoiceIds.UnitsUnderAttack, GameAudio.VoiceFor(AlertKind.UnderAttack, "Units under attack"));
        Assert.Equal(VoiceIds.CommanderUnderAttack, GameAudio.VoiceFor(AlertKind.CommanderUnderAttack, "Commander under attack!"));
        Assert.Equal(VoiceIds.StructureLost, GameAudio.VoiceFor(AlertKind.StructureLost, "Structure lost: Wall ×3"));
        Assert.Null(GameAudio.VoiceFor(AlertKind.ConstructionDone, "Construction complete: Mass Extractor"));
        Assert.Null(GameAudio.VoiceFor(AlertKind.Ping, ""));
    }
}
