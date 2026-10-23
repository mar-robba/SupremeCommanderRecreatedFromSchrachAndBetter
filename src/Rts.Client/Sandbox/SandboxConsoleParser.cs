using System.Globalization;
using System.Numerics;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Client.Sandbox;

/// <summary>
/// Pure parser for the sandbox console (no raylib, no sim access): one text line -> one <see cref="ConsoleAction"/>
/// or a short error. Case-insensitive, invariant-culture numbers, whitespace-separated tokens ("double quotes" make
/// one token). Armies are 1-based player numbers like the setup screen: me, enemy, enemyN/aiN (N-th other army),
/// armyN/pN or a bare N where an army is expected, all (toggles and ai only).
/// </summary>
public static partial class SandboxConsoleParser
{
    public const float DefaultDamage = 0.5f;
    public const float DefaultClearRadius = 20f;
    public const float MinClearRadius = 1f, MaxClearRadius = 500f;
    public const float MinSpeed = 0.125f, MaxSpeed = 8f;

    private delegate ParseResult Handler(Cmd cmd, List<string> args, ConsoleContext ctx);

    private sealed class Cmd(string name, string[] aliases, string usage, string description, string[] keywords, Handler parse)
    {
        public readonly string Name = name;
        public readonly string[] Aliases = aliases;
        /// <summary>Syntax, e.g. "speed &lt;x&gt;".</summary>
        public readonly string Usage = usage;
        public readonly string Help = usage + "  " + description;
        /// <summary>Words Tab can complete after the command name.</summary>
        public readonly string[] Keywords = keywords;
        public readonly Handler Parse = parse;
    }

    private static readonly string[] OnOff = ["on", "off"];
    private static readonly string[] ToggleWords = ["on", "off", "all", "me", "enemy"];

    private static readonly Cmd[] Commands =
    [
        new("spawn", ["s", "sp"], "spawn <unit> [count] [army] [at x,z] [site]", "spawn units",
            ["site", "at", "me", "enemy"], ParseSpawn),
        new("nuke", [], "nuke [instant] [x,z]", "nuke missile (instant: blast, no missile)", ["instant", "at"], ParseNuke),
        new("money", ["infinite", "res"], "money [army|all] [on|off]", "infinite resources",
            ToggleWords, (c, a, x) => ParseToggle(c, a, x, SandboxToggle.InfiniteResources)),
        new("instabuild", ["instant"], "instabuild [army|all] [on|off]", "instant construction",
            ToggleWords, (c, a, x) => ParseToggle(c, a, x, SandboxToggle.InstantBuild)),
        new("buildanywhere", ["anywhere"], "buildanywhere [army|all] [on|off]", "extractors without deposits",
            ToggleWords, (c, a, x) => ParseToggle(c, a, x, SandboxToggle.BuildAnywhere)),
        new("fog", [], "fog [on|off]", "fog of war", OnOff, ParseFog),
        new("reveal", [], "reveal", "reveal the map (fog off)", OnOff, ParseReveal),
        new("ai", [], "ai <army|all> on|off [easy|normal|hard]", "AI control of an army",
            ["on", "off", "easy", "normal", "hard", "all", "enemy"], ParseAi),
        new("kill", [], "kill [radius] [at x,z]", "destroy the selected units, or every unit in an area (default radius 5)",
            ["at"], ParseKill),
        new("overcharge", ["oc"], "overcharge [army|all] [on|off]", "arm / disarm the commanders' Overcharge (default on)",
            ToggleWords, ParseOvercharge),
        new("delete", ["del", "remove"], "delete", "remove the selected units (no wreck)",
            [], (c, a, x) => NoArgs(c, a, new UnitActionAction(SandboxUnitAction.Delete))),
        new("heal", [], "heal", "fully heal the selected units",
            [], (c, a, x) => NoArgs(c, a, new UnitActionAction(SandboxUnitAction.Heal))),
        new("damage", ["dmg"], "damage [pct]", "damage the selected units (default 50%)", [], ParseDamage),
        new("give", ["owner"], "give <army>", "give the selected units to an army", ["me", "enemy"], ParseGive),
        new("complete", ["finish"], "complete", "finish the selected construction sites",
            [], (c, a, x) => NoArgs(c, a, new UnitActionAction(SandboxUnitAction.Complete))),
        new("clear", [], "clear [radius] [at x,z]", "remove all units in an area (default radius 20)", ["at"], ParseClear),
        new("speed", [], "speed <x>", "game speed (0.125..8)", [], ParseSpeed),
        new("pause", [], "pause [on|off]", "pause the game", OnOff, (c, a, x) => ParseOptionalOnOff(c, a, on => new PauseAction(on))),
        new("victory", [], "victory on|off", "victory/defeat end of the game", OnOff, ParseVictory),
        new("camera", ["cam"], "camera x,z [dist]", "move the camera", [], ParseCamera),
        new("wait", [], "wait <frames>", "pause a script for some frames", [], ParseWait),
        new("help", ["?", "h"], "help", "list commands (armies: me, enemy, enemy2, p2, all)", [], (c, a, x) => ParseResult.Ok(new HelpAction())),
        new("panel", [], "panel [on|off]", "sandbox panel", OnOff, (c, a, x) => ParseOptionalOnOff(c, a, on => new PanelAction(on))),
        new("console", [], "console [on|off]", "show / hide this console", OnOff, (c, a, x) => ParseOptionalOnOff(c, a, on => new ShowConsoleAction(on))),
        new("cls", [], "cls", "clear the console log", [], (c, a, x) => NoArgs(c, a, new ClearLogAction())),
    ];

