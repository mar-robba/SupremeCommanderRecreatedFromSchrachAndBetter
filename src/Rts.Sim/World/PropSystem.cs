using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Systems;

namespace Rts.Sim.World;

/// <summary>
/// Trees react to the battle (wave 8b). <see cref="Run"/> (after MovementSystem): land units moving over a standing
/// tree knock it over (parallel scan over the AliveList that only READS props → <see cref="ChunkedBuffer{T}"/> merged
/// in chunk order), burning trees burn out (Burnt: worth less) and may ignite their neighbours.
/// <see cref="OnDamage"/> (start of DamageSystem, before the events are consumed): area damage ignites trees, big
/// blasts and nukes also knock them over away from the center (a nuke's inner ring is vaporized by
/// <c>MissileSystem.VaporizeCraters</c> one tick later). Trees never block movement. All state changes sequential.
/// </summary>
public static class PropSystem
{
    /// <summary>A unit knocks over trees within its radius × this (+ the tree radius).</summary>
    public const float KnockRadiusFactor = 0.8f;
    /// <summary>Units slower than this (world units/s) don't knock trees over.</summary>
    public const float KnockMinSpeed = 0.3f;
    /// <summary>Area damage at least this strong sets trees on fire.</summary>
    public const float IgniteMinDamage = 25f;
    /// <summary>Area damage at least this strong also knocks trees over.</summary>
    public const float KnockDownDamage = 400f;
    public const int BurnTicks = 150;
    public const int BurnJitterTicks = 40;
    /// <summary>Ticks before the end of its burn when a burning tree tries to ignite its neighbours.</summary>
    public const int SpreadBeforeEndTicks = 60;
    public const float SpreadRadius = 2.6f;
    public const float SpreadChance = 0.3f;
    /// <summary>Value left after burning (energy / mass multipliers).</summary>
    public const float BurntEnergyFactor = 0.25f;
    public const float BurntMassFactor = 0.5f;

    [ThreadStatic, Persistence.NoSave] private static List<int>? t_tmp;
    private static List<int> _tmp => t_tmp ??= new();
    [ThreadStatic, Persistence.NoSave] private static List<(int prop, float dir)>? t_falls;

    public static void Run(SimWorld w)
    {
        var props = w.Props;
        if (props.Count == 0) return;
        KnockOver(w);
        Burn(w);
    }

    private static void KnockOver(SimWorld w)
    {
        var props = w.Props;
        var u = w.Units;
        if (u.AliveCount == 0) return;
        props.EnsureGrid();
        var buf = props.FallScratch ??= new ChunkedBuffer<(int, float)>();
        const int chunk = 256;
        buf.Reset(Par.ChunkCount(u.AliveCount, chunk));
        Par.For(u.AliveCount, chunk, w, static (w, c, start, end) =>
        {
            var u = w.Units;
            var props = w.Props;
            var outList = props.FallScratch![c];
            for (int k = start; k < end; k++)
            {
                int i = u.AliveList[k];
                if (u.Move[i].Speed < KnockMinSpeed || !u.IsComplete(i)) continue;
                var bp = w.BpOf(i);
                if (bp.IsStructure || OrderSystem.LayerOf(w, i) != LayerMask.Land) continue;
                var pos = u.Pos[i];
                float reach = bp.Radius * KnockRadiusFactor;
                float q = reach + 1f;
                int x0 = (int)MathF.Floor((pos.X - q) / PropStore.GridCell), x1 = (int)MathF.Floor((pos.X + q) / PropStore.GridCell);
                int z0 = (int)MathF.Floor((pos.Y - q) / PropStore.GridCell), z1 = (int)MathF.Floor((pos.Y + q) / PropStore.GridCell);
                for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                    foreach (int p in props.Cell(cx, cz))
                    {
                        if (props.Kind[p] != PropKind.Tree || (props.Flags[p] & PropFlags.Fallen) != 0) continue;
                        float r = reach + props.Radius[p];
                        if (Vector2.DistanceSquared(props.Pos[p], pos) <= r * r) outList.Add((p, u.Heading[i]));
                    }
            }
        });
        var falls = t_falls ??= new();
        falls.Clear();
        buf.DrainTo(falls);
        foreach (var (p, dir) in falls) Fell(w, p, dir);
        falls.Clear();
    }

