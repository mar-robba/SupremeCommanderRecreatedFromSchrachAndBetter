using System.Diagnostics;
using System.Globalization;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Persistence;

/// <summary>
/// (8b-2) <c>mapexport</c>: writes generated maps as map files (<see cref="MapFile"/>), checked against the
/// generator's guarantees (<see cref="MapChecks"/>); a map that fails is refused (no file, exit 1).
/// <code>
/// mapexport &lt;out.rtsmap&gt; &lt;mapSize&gt; &lt;seed&gt; &lt;players&gt; [preset] [name] [--water X] [--exact] [--desc TEXT]
/// mapexport --pack &lt;pack.txt&gt; &lt;outDir&gt;      (one map per line: id size seed players preset "Name" ["Description"])
/// mapexport --scan &lt;mapSize&gt; &lt;players&gt; &lt;preset&gt; &lt;firstSeed&gt; &lt;count&gt;   (which seeds pass, file sizes)
/// </code>
/// Heights are rounded to 1/256 (<see cref="MapFile.Quantize"/>, ~4x smaller files) unless <c>--exact</c>; the checks
/// run on the terrain read back from the encoded bytes, which must equal the written one bit for bit.
/// </summary>
static class MapExport
{
    public const int HeightBits = 8;

    public static int Run(string[] args)
    {
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        if (args.Length >= 3 && args[0] == "--pack") return Pack(db, args[1], args[2]);
        if (args.Length >= 6 && args[0] == "--scan") return Scan(db, args[1..]);
        if (args.Length < 4)
        {
            Console.WriteLine("usage: mapexport <out.rtsmap> <mapSize> <seed> <players> [preset] [name] [--water X] [--exact] [--desc TEXT]\n" +
                              "       mapexport --pack <pack.txt> <outDir>\n       mapexport --scan <mapSize> <players> <preset> <firstSeed> <count>");
            return 2;
        }
        var pos = new List<string>();
        float water = -1;
        bool exact = false;
        string desc = "";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--water") water = float.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--exact") exact = true;
            else if (args[i] == "--desc") desc = args[++i];
            else pos.Add(args[i]);
        }
        var preset = MapPreset.Plains;
        if (pos.Count > 4 && !MapPresets.TryParse(pos[4], out preset)) { Console.WriteLine($"unknown preset '{pos[4]}'"); return 2; }
        var s = new MapGenSettings { Size = int.Parse(pos[1]), Seed = int.Parse(pos[2]), Players = int.Parse(pos[3]), Preset = preset, WaterAmount = water };
        string name = pos.Count > 5 ? pos[5] : $"{MapPresets.Name(preset)} {s.Size} #{s.Seed}";
        return Export(db, pos[0], s, name, desc, exact) ? 0 : 1;
    }

    /// <summary>Generates, encodes, reads back, checks, writes. False (and no file) when a guarantee fails.</summary>
    public static bool Export(BlueprintDb db, string outPath, MapGenSettings s, string name, string desc, bool exact, bool quiet = false)
    {
        var sw = Stopwatch.StartNew();
        var (bytes, info, errors) = Build(db, s, name, desc, exact);
        if (errors.Count > 0)
        {
            Console.WriteLine($"REFUSED {outPath}: {MapPresets.Name(s.Preset)} {s.Size} seed {s.Seed} {s.Players}p fails {errors.Count} check(s):");
            foreach (var e in errors.Take(8)) Console.WriteLine("  - " + e);
            return false;
        }
        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllBytes(outPath, bytes);
        if (!quiet)
            Console.WriteLine($"wrote {outPath}: \"{info.Name}\" {info.Width} {info.Players}p {MapPresets.Name(s.Preset)} seed {s.Seed}, " +
                              $"{bytes.Length / 1024.0:0} KB, hash {info.HashHex}, {sw.ElapsedMilliseconds} ms");
        return true;
    }

    public static (byte[] bytes, MapFileInfo info, List<string> errors) Build(BlueprintDb db, MapGenSettings s, string name, string desc, bool exact)
    {
        var generated = MapGenerator.Generate(s);
        var t = exact ? generated : MapFile.Quantize(generated, HeightBits);
        var info = new MapFileInfo
        {
            Name = name, Description = desc, HeightBits = exact ? 0 : HeightBits,
            Generated = true, Preset = s.Preset, Seed = s.Seed, WaterAmount = s.WaterAmount,
        };
        var bytes = MapFile.Encode(t, info);
        var back = MapFile.Decode(bytes).Terrain;
        var errors = new List<string>();
        if (!SaveGame.TerrainBytes(back).AsSpan().SequenceEqual(SaveGame.TerrainBytes(t))) errors.Add("round trip is not exact (encoder bug)");
        errors.AddRange(MapChecks.Check(back, db, naval: MapPresets.NeedsNaval(s.Preset)));
        return (bytes, info, errors);
    }

    private static int Pack(BlueprintDb db, string packFile, string outDir)
    {
        int ok = 0, bad = 0;
        long total = 0;
        foreach (var raw in File.ReadAllLines(packFile))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var f = SplitQuoted(line);
            if (f.Count < 6 || !MapPresets.TryParse(f[4], out var preset)) { Console.WriteLine($"bad line: {line}"); bad++; continue; }
            var s = new MapGenSettings { Size = int.Parse(f[1]), Seed = int.Parse(f[2]), Players = int.Parse(f[3]), Preset = preset };
            string path = Path.Combine(outDir, f[0] + MapFile.Extension);
            if (Export(db, path, s, f[5], f.Count > 6 ? f[6] : "", exact: false)) { ok++; total += new FileInfo(path).Length; }
            else bad++;
        }
        Console.WriteLine($"pack: {ok} maps written, {bad} refused, total {total / 1024.0:0} KB");
        return bad == 0 ? 0 : 1;
    }

    private static int Scan(BlueprintDb db, string[] a)
    {
        int size = int.Parse(a[0]), players = int.Parse(a[1]);
        if (!MapPresets.TryParse(a[2], out var preset)) { Console.WriteLine($"unknown preset '{a[2]}'"); return 2; }
        int first = int.Parse(a[3]), count = int.Parse(a[4]);
        for (int seed = first; seed < first + count; seed++)
        {
            var s = new MapGenSettings { Size = size, Seed = seed, Players = players, Preset = preset };
            var (bytes, _, errors) = Build(db, s, "scan", "", exact: false);
            var t = MapFile.Decode(bytes).Terrain;
            float landShare = 0;
            for (int i = 0; i < t.Pass.Length; i++) if ((t.Pass[i] & PassFlags.Land) != 0) landShare++;
            landShare /= t.Pass.Length;
            Console.WriteLine($"seed {seed}: {(errors.Count == 0 ? "ok" : $"FAIL ({errors[0]})")}, {bytes.Length / 1024} KB, " +
                              $"land {landShare:P0}, deposits {t.Deposits.Length}, props {t.Props.Length}");
        }
        return 0;
    }

    private static List<string> SplitQuoted(string line)
    {
        var result = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i >= line.Length) break;
            if (line[i] == '"')
            {
                int end = line.IndexOf('"', i + 1);
                if (end < 0) end = line.Length;
                result.Add(line[(i + 1)..end]);
                i = end + 1;
            }
            else
            {
                int end = i;
                while (end < line.Length && !char.IsWhiteSpace(line[end])) end++;
                result.Add(line[i..end]);
                i = end;
            }
        }
        return result;
    }
}