    /// <summary>Name/alias -> command (lookups only, never iterated).</summary>
    private static readonly Dictionary<string, Cmd> Lookup = BuildLookup();

    private static Dictionary<string, Cmd> BuildLookup()
    {
        var d = new Dictionary<string, Cmd>(StringComparer.Ordinal);
        foreach (var c in Commands)
        {
            d.Add(c.Name, c);
            foreach (var a in c.Aliases) d.Add(a, c);
        }
        return d;
    }

    public static IReadOnlyList<string> HelpLines { get; } = Commands.Select(c => c.Help).ToArray();
    public static IReadOnlyList<string> CommandNames { get; } = Commands.Select(c => c.Name).ToArray();

    // ------------------------------------------------------------------ parsing

    public static ParseResult Parse(string line, ConsoleContext ctx)
    {
        if (string.IsNullOrWhiteSpace(line)) return default;
        string s = line.Trim();
        if (s.StartsWith('#') || s.StartsWith("//", StringComparison.Ordinal)) return default; // script comment
        var toks = Tokenize(s);
        if (toks.Count == 0) return default;
        if (!Lookup.TryGetValue(toks[0].Text.ToLowerInvariant(), out var cmd))
            return ParseResult.Fail($"unknown command '{toks[0].Text}' - type help");
        var args = new List<string>(toks.Count - 1);
        for (int i = 1; i < toks.Count; i++) args.Add(toks[i].Text.ToLowerInvariant());
        return cmd.Parse(cmd, args, ctx);
    }

    /// <summary>Splits a script on ';' and newlines; trimmed, empty parts dropped.</summary>
    public static List<string> SplitScript(string script)
    {
        var res = new List<string>();
        if (string.IsNullOrEmpty(script)) return res;
        foreach (var part in script.Split([';', '\n', '\r']))
        {
            string p = part.Trim();
            if (p.Length > 0) res.Add(p);
        }
        return res;
    }

    private static ParseResult Fail(string e) => ParseResult.Fail(e);
    private static ParseResult Unexpected(Cmd c, string token) => Fail($"{c.Name}: unexpected '{token}' - {c.Usage}");

