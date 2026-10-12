using System.Numerics;
using Rts.Sim.Data;

namespace Rts.Client.Rendering;

/// <summary>
/// Naval wave shapes: the floating water structures (naval factory dock, sonar buoy, torpedo launcher platform), the
/// T3 battleship and the T2 torpedo bomber. Water structures sit at the water surface (sim elevation = water level),
/// so their model origin (y = 0) is the water line and the pontoons reach a bit below it, like the ship hulls.
/// Same palette, material tags and turret conventions as the T1 ships. Dispatched from <see cref="Build"/>.
/// </summary>
public static partial class UnitModels
{
    /// <summary>Builds the body into b and returns the turret mesh (or null), adding its pivots and turret mode.</summary>
    private static MeshBuilder? NavalShape(UnitBlueprint bp, MeshBuilder b, float w, float h, float l, bool hasWeapon,
                                           List<Vector3> pivots, ref TurretMode mode, ref float spin)
    {
        int tier = bp.Has(UnitCategory.Tech3) ? 3 : bp.Has(UnitCategory.Tech2) ? 2 : 1;
        MeshBuilder? t = null;
        switch (bp.Visual.Shape)
        {
            case "naval_factory":
                NavalFactory(b, w, h, l, tier);
                break;
            case "battleship":
            {
                float deck = Battleship(b, w, h, l);
                if (hasWeapon)
                {
                    // Two forward turrets (B superfiring over A) and one aft, all on barbettes.
                    t = new MeshBuilder(); BattleshipTurret(t, w, h, l);
                    pivots.Add(V(0, deck + 0.035f * h, 0.33f * l));
                    pivots.Add(V(0, deck + 0.15f * h, 0.195f * l));
                    pivots.Add(V(0, deck + 0.035f * h, -0.30f * l));
                }
                break;
            }
            case "sonar":
                Sonar(b, w, h, l, tier);
                t = new MeshBuilder(); SonarHead(t, w, h, l, tier); pivots.Add(V(0, 0.82f * h, 0));
                mode = TurretMode.Spin; spin = tier >= 2 ? 1.9f : 1.4f;
                break;
            case "torpedo_launcher":
                TorpedoPlatform(b, w, h, l);
                if (hasWeapon) { t = new MeshBuilder(); TorpedoTurret(t, w, h, l); pivots.Add(V(0, 0.215f * h, 0)); }
                break;
            case "torpedo_bomber":
                TorpedoBomber(b, w, h, l);
                break;
        }
        return t;
    }

    // ================================================================ naval factory (floating dock)