    /// <summary>Knocks tree p over towards dir (no-op if it already lies). Sequential.</summary>
    public static void Fell(SimWorld w, int p, float dir)
    {
        var props = w.Props;
        if (!props.Alive[p] || props.Kind[p] != PropKind.Tree || (props.Flags[p] & PropFlags.Fallen) != 0) return;
        props.FallHeading[p] = DMath.WrapAngle(dir);
        props.SetFlags(p, props.Flags[p] | PropFlags.Fallen, w.Tick);
    }

    /// <summary>Sets tree p on fire (no-op if burning or burnt). Sequential.</summary>
    public static void Ignite(SimWorld w, int p)
    {
        var props = w.Props;
        if (!props.Alive[p] || props.Kind[p] != PropKind.Tree || (props.Flags[p] & (PropFlags.Burning | PropFlags.Burnt)) != 0) return;
        var rng = DetRandom.For(w.Seed, w.Tick, props.Id[p], 0x7EE);
        props.BurnEnd[p] = w.Tick + BurnTicks + rng.Range(-BurnJitterTicks, BurnJitterTicks + 1);
        props.SetFlags(p, props.Flags[p] | PropFlags.Burning, w.Tick);
        props.Burning.Add(props.HandleOf(p));
    }

    private static void Burn(SimWorld w)
    {
        var props = w.Props;
        var list = props.Burning;
        if (list.Count == 0) return;
        int write = 0;
        // Index loop: Ignite may append (those are kept and handled from the next tick on).
        for (int k = 0; k < list.Count; k++)
        {
            var h = list[k];
            if (!props.IsActive(h)) continue;
            int p = h.Index;
            int end = props.BurnEnd[p];
            if (w.Tick == end - SpreadBeforeEndTicks) Spread(w, p);
            if (w.Tick >= end)
            {
                props.Energy[p] *= BurntEnergyFactor; props.EnergyMax[p] *= BurntEnergyFactor;
                props.Mass[p] *= BurntMassFactor; props.MassMax[p] *= BurntMassFactor;
                props.SetFlags(p, (props.Flags[p] & ~PropFlags.Burning) | PropFlags.Burnt, w.Tick);
                continue;
            }
            list[write++] = h;
        }
        list.RemoveRange(write, list.Count - write);
    }

    private static void Spread(SimWorld w, int p)
    {
        var props = w.Props;
        var tmp = _tmp;
        tmp.Clear();
        props.QueryRadius(props.Pos[p], SpreadRadius, tmp);
        foreach (int j in tmp)
        {
            if (j == p || props.Kind[j] != PropKind.Tree || (props.Flags[j] & (PropFlags.Burning | PropFlags.Burnt)) != 0) continue;
            var rng = DetRandom.For(w.Seed, w.Tick, props.Id[j], 0x5B4E);
            if (rng.Chance(SpreadChance)) Ignite(w, j);
        }
        tmp.Clear();
    }

    /// <summary>Area damage and nuke blasts of this tick ignite / knock over trees. Call at the start of DamageSystem.</summary>
    public static void OnDamage(SimWorld w)
    {
        var props = w.Props;
        if (props.Count == 0) return;
        var tmp = _tmp;
        foreach (var a in w.Events.AreaDamage)
        {
            if (a.Amount < IgniteMinDamage) continue;
            if (a.Layers != LayerMask.None && (a.Layers & LayerMask.Land) == 0) continue; // flak etc.
            tmp.Clear();
            props.QueryRadius(a.Center, a.Radius + 0.5f, tmp);
            foreach (int p in tmp) Blast(w, p, a.Center, a.Amount >= KnockDownDamage);
        }
        foreach (var d in Strategic.StrategicState.Of(w).Detonations)
        {
            tmp.Clear();
            props.QueryRadius(d.Center, d.OuterRadius, tmp);
            foreach (int p in tmp) Blast(w, p, d.Center, true);
        }
        tmp.Clear();
    }

    private static void Blast(SimWorld w, int p, Vector2 center, bool knock)
    {
        var props = w.Props;
        if (props.Kind[p] != PropKind.Tree) return;
        if (knock)
        {
            var away = props.Pos[p] - center;
            Fell(w, p, away.LengthSquared() > 1e-6f ? DMath.HeadingOf(away) : props.Heading[p]);
        }
        Ignite(w, p);
    }
}