    private static ParseResult ParseSpawn(Cmd c, List<string> a, ConsoleContext ctx)
    {
        if (a.Count == 0) return Fail("spawn: missing unit (e.g. spawn tank 10)");
        int unitAt = 0;
        long? count = null;
        // "spawn 10 tank" is accepted too.
        if (a.Count > 1 && TryCount(a[0], out long first) && !TryCount(a[1], out _)) { count = first; unitAt = 1; }
        var bp = BlueprintMatcher.Find(ctx.Db, a[unitAt]);
        if (bp == null) return Fail($"unknown unit '{a[unitAt]}'");

        int army = ctx.LocalArmy;
        bool armySet = false, site = false;
        Vector2? at = null;
        for (int i = 0; i < a.Count;)
        {
            if (i == unitAt || (unitAt == 1 && i == 0)) { i++; continue; }
            string t = a[i];
            if (t is "site" or "sites")
            {
                site = true;
                i++;
            }
            else if (t == "at")
            {
                i++;
                if (!TryPos(a, ref i, true, out var p)) return Fail("spawn: 'at' needs a position (at x,z)");
                at = p;
            }
            else if (t.Contains(','))
            {
                if (!TryPos(a, ref i, false, out var p)) return Fail($"spawn: bad position '{t}' (use x,z)");
                at = p;
            }
            else if (TryCount(t, out long n))
            {
                if (count != null) return Fail($"spawn: count given twice ('{t}'); for an army use p{t}");
                count = n;
                i++;
            }
            else
            {
                var k = ParseArmy(t, ctx, false, out int ar, out string? err);
                if (k == ArmyParse.Error) return Fail("spawn: " + err);
                if (k == ArmyParse.NotArmy) return Unexpected(c, t);
                if (armySet) return Fail($"spawn: army given twice ('{t}')");
                army = ar;
                armySet = true;
                i++;
            }
        }
        int cnt = (int)Math.Clamp(count ?? 1, 1, SandboxSystem.MaxSpawnCount);
        return ParseResult.Ok(new SpawnAction(bp.Index, cnt, army, at, site));
    }

    /// <summary>Words that turn <c>nuke</c> into the instant blast (no missile).</summary>
    private static readonly string[] InstantWords = ["instant", "now", "blast", "-i"];

    private static ParseResult ParseNuke(Cmd c, List<string> a, ConsoleContext ctx)
    {
        bool instant = false;
        if (a.Count > 0 && Array.IndexOf(InstantWords, a[0]) >= 0) { instant = true; a = a.GetRange(1, a.Count - 1); }
        else if (a.Count > 0 && Array.IndexOf(InstantWords, a[^1]) >= 0) { instant = true; a = a.GetRange(0, a.Count - 1); }
        if (a.Count == 0) return ParseResult.Ok(new NukeAction(null, instant));
        int i = a[0] == "at" ? 1 : 0;
        if (!TryPos(a, ref i, true, out var p)) return Fail("nuke: expected [instant] x,z (or nothing to click a target)");
        if (i < a.Count) return Unexpected(c, a[i]);
        return ParseResult.Ok(new NukeAction(p, instant));
    }

    private static ParseResult ParseToggle(Cmd c, List<string> a, ConsoleContext ctx, SandboxToggle toggle)
    {
        int army = ctx.LocalArmy;
        bool armySet = false;
        bool? on = null;
        for (int i = 0; i < a.Count; i++)
        {
            string t = a[i];
            if (TryOnOffWord(t, out bool v))
            {
                if (on != null) return Unexpected(c, t);
                on = v;
                continue;
            }
            // A trailing 0/1 is on/off ("money 1"); elsewhere a bare number is an army ("money 1 off").
            if (t is "0" or "1" && on == null && i == a.Count - 1)
            {
                on = t == "1";
                continue;
            }
            var k = ParseArmy(t, ctx, true, out int ar, out string? err);
            if (k == ArmyParse.Error) return Fail($"{c.Name}: {err}");
            if (k == ArmyParse.NotArmy || armySet) return Unexpected(c, t);
            army = ar;
            armySet = true;
        }
        return ParseResult.Ok(new ToggleAction(toggle, army, on));
    }

    private static ParseResult ParseFog(Cmd c, List<string> a, ConsoleContext ctx)
        => ParseOptionalOnOff(c, a, on => new FogAction(on));

    private static ParseResult ParseReveal(Cmd c, List<string> a, ConsoleContext ctx)
        => ParseOptionalOnOff(c, a, on => new FogAction(on == null ? false : !on.Value));

