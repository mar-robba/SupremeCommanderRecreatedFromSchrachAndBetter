using System.Numerics;
using Rts.Client.Input;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;

namespace Rts.Client.UI;

/// <summary>
/// <c>--tech-demo</c>: screenshot scene for the T2/T3 tech (via <see cref="DebugSpawnCommand"/>, like the sandbox):
/// rows of T2/T3 structures and units near the local start, plus a T1 land factory (with queued units and two T3
/// engineers assisting) and a T1 mass extractor that start upgrading in place. Selects the upgrading factory and,
/// unless --camera is given, points the camera at the scene.
/// </summary>
public sealed class TechDemo
{
    private static readonly string[] BigStructures =
        ["uef_t2_land_factory", "uef_t3_land_factory", "uef_t2_air_factory", "uef_t3_air_factory"];
    private static readonly string[] SmallStructures =
    [
        "uef_t2_power_generator", "uef_t3_power_generator", "uef_t2_mass_fabricator", "uef_t3_mass_fabricator",
        "uef_t2_point_defense", "uef_t3_point_defense", "uef_t2_aa_flak", "uef_t3_sam", "uef_t2_radar", "uef_t3_omni_sensor",
    ];
    private static readonly string[] LandUnits =
    [
        "uef_t2_engineer", "uef_t2_heavy_tank", "uef_t2_amphibious_tank", "uef_t2_mobile_missile", "uef_t2_mobile_flak",
        "uef_t3_engineer", "uef_t3_assault_bot", "uef_t3_heavy_bot", "uef_t3_mobile_artillery",
    ];
    private static readonly string[] AirUnits =
        ["uef_t2_gunship", "uef_t2_fighter_bomber", "uef_t3_air_superiority", "uef_t3_strategic_bomber", "uef_t3_spy_plane"];

    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    private readonly bool _setCamera;
    private readonly Vector2 _focus;
    private int _frames;
    private bool _done;

    public TechDemo(GameContext ctx, SelectionController sel, ClientOptions o)
    {
        _ctx = ctx;
        _sel = sel;
        _setCamera = o.CameraXZD == null;
        var t = ctx.Terrain;
        int army = ctx.LocalArmy;
        var sp = t.StartPositions[army % t.StartPositions.Length];
        var center = new Vector2(t.Width * 0.5f, t.Height * 0.5f);
        // Axis-aligned rows (they read as screen rows with the default camera), growing towards the map center.
        var dir = new Vector2(0, center.Y >= sp.Y ? 1 : -1);
        var right = new Vector2(1, 0);
        sp.X = Math.Clamp(sp.X, 40, t.Width - 40);
        void Spawn(string id, Vector2 p)
        {
            int bp = ctx.Db.IndexOf(id);
            if (bp >= 0) ctx.Runner.Enqueue(new DebugSpawnCommand(army, bp, t.Clamp(p, 6), 1));
        }
        // The T1 land factory that gets upgraded, two T3 engineers next to it (they assist once it upgrades).
        var origin = sp + dir * 14;
        Spawn("uef_t1_land_factory", origin - right * 22);
        Spawn("uef_t3_engineer", origin - right * 22 + dir * 9 - right * 4);
        Spawn("uef_t3_engineer", origin - right * 22 + dir * 9 + right * 4);
        for (int i = 0; i < BigStructures.Length; i++) Spawn(BigStructures[i], origin + right * (-6 + i * 15));
        var row2 = origin + dir * 16;
        for (int i = 0; i < SmallStructures.Length; i++) Spawn(SmallStructures[i], row2 + right * (-28 + i * 7));
        var row3 = origin + dir * 26;
        for (int i = 0; i < LandUnits.Length; i++) Spawn(LandUnits[i], row3 + right * (-24 + i * 6));
        var row4 = origin + dir * 35;
        for (int i = 0; i < AirUnits.Length; i++) Spawn(AirUnits[i], row4 + right * (-16 + i * 8));
        _focus = origin + dir * 16;
        // A T1 mass extractor on the nearest mass deposit (upgraded to T2 below).
        Rts.Sim.Map.Deposit? best = null;
        foreach (var d in t.Deposits)
            if (d.Type == Rts.Sim.Map.DepositType.Mass && (best == null || Vector2.Distance(d.Pos, sp) < Vector2.Distance(best.Value.Pos, sp))) best = d;
        if (best is { } dep) Spawn("uef_t1_mass_extractor", dep.Pos);
        Spawn("uef_t3_power_generator", origin - right * 22 - dir * 14); // keeps the mass fabricators + upgrades out of energy stall
        Console.WriteLine($"tech-demo: factories row at {origin.X:0},{origin.Y:0} · structures {row2.X:0},{row2.Y:0} · land units {row3.X:0},{row3.Y:0} · air {row4.X:0},{row4.Y:0}");
    }

    /// <summary>Called once per frame before input handling (from <see cref="UiStartup"/>-style Hud hook).</summary>
    public void Update()
    {
        _frames++;
        if (_frames == 1 && _setCamera) _ctx.Camera.LookAt(_focus.X, _focus.Y, 75);
        if (_done || _frames > 600) return;
        var snap = _ctx.Snapshots.Current;
        int lf = _ctx.Db.IndexOf("uef_t1_land_factory"), mex = _ctx.Db.IndexOf("uef_t1_mass_extractor"), eng = _ctx.Db.IndexOf("uef_t3_engineer");
        UnitHandle factory = UnitHandle.None;
        var mexes = new List<UnitHandle>();
        var engineers = new List<UnitHandle>();
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var v = ref snap.Units[k];
            if (v.Army != _ctx.LocalArmy) continue;
            if (v.Bp == lf && factory.IsNone) factory = new UnitHandle(v.Index, v.Gen);
            else if (v.Bp == mex) mexes.Add(new UnitHandle(v.Index, v.Gen));
            else if (v.Bp == eng) engineers.Add(new UnitHandle(v.Index, v.Gen));
        }
        if (factory.IsNone || engineers.Count < 2) return;
        var f = new[] { factory };
        void Q(string id, int n) { int b = _ctx.Db.IndexOf(id); if (b >= 0) _ctx.Runner.Enqueue(new FactoryEnqueueCommand(_ctx.LocalArmy, f, b, n)); }
        Q("uef_t1_tank", 2);
        Q("uef_t1_artillery", 1);
        _ctx.Runner.Enqueue(new UpgradeCommand(_ctx.LocalArmy, f));
        _ctx.Runner.Enqueue(new IssueOrderCommand(_ctx.LocalArmy, engineers.ToArray(), Order.GuardUnit(factory), false));
        if (mexes.Count > 0) _ctx.Runner.Enqueue(new UpgradeCommand(_ctx.LocalArmy, mexes.ToArray()));
        _sel.SelectOnly(f);
        _done = true;
    }
}