    /// <summary>
    /// 10x10 floating dock: two pontoon piers framing a slipway that opens toward +Z (the rollout side), a hall closing
    /// the back, gantry cranes spanning the bay, a control tower and team stripes. Higher tiers add stacks and cranes.
    /// </summary>
    private static void NavalFactory(MeshBuilder b, float w, float h, float l, int tier)
    {
        float top = 0.14f * h, bot = -0.3f * h, ph = top - bot, pc = (top + bot) * 0.5f;
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            float px = sd * 0.37f * w;
            b.Loft(MeshBuilder.Chamfered(0.24f * w, ph, 0.05f * h), [
                new Station(-0.5f * l, 1, 1, px, pc), new Station(0.42f * l, 1, 1, px, pc),
                new Station(0.5f * l, 0.82f, 0.9f, px, pc + 0.05f * ph)], Axis.Z, Hull, HullDark);
            b.Box(V(px, top + 0.002f, -0.03f * l), V(0.2f * w, 0.004f, 0.86f * l), Pad);
            b.Box(V(sd * 0.492f * w, 0.04f * h, -0.04f * l), V(0.012f * w, 0.05f * h, 0.78f * l), Team);
            // Guide lights along the slipway edge and beacons at the pier tips.
            for (int k = 0; k < 6; k++)
                b.Box(V(sd * 0.262f * w, top + 0.01f * h, (-0.14f + k * 0.12f) * l), V(0.014f * w, 0.02f * h, 0.014f * w), GlowC);
            b.Box(V(sd * 0.3f * w, top + 0.03f * h, 0.47f * l), V(0.03f * w, 0.05f * h, 0.02f * l), GlowY);
            // Back corner towers.
            b.Frustum(V(sd * 0.42f * w, top, -0.44f * l), 0.12f * w, 0.1f * l, 0.09f * w, 0.07f * l, 0.85f * h, Hull, HullDark, null, 0.15f);
            b.Box(V(sd * 0.42f * w, top + 0.62f * h, -0.44f * l), V(0.1f * w, 0.09f * h, 0.08f * l), Team);
            b.Box(V(sd * 0.42f * w, top + 0.88f * h, -0.44f * l), V(0.025f * w, 0.05f * h, 0.025f * w), GlowR);
        }
        // Back block closing the U + the hall on top of it, its door facing the slipway.
        b.Box(V(0, pc, -0.36f * l), V(0.52f * w, ph, 0.28f * l), Hull, Pad);
        b.Box(V(0, top + 0.3f * h, -0.37f * l), V(0.7f * w, 0.6f * h, 0.24f * l), Hull);
        b.Frustum(V(0, top + 0.6f * h, -0.37f * l), 0.7f * w, 0.24f * l, 0.56f * w, 0.14f * l, 0.12f * h, HullLight, HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Box(V(sd * 0.351f * w, top + 0.42f * h, -0.37f * l), V(0.01f * w, 0.08f * h, 0.2f * l), Team);
            b.Box(V(sd * 0.23f * w, top + 0.24f * h, -0.245f * l), V(0.03f * w, 0.48f * h, 0.03f * l), HullLight);
        }
        b.Box(V(0, top + 0.24f * h, -0.249f * l), V(0.42f * w, 0.46f * h, 0.01f * l), Black);
        b.Box(V(0, top + 0.5f * h, -0.245f * l), V(0.5f * w, 0.06f * h, 0.02f * l), Team);
        for (int i = -1; i <= 1; i++)
            b.Cylinder(V(i * 0.14f * w, top + 0.68f * h, -0.39f * l), Axis.Y, 0.1f * h, 0.035f * w, 0.03f * w, 6, Dark);
        // Submerged slipway with cradle blocks and rails (seen through the water).
        b.Box(V(0, -0.25f * h, 0.13f * l), V(0.5f * w, 0.03f * h, 0.7f * l), Dark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.16f * w, -0.22f * h, 0.13f * l), V(0.02f * w, 0.04f * h, 0.7f * l), HullDark);
        foreach (float z in (ReadOnlySpan<float>)[-0.1f, 0.1f, 0.3f])
            b.Box(V(0, -0.2f * h, z * l), V(0.2f * w, 0.07f * h, 0.03f * l), HullDark);

        // Gantry cranes across the slipway, riding on the piers.
        float craneTop = top + (0.85f + 0.06f * tier) * h;
        Gantry(b, w, h, l, 0.0f, top, craneTop, 1);
        Gantry(b, w, h, l, 0.24f * l, top, craneTop, -1);
        if (tier >= 3) Gantry(b, w, h, l, 0.42f * l, top, craneTop - 0.08f * h, 1);

        // Control tower on the right pier, crates on the left one.
        float tx = 0.40f * w, tz = 0.34f * l;
        b.Cylinder(V(tx, top, tz), Axis.Y, 0.55f * h, 0.04f * w, 0.032f * w, 6, Hull);
        b.BevelBox(V(tx, top + 0.6f * h, tz), V(0.1f * w, 0.1f * h, 0.1f * l), 0.02f * w, HullLight);
        b.Box(V(tx, top + 0.6f * h, tz), V(0.104f * w, 0.04f * h, 0.104f * l), Glass);
        b.Box(V(tx, top + 0.66f * h, tz), V(0.08f * w, 0.02f * h, 0.08f * l), Team);
        b.Box(V(tx, top + 0.72f * h, tz), V(0.012f * w, 0.1f * h, 0.012f * w), HullDark);
        b.Box(V(tx, top + 0.78f * h, tz), V(0.02f * w, 0.02f * h, 0.02f * w), GlowR);
        foreach (float z in (ReadOnlySpan<float>)[0.12f, 0.26f])
            b.Box(V(-0.41f * w, top + 0.06f * h, z * l), V(0.09f * w, 0.12f * h, 0.11f * l), HullDark, Team);
        b.Box(V(-0.41f * w, top + 0.17f * h, 0.12f * l), V(0.09f * w, 0.1f * h, 0.11f * l), Dark, Team);

        if (tier >= 2)
        {
            // Exhaust stacks on the hall roof and annexes on the back corners.
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            {
                b.Cylinder(V(sd * 0.2f * w, top + 0.62f * h, -0.43f * l), Axis.Y, 0.45f * h, 0.05f * w, 0.04f * w, 8, HullDark, Dark);
                b.Cylinder(V(sd * 0.2f * w, top + 0.88f * h, -0.43f * l), Axis.Y, 0.06f * h, 0.046f * w, 0.044f * w, 8, Team);
                b.BevelBox(V(sd * 0.3f * w, top + 0.2f * h, -0.17f * l), V(0.08f * w, 0.4f * h, 0.08f * l), 0.015f * w, HullLight);
                b.Box(V(sd * 0.3f * w, top + 0.3f * h, -0.17f * l), V(0.084f * w, 0.05f * h, 0.084f * l), Team);
            }
        }
        if (tier >= 3)
        {
            // Tall tower crane on the left pier and a glowing strip along the hall front.
            float cx = -0.4f * w, cz = -0.12f * l, ct = top + 1.45f * h;
            b.Frustum(V(cx, top, cz), 0.06f * w, 0.06f * l, 0.04f * w, 0.04f * l, ct - top, HullDark, null, null, 0.2f);
            b.Box(V(cx + 0.08f * w, ct, cz), V(0.34f * w, 0.04f * h, 0.035f * l), Hull);
            b.Box(V(cx - 0.05f * w, ct - 0.05f * h, cz), V(0.07f * w, 0.1f * h, 0.05f * l), Dark);
            b.Box(V(cx + 0.08f * w, ct + 0.025f * h, cz), V(0.3f * w, 0.012f * h, 0.036f * l), Team);
            b.Box(V(cx + 0.24f * w, ct - 0.25f * h, cz), V(0.006f * w, 0.5f * h, 0.006f * w), Black);
            b.Box(V(cx, ct + 0.06f * h, cz), V(0.02f * w, 0.03f * h, 0.02f * w), GlowR);
            b.Box(V(0, top + 0.56f * h, -0.244f * l), V(0.5f * w, 0.02f * h, 0.012f * l), GlowC);
        }
    }

    private static void Gantry(MeshBuilder b, float w, float h, float l, float zc, float top, float craneTop, int trolleySide)
    {
        float legH = craneTop - top;
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            float x = sd * 0.31f * w;
            foreach (int sz in (ReadOnlySpan<int>)[-1, 1])
                b.Box(V(x, top + legH * 0.5f, zc + sz * 0.035f * l), V(0.025f * w, legH, 0.025f * l), HullDark);
            b.Box(V(x, top + 0.04f * h, zc), V(0.06f * w, 0.08f * h, 0.11f * l), Dark);
            b.Box(V(x, top + legH * 0.45f, zc), V(0.02f * w, 0.03f * h, 0.07f * l), HullDark);
            b.Box(V(sd * 0.33f * w, craneTop + 0.045f * h, zc), V(0.02f * w, 0.02f * h, 0.02f * w), GlowR);
        }
        b.Box(V(0, craneTop, zc), V(0.68f * w, 0.07f * h, 0.09f * l), Hull);
        foreach (int sz in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(0, craneTop, zc + sz * 0.0455f * l), V(0.56f * w, 0.035f * h, 0.004f * l), Team);
        float trx = trolleySide * 0.08f * w;
        b.Box(V(trx, craneTop - 0.065f * h, zc), V(0.08f * w, 0.06f * h, 0.07f * l), HullLight);
        b.Box(V(trx, craneTop - 0.35f * h, zc), V(0.006f * w, 0.52f * h, 0.006f * w), Black);
        b.Box(V(trx, craneTop - 0.62f * h, zc), V(0.03f * w, 0.05f * h, 0.03f * w), Dark);
    }

    // ================================================================ battleship

    /// <summary>T3 battleship: long round-bilged hull with a raised bow, big superstructure, funnel, AA tubs, helipad.
    /// Returns the midship deck height.</summary>
    private static float Battleship(MeshBuilder b, float w, float h, float l)
    {
        Vector2[] prof = [V2(-0.5f, 1.0f), V2(-0.46f, 0.35f), V2(-0.2f, 0.04f), V2(0, 0), V2(0.2f, 0.04f), V2(0.46f, 0.35f), V2(0.5f, 1.0f)];
        float bottom = -0.26f * h, hh = 0.5f * h, deck = bottom + hh;
        b.Loft(prof, [
            new Station(-0.5f * l, 0.78f * w, hh * 0.9f, 0, bottom + hh * 0.1f), new Station(-0.4f * l, 0.95f * w, hh, 0, bottom),
            new Station(0.18f * l, w, hh, 0, bottom), new Station(0.34f * l, 0.84f * w, hh * 1.03f, 0, bottom),
            new Station(0.45f * l, 0.42f * w, hh * 1.08f, 0, bottom), new Station(0.5f * l, 0.04f * w, hh * 1.12f, 0, bottom + hh * 0.02f)],
            Axis.Z, Hull, HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.497f * w, deck - 0.05f * h, -0.09f * l), V(0.012f * w, 0.045f * h, 0.54f * l), Team);
        b.Box(V(0, deck + 0.001f, -0.06f * l), V(0.84f * w, 0.004f, 0.62f * l), Pad);

        // Barbettes (B raised so it fires over A).
        b.Cylinder(V(0, deck - 0.02f * h, 0.33f * l), Axis.Y, 0.055f * h, 0.2f * w, 0.2f * w, 12, HullDark, Dark);
        b.Cylinder(V(0, deck - 0.02f * h, 0.195f * l), Axis.Y, 0.17f * h, 0.21f * w, 0.2f * w, 12, HullDark, Dark);
        b.Box(V(0, deck + 0.1f * h, 0.195f * l + 0.2f * w), V(0.16f * w, 0.025f * h, 0.012f * l), Team);
        b.Cylinder(V(0, deck - 0.02f * h, -0.30f * l), Axis.Y, 0.055f * h, 0.2f * w, 0.2f * w, 12, HullDark, Dark);

        // Superstructure: main block, second tier, bridge, mast; funnel aft of it.
        b.BevelBox(V(0, deck + 0.13f * h, -0.03f * l), V(0.58f * w, 0.26f * h, 0.24f * l), 0.05f * w, HullLight);
        b.Box(V(0, deck + 0.225f * h, -0.03f * l), V(0.585f * w, 0.04f * h, 0.243f * l), Team);
        b.BevelBox(V(0, deck + 0.32f * h, 0.0f), V(0.44f * w, 0.12f * h, 0.14f * l), 0.04f * w, Hull);
        b.BevelBox(V(0, deck + 0.43f * h, 0.03f * l), V(0.36f * w, 0.1f * h, 0.07f * l), 0.03f * w, HullLight);
        b.Box(V(0, deck + 0.44f * h, 0.066f * l), V(0.3f * w, 0.04f * h, 0.006f * l), Glass);
        b.Box(V(0, deck + 0.41f * h, 0.05f * l), V(0.56f * w, 0.025f * h, 0.03f * l), HullDark);
        b.Box(V(-0.28f * w, deck + 0.43f * h, 0.05f * l), V(0.025f * w, 0.025f * h, 0.025f * w), GlowR);
        b.Box(V(0.28f * w, deck + 0.43f * h, 0.05f * l), V(0.025f * w, 0.025f * h, 0.025f * w), GlowG);
        b.Frustum(V(0, deck + 0.48f * h, 0.0f), 0.12f * w, 0.04f * l, 0.05f * w, 0.02f * l, 0.4f * h, HullDark);
        b.BevelBox(V(0, deck + 0.62f * h, 0.012f * l), V(0.18f * w, 0.06f * h, 0.03f * l), 0.02f * w, Hull);
        b.Box(V(0, deck + 0.74f * h, 0), V(0.42f * w, 0.022f * h, 0.018f * l), HullDark);
        b.Box(V(0, deck + 0.9f * h, 0), V(0.035f * w, 0.035f * h, 0.035f * w), GlowR);
        b.Box(V(0, deck + 0.36f * h, -0.105f * l), V(0.2f * w, 0.22f * h, 0.07f * l), Hull, Dark);
        b.Box(V(0, deck + 0.42f * h, -0.105f * l), V(0.205f * w, 0.03f * h, 0.072f * l), Team);

        // AA gun tubs beside the superstructure (static detail; the AA railgun has no own turret).
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        foreach (float z in (ReadOnlySpan<float>)[-0.11f, 0.05f])
        {
            b.Cylinder(V(sd * 0.37f * w, deck, z * l), Axis.Y, 0.07f * h, 0.07f * w, 0.06f * w, 8, HullDark, Dark);
            var xf = PitchUp(0.6f) * T(sd * 0.37f * w, deck + 0.07f * h, z * l);
            foreach (int bx in (ReadOnlySpan<int>)[-1, 1])
                b.Cylinder(V(bx * 0.022f * w, 0, -0.01f * l), Axis.Z, 0.05f * l, 0.012f * w, 0.01f * w, 5, Dark, null, xf);
        }

        // Stern helipad with landing lights, anti-torpedo flare racks.
        b.Cylinder(V(0, deck, -0.43f * l), Axis.Y, 0.008f * h, 0.2f * w, 0.2f * w, 12, Pad);
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.Tau / 8;
            b.Box(V(MathF.Cos(a) * 0.17f * w, deck + 0.01f * h, -0.43f * l + MathF.Sin(a) * 0.17f * w), V(0.02f * w, 0.012f * h, 0.02f * w), GlowC);
        }
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.34f * w, deck + 0.03f * h, -0.38f * l), V(0.1f * w, 0.06f * h, 0.04f * l), HullDark, Team);
        return deck;
    }

    /// <summary>Big triple-gun turret (origin on the barbette top, barrels along +Z).</summary>
    private static void BattleshipTurret(MeshBuilder t, float w, float h, float l)
    {
        var prof = MeshBuilder.Chamfered(0.38f * w, 0.12f * h, 0.06f * w);
        t.Loft(prof, [new Station(-0.06f * l, 0.88f, 0.86f, 0, 0.06f * h), new Station(0.03f * l, 1, 1, 0, 0.06f * h),
                      new Station(0.055f * l, 0.72f, 0.7f, 0, 0.056f * h)], Axis.Z, Hull);
        t.Box(V(0, 0.121f * h, -0.012f * l), V(0.2f * w, 0.006f * h, 0.05f * l), Team);
        t.Box(V(0, 0.13f * h, -0.04f * l), V(0.46f * w, 0.022f * h, 0.01f * l), HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Box(V(sd * 0.23f * w, 0.13f * h, -0.04f * l), V(0.025f * w, 0.035f * h, 0.016f * l), Dark);
        t.Box(V(0, 0.06f * h, 0.054f * l), V(0.28f * w, 0.075f * h, 0.008f * l), HullDark);
        for (int i = -1; i <= 1; i++)
        {
            t.Cylinder(V(i * 0.085f * w, 0.064f * h, 0.05f * l), Axis.Z, 0.13f * l, 0.022f * w, 0.017f * w, 6, HullDark);
            t.Cylinder(V(i * 0.085f * w, 0.064f * h, 0.17f * l), Axis.Z, 0.01f * l, 0.023f * w, 0.023f * w, 6, Dark);
        }
    }

    // ================================================================ sonar buoy

    /// <summary>Floating sonar buoy: octagonal float, dome, mast. T2 adds a drum with a bigger dome and mast struts.</summary>
    private static void Sonar(MeshBuilder b, float w, float h, float l, int tier)
    {
        float top = 0.06f * h;
        b.Cylinder(V(0, -0.14f * h, 0), Axis.Y, 0.2f * h, 0.46f * w, 0.43f * w, 8, Hull, Pad);
        b.Cylinder(V(0, -0.01f * h, 0), Axis.Y, 0.04f * h, 0.457f * w, 0.453f * w, 8, Team);
        for (int i = 0; i < 4; i++)
        {
            float a = MathF.PI / 4 + i * MathF.PI / 2;
            b.Box(V(MathF.Cos(a) * 0.37f * w, top + 0.012f * h, MathF.Sin(a) * 0.37f * w), V(0.04f * w, 0.025f * h, 0.04f * w), GlowY);
        }
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.33f * w, top + 0.035f * h, 0), V(0.1f * w, 0.07f * h, 0.2f * w), Dark);
        float domeY = top;
        if (tier >= 2)
        {
            b.Cylinder(V(0, top, 0), Axis.Y, 0.12f * h, 0.3f * w, 0.28f * w, 8, HullLight, Hull);
            b.Cylinder(V(0, top + 0.07f * h, 0), Axis.Y, 0.025f * h, 0.297f * w, 0.295f * w, 8, Team);
            domeY = top + 0.12f * h;
            for (int i = 0; i < 4; i++)
            {
                float a = i * MathF.PI / 2;
                Limb(b, V(MathF.Cos(a) * 0.36f * w, top, MathF.Sin(a) * 0.36f * w), V(0, 0.5f * h, 0), 0.018f * w, HullDark);
            }
        }
        b.Sphere(V(0, domeY, 0), V(0.25f * w, 0.13f * h, 0.25f * w), 4, 10, HullLight, null, 0.5f);
        b.Cylinder(V(0, domeY + 0.08f * h, 0), Axis.Y, 0.82f * h - domeY - 0.08f * h, 0.05f * w, 0.03f * w, 6, HullDark);
        b.Box(V(0, 0.62f * h, 0), V(0.36f * w, 0.02f * h, 0.04f * w), HullDark);
        b.Box(V(-0.18f * w, 0.635f * h, 0), V(0.03f * w, 0.025f * h, 0.03f * w), GlowR);
        b.Box(V(0.18f * w, 0.635f * h, 0), V(0.03f * w, 0.025f * h, 0.03f * w), GlowG);
    }

    /// <summary>Spinning transducer head on top of the sonar mast.</summary>
    private static void SonarHead(MeshBuilder t, float w, float h, float l, int tier)
    {
        t.Cylinder(V(0, -0.02f * h, 0), Axis.Y, 0.05f * h, 0.07f * w, 0.06f * w, 6, Hull);
        t.Box(V(0, 0.01f * h, 0), V(0.56f * w, 0.025f * h, 0.05f * w), HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Cylinder(V(sd * 0.28f * w, -0.015f * h, 0), Axis.Y, 0.05f * h, 0.055f * w, 0.05f * w, 6, Hull, Team);
        if (tier >= 2)
        {
            t.Box(V(0, 0.01f * h, 0), V(0.05f * w, 0.025f * h, 0.4f * w), HullDark);
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
                t.Cylinder(V(0, -0.01f * h, sd * 0.2f * w), Axis.Y, 0.04f * h, 0.045f * w, 0.04f * w, 6, Hull, Team);
        }
        t.Box(V(0, 0.045f * h, 0), V(0.04f * w, 0.03f * h, 0.04f * w), GlowC);
    }

    // ================================================================ torpedo launcher

    /// <summary>Floating octagonal platform for the torpedo turret.</summary>
    private static void TorpedoPlatform(MeshBuilder b, float w, float h, float l)
    {
        float top = 0.2f * h;
        b.Cylinder(V(0, -0.3f * h, 0), Axis.Y, 0.5f * h, 0.49f * w, 0.45f * w, 8, Hull, Pad);
        b.Cylinder(V(0, 0.02f * h, 0), Axis.Y, 0.07f * h, 0.472f * w, 0.466f * w, 8, Team);
        b.Cylinder(V(0, top, 0), Axis.Y, 0.015f * h, 0.36f * w, 0.36f * w, 8, HullDark);
        for (int i = 0; i < 4; i++)
        {
            float a = MathF.PI / 4 + i * MathF.PI / 2;
            b.Box(V(MathF.Cos(a) * 0.41f * w, top + 0.015f * h, MathF.Sin(a) * 0.41f * w), V(0.04f * w, 0.03f * h, 0.04f * w), GlowY);
            float c = i * MathF.PI / 2;
            b.Box(V(MathF.Cos(c) * 0.47f * w, 0.0f, MathF.Sin(c) * 0.47f * w), V(0.07f * w, 0.12f * h, 0.07f * w), HullDark);
        }
    }

    /// <summary>Rotating torpedo turret: armored housing with three forward tubes.</summary>
    private static void TorpedoTurret(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.08f * h, 0.32f * w, 0.3f * w, 8, Dark);
        var prof = MeshBuilder.Chamfered(0.5f * w, 0.26f * h, 0.07f * w);
        t.Loft(prof, [new Station(-0.26f * l, 0.85f, 0.8f, 0, 0.2f * h), new Station(0.06f * l, 1, 1, 0, 0.2f * h),
                      new Station(0.16f * l, 0.8f, 0.75f, 0, 0.19f * h)], Axis.Z, Hull);
        t.Box(V(0, 0.331f * h, -0.06f * l), V(0.24f * w, 0.008f * h, 0.2f * l), Team);
        for (int i = -1; i <= 1; i++)
        {
            float x = i * 0.14f * w;
            t.Cylinder(V(x, 0.2f * h, 0.0f), Axis.Z, 0.42f * l, 0.06f * w, 0.06f * w, 8, HullDark);
            t.Cylinder(V(x, 0.2f * h, 0.34f * l), Axis.Z, 0.04f * l, 0.07f * w, 0.07f * w, 8, Dark);
            t.Cylinder(V(x, 0.2f * h, 0.42f * l), Axis.Z, 0.008f * l, 0.048f * w, 0.048f * w, 8, Black);
        }
        t.Box(V(0.13f * w, 0.345f * h, 0.06f * l), V(0.05f * w, 0.025f * h, 0.05f * w), GlowR);
    }

    // ================================================================ torpedo bomber

    /// <summary>T2 torpedo bomber: the bomber airframe with a torpedo slung under the fuselage, a chin sonar pod
    /// and a team-colored spine stripe.</summary>
    private static void TorpedoBomber(MeshBuilder b, float w, float h, float l)
    {
        Bomber(b, w, h, l);
        float belly = 0.25f * h, r = 0.055f * w, y = belly - r - 0.012f * w;
        foreach (float z in (ReadOnlySpan<float>)[-0.1f, 0.12f])
            b.Box(V(0, (y + r + belly + 0.02f * h) * 0.5f, z * l), V(0.02f * w, belly + 0.02f * h - y - r + 0.004f, 0.06f * l), HullDark);
        b.Cylinder(V(0, y, -0.2f * l), Axis.Z, 0.42f * l, r, r, 8, HullDark);
        b.Cylinder(V(0, y, 0.22f * l), Axis.Z, 0.07f * l, r, 0.3f * r, 8, Team);
        b.Cylinder(V(0, y, -0.25f * l), Axis.Z, 0.05f * l, 0.45f * r, r, 8, HullDark);
        b.Box(V(0, y, -0.245f * l), V(0.008f * w, 2.8f * r, 0.05f * l), Dark);
        b.Box(V(0, y, -0.245f * l), V(2.8f * r, 0.008f * w, 0.05f * l), Dark);
        b.Sphere(V(0, 0.3f * h, 0.36f * l), V(0.05f * w, 0.08f * h, 0.07f * l), 3, 6, Dark);
        b.Box(V(0, 0.24f * h, 0.36f * l), V(0.02f * w, 0.02f * h, 0.02f * w), GlowC);
        b.Box(V(0, 0.752f * h, -0.02f * l), V(0.05f * w, 0.012f * h, 0.36f * l), Team);
    }
}