    private static ParseResult ParseAi(Cmd c, List<string> a, ConsoleContext ctx)
    {
        int? army = null;
        bool? on = null;
        AiDifficulty? diff = null;
        foreach (string t in a)
        {
            if (TryDifficulty(t, out var d))
            {
                if (diff != null) return Unexpected(c, t);
                diff = d;
            }
            else if (TryOnOffWord(t, out bool v))
            {
                if (on != null) return Unexpected(c, t);
                on = v;
            }
            else if (t is "0" or "1" && army != null && on == null)
            {
                on = t == "1";
            }
            else
            {
                var k = ParseArmy(t, ctx, true, out int ar, out string? err);
                if (k == ArmyParse.Error) return Fail("ai: " + err);
                if (k == ArmyParse.NotArmy || army != null) return Unexpected(c, t);
                army = ar;
            }
        }
        if (army == null) return Fail("ai: which army? (e.g. ai 2 on, ai enemy hard, ai all off)");
        if (on == null && diff == null) return Fail("ai: expected on, off or easy|normal|hard");
        return ParseResult.Ok(new AiAction(army.Value, on ?? true, diff));
    }

    private static ParseResult ParseDamage(Cmd c, List<string> a, ConsoleContext ctx)
    {
        float amount = DefaultDamage;
        if (a.Count > 1) return Unexpected(c, a[1]);
        if (a.Count == 1 && !TryFraction(a[0], out amount))
            return Fail("damage: expected a percentage (e.g. damage 50, damage 50%, damage 0.5)");
        return ParseResult.Ok(new UnitActionAction(SandboxUnitAction.Damage, amount));
    }

    private static ParseResult ParseGive(Cmd c, List<string> a, ConsoleContext ctx)
    {
        if (a.Count == 0) return Fail("give: which army? (e.g. give enemy, give 2)");
        if (a.Count > 1) return Unexpected(c, a[1]);
        if (ParseArmy(a[0], ctx, false, out int army, out string? err) != ArmyParse.Ok) return Fail("give: " + err);
        return ParseResult.Ok(new UnitActionAction(SandboxUnitAction.ChangeOwner, 0, army));
    }

    private static ParseResult ParseClear(Cmd c, List<string> a, ConsoleContext ctx)
    {
        if (ParseArea(c, a, out float? radius, out Vector2? at) is { Error: not null } err) return err;
        return ParseResult.Ok(new ClearAction(radius ?? DefaultClearRadius, at));
    }

    /// <summary>"[radius] [at x,z]" (also a bare x,z) for clear / kill; returns a failed result on bad input.</summary>
    private static ParseResult? ParseArea(Cmd c, List<string> a, out float? radius, out Vector2? at)
    {
        radius = null;
        at = null;
        for (int i = 0; i < a.Count;)
        {
            string t = a[i];
            if (t == "at")
            {
                i++;
                if (!TryPos(a, ref i, true, out var p)) return Fail($"{c.Name}: 'at' needs a position (at x,z)");
                at = p;
            }
            else if (t.Contains(','))
            {
                if (!TryPos(a, ref i, false, out var p)) return Fail($"{c.Name}: bad position '{t}' (use x,z)");
                at = p;
            }
            else if (radius == null && TryNum(t, out float r))
            {
                radius = Math.Clamp(r, MinClearRadius, MaxClearRadius);
                i++;
            }
            else return Unexpected(c, t);
        }
        return null;
    }

    private static ParseResult ParseSpeed(Cmd c, List<string> a, ConsoleContext ctx)
    {
        if (a.Count == 0) return Fail("speed: expected a number (e.g. speed 2, speed 0.5)");
        if (a.Count > 1) return Unexpected(c, a[1]);
        string t = a[0].Trim('x');
        if (!TryNum(t, out float v)) return Fail("speed: expected a number (e.g. speed 2, speed 0.5)");
        if (v <= 0) return Fail("speed: expected a number > 0 (use pause to stop)");
        return ParseResult.Ok(new SpeedAction(Math.Clamp(v, MinSpeed, MaxSpeed)));
    }

    private static ParseResult ParseVictory(Cmd c, List<string> a, ConsoleContext ctx)
    {
        if (a.Count == 0 || !TryOnOff(a[0], out bool on)) return Fail("victory: expected on or off");
        if (a.Count > 1) return Unexpected(c, a[1]);
        return ParseResult.Ok(new VictoryAction(on));
    }

    private static ParseResult ParseCamera(Cmd c, List<string> a, ConsoleContext ctx)
    {
        int i = 0;
        // "x,z,dist" in one token (same format as --camera).
        if (a.Count == 1 && a[0].Split(',') is { Length: 3 } xyz && TryNum(xyz[0], out float cx) && TryNum(xyz[1], out float cz)
            && TryNum(xyz[2], out float cd) && cd > 0)
            return ParseResult.Ok(new CameraAction(new Vector2(cx, cz), cd));
        if (!TryPos(a, ref i, true, out var p)) return Fail("camera: expected x,z [distance] (e.g. camera 128,128 90)");
        float? dist = null;
        if (i < a.Count)
        {
            if (!TryNum(a[i], out float d) || d <= 0) return Fail($"camera: bad distance '{a[i]}'");
            dist = d;
            i++;
        }
        if (i < a.Count) return Unexpected(c, a[i]);
        return ParseResult.Ok(new CameraAction(p, dist));
    }

    private static ParseResult ParseWait(Cmd c, List<string> a, ConsoleContext ctx)
    {
        if (a.Count != 1 || !int.TryParse(a[0], NumberStyles.None, CultureInfo.InvariantCulture, out int frames))
            return Fail("wait: expected a number of frames (e.g. wait 30)");
        return ParseResult.Ok(new WaitAction(frames));
    }

    private static ParseResult NoArgs(Cmd c, List<string> a, ConsoleAction action)
        => a.Count == 0 ? ParseResult.Ok(action) : Unexpected(c, a[0]);

    private static ParseResult ParseOptionalOnOff(Cmd c, List<string> a, Func<bool?, ConsoleAction> make)
    {
        if (a.Count == 0) return ParseResult.Ok(make(null));
        if (!TryOnOff(a[0], out bool on)) return Fail($"{c.Name}: expected on or off");
        if (a.Count > 1) return Unexpected(c, a[1]);
        return ParseResult.Ok(make(on));
    }

    // ------------------------------------------------------------------ armies

    private enum ArmyParse { NotArmy, Ok, Error }

    /// <summary>enemyN / aiN = N-th army other than the local one.</summary>
    private static readonly string[] OtherArmyPrefixes = ["enemy", "ai"];

    /// <summary>Parses an army token (me, enemy, enemyN, aiN, armyN, pN, a bare N). "all" is not accepted here
    /// (commands that allow it check it themselves). On failure error says why.</summary>
    public static bool TryParseArmy(string token, ConsoleContext ctx, out int army, out string? error)
    {
        string t = (token ?? "").Trim().ToLowerInvariant();
        return ParseArmy(t, ctx, false, out army, out error) == ArmyParse.Ok;
    }

    private static ArmyParse ParseArmy(string t, ConsoleContext ctx, bool allowAll, out int army, out string? error)
    {
        army = -1;
        error = null;
        switch (t)
        {
            case "me" or "self" or "my" or "mine" or "player" or "local":
                army = ctx.LocalArmy;
                return ArmyParse.Ok;
            case "all" or "everyone":
                if (allowAll) return ArmyParse.Ok;
                error = $"'{t}' can't be used here, pick one army";
                return ArmyParse.Error;
            case "enemy":
                for (int i = 0; i < ctx.ArmyCount; i++)
                {
                    if (i == ctx.LocalArmy || SameTeam(ctx, i, ctx.LocalArmy)) continue;
                    army = i;
                    return ArmyParse.Ok;
                }
                error = "no enemy army";
                return ArmyParse.Error;
        }

        foreach (string prefix in OtherArmyPrefixes)
        {
            if (!PrefixNum(t, prefix, out int n)) continue;
            int others = 0;
            for (int i = 0; i < ctx.ArmyCount; i++)
            {
                if (i == ctx.LocalArmy) continue;
                if (++others == n)
                {
                    army = i;
                    return ArmyParse.Ok;
                }
            }
            error = $"{prefix}{n} doesn't exist ({others} other {(others == 1 ? "army" : "armies")})";
            return ArmyParse.Error;
        }

        int num;
        if (PrefixNum(t, "player", out num) || PrefixNum(t, "army", out num) || PrefixNum(t, "p", out num)
            || (t.Length > 0 && int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out num)))
        {
            if (num >= 1 && num <= ctx.ArmyCount)
            {
                army = num - 1;
                return ArmyParse.Ok;
            }
            error = $"army {num} doesn't exist ({ctx.ArmyCount} {(ctx.ArmyCount == 1 ? "army" : "armies")})";
            return ArmyParse.Error;
        }

        error = $"'{t}' is not an army (me, enemy, enemy2, p2...)";
        return ArmyParse.NotArmy;
    }

    private static bool SameTeam(ConsoleContext ctx, int a, int b)
    {
        var teams = ctx.Teams;
        if (teams == null || a < 0 || b < 0 || a >= teams.Length || b >= teams.Length) return false;
        return teams[a] == teams[b];
    }

    private static bool PrefixNum(string t, string prefix, out int n)
    {
        n = 0;
        return t.Length > prefix.Length && t.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(t.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out n);
    }

    // ------------------------------------------------------------------ small token parsers

    private static bool TryNum(string s, out float v)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && float.IsFinite(v);

    /// <summary>Integer count, also "10x".</summary>
    private static bool TryCount(string s, out long n)
    {
        if (s.Length > 1 && s[^1] == 'x') s = s[..^1];
        return long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n);
    }

    /// <summary>"50%" / "50" -> 0.5, "0.5" -> 0.5 (values up to 1 are fractions). Clamped to 0..1.</summary>
    private static bool TryFraction(string s, out float f)
    {
        f = 0;
        bool pct = s.EndsWith('%');
        if (pct) s = s[..^1];
        if (!TryNum(s, out float v) || v < 0) return false;
        if (pct || v > 1) v /= 100f;
        f = Math.Clamp(v, 0f, 1f);
        return true;
    }

    private static bool TryOnOffWord(string t, out bool on)
    {
        switch (t)
        {
            case "on" or "true" or "yes": on = true; return true;
            case "off" or "false" or "no": on = false; return true;
            default: on = false; return false;
        }
    }

    private static bool TryOnOff(string t, out bool on)
    {
        if (TryOnOffWord(t, out on)) return true;
        if (t == "1") { on = true; return true; }
        if (t == "0") { on = false; return true; }
        return false;
    }

    private static bool TryDifficulty(string t, out AiDifficulty d)
    {
        switch (t)
        {
            case "easy": d = AiDifficulty.Easy; return true;
            case "normal" or "medium": d = AiDifficulty.Normal; return true;
            case "hard": d = AiDifficulty.Hard; return true;
            default: d = default; return false;
        }
    }

    /// <summary>Position at a[i]: "x,z" (also "x," "z" and "(x,z)") or, when allowSpaced, "x z". Advances i.</summary>
    private static bool TryPos(List<string> a, ref int i, bool allowSpaced, out Vector2 p)
    {
        p = default;
        if (i >= a.Count) return false;
        string t = a[i].Trim('(', ')');
        int comma = t.IndexOf(',');
        float x, z;
        if (comma >= 0)
        {
            string xs = t[..comma], zs = t[(comma + 1)..];
            if (zs.Length == 0)
            {
                if (i + 1 >= a.Count || !TryNum(xs, out x) || !TryNum(a[i + 1].Trim('(', ')'), out z)) return false;
                p = new Vector2(x, z);
                i += 2;
                return true;
            }
            if (!TryNum(xs, out x) || !TryNum(zs, out z)) return false;
            p = new Vector2(x, z);
            i++;
            return true;
        }
        if (allowSpaced && i + 1 < a.Count && TryNum(t, out x) && TryNum(a[i + 1].Trim('(', ')'), out z))
        {
            p = new Vector2(x, z);
            i += 2;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ tokenizer

    private readonly record struct Tok(string Text, int Start);

    /// <summary>Whitespace-separated tokens; a "double-quoted string" is one token (unterminated = to the end).
    /// Start is the index of the token (of its opening quote) in the line.</summary>
    private static List<Tok> Tokenize(string line)
    {
        var list = new List<Tok>();
        int i = 0, n = line.Length;
        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(line[i])) i++;
            if (i >= n) break;
            int start = i;
            if (line[i] == '"')
            {
                int s = ++i;
                while (i < n && line[i] != '"') i++;
                list.Add(new Tok(line[s..i], start));
                if (i < n) i++;
            }
            else
            {
                while (i < n && !char.IsWhiteSpace(line[i])) i++;
                list.Add(new Tok(line[start..i], start));
            }
        }
        return list;
    }

    // ------------------------------------------------------------------ completion / hints

    /// <summary>Tab completion: returns the new full input, or null if there is nothing to complete.</summary>
    public static string? Complete(string input, ConsoleContext ctx)
    {
        if (string.IsNullOrEmpty(input) || char.IsWhiteSpace(input[^1])) return null;
        var toks = Tokenize(input);
        if (toks.Count == 0) return null;
        var last = toks[^1];
        string prefix = input[..last.Start];
        string partial = last.Text.ToLowerInvariant();

        if (toks.Count == 1)
        {
            if (partial.Length == 0) return null;
            var names = new List<string>();
            foreach (var c in Commands)
                if (c.Name.StartsWith(partial, StringComparison.Ordinal)) names.Add(c.Name);
            if (names.Count == 1) return prefix + names[0] + " ";
            if (names.Count > 1)
            {
                string lcp = CommonPrefix(names);
                return lcp.Length > partial.Length ? prefix + lcp : null;
            }
            return Lookup.ContainsKey(partial) ? prefix + last.Text + " " : null; // alias like "res", "?"
        }

        if (!Lookup.TryGetValue(toks[0].Text.ToLowerInvariant(), out var cmd)) return null;
        if (cmd.Name == "spawn" && toks.Count == 2)
        {
            var cands = BlueprintMatcher.Candidates(ctx.Db, last.Text, 1);
            return cands.Count == 0 ? null : prefix + cands[0].Id + " ";
        }
        if (partial.Length == 0) return null;
        var words = new List<string>();
        foreach (var k in cmd.Keywords)
            if (k.StartsWith(partial, StringComparison.Ordinal)) words.Add(k);
        if (words.Count == 1) return prefix + words[0] + " ";
        if (words.Count > 1)
        {
            string lcp = CommonPrefix(words);
            if (lcp.Length > partial.Length) return prefix + lcp;
        }
        return null;
    }

    /// <summary>Hint lines for what is being typed: matching commands, unit candidates for spawn, or the
    /// help line of the command.</summary>
    public static List<string> Suggestions(string input, ConsoleContext ctx, int max = 6)
    {
        var res = new List<string>();
        if (string.IsNullOrWhiteSpace(input) || max <= 0) return res;
        var toks = Tokenize(input);
        if (toks.Count == 0) return res;
        bool typing = !char.IsWhiteSpace(input[^1]);
        string first = toks[0].Text.ToLowerInvariant();

        if (toks.Count == 1 && typing)
        {
            foreach (var c in Commands)
            {
                if (res.Count >= max) break;
                if (c.Name.StartsWith(first, StringComparison.Ordinal)
                    || Array.Exists(c.Aliases, al => al.StartsWith(first, StringComparison.Ordinal)))
                    res.Add(c.Help);
            }
            if (res.Count == 0) res.Add($"unknown command '{toks[0].Text}' - type help");
            return res;
        }

        if (!Lookup.TryGetValue(first, out var cmd))
        {
            res.Add($"unknown command '{toks[0].Text}' - type help");
            return res;
        }
        if (cmd.Name == "spawn" && toks.Count >= 2)
        {
            if (toks.Count == 2 && typing)
            {
                foreach (var bp in BlueprintMatcher.Candidates(ctx.Db, toks[1].Text, max))
                    res.Add($"{bp.Id}  {bp.Name} (T{bp.Tech})");
                if (res.Count == 0) res.Add($"no unit matches '{toks[1].Text}'");
                return res;
            }
            res.Add(cmd.Help);
            var unit = BlueprintMatcher.Find(ctx.Db, toks[1].Text);
            if (res.Count < max)
                res.Add(unit != null ? $"unit: {unit.Id}  {unit.Name} (T{unit.Tech})" : $"unknown unit '{toks[1].Text}'");
            return res;
        }
        res.Add(cmd.Help);
        return res;
    }

    private static string CommonPrefix(List<string> words)
    {
        string p = words[0];
        for (int i = 1; i < words.Count && p.Length > 0; i++)
        {
            int k = 0;
            while (k < p.Length && k < words[i].Length && p[k] == words[i][k]) k++;
            p = p[..k];
        }
        return p;
    }
}
