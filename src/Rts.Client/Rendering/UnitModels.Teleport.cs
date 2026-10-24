using System.Numerics;

namespace Rts.Client.Rendering;

public static partial class UnitModels
{
    /// <summary>
    /// Personal Teleporter (commander enhancement, Visual "teleporter", Back slot): a mounting frame on the back with a
    /// glowing vertical quantum core wrapped by two coil rings (team-coloured segments), side struts and a glowing
    /// emitter cap on top. Same part for UEF and Cybran ACUs; Cybran gets its darker hull colours.
    /// </summary>
    private static void EnhTeleporter(MeshBuilder m, MechAnchor a, float w, float h, float l, bool cybran)
    {
        var hull = cybran ? CyHull : Hull;
        var light = cybran ? CyHullLight : HullLight;
        var dark = cybran ? CyHullDark : HullDark;
        float y = a.BackY, z = a.BackZ, zc = z - 0.09f * l;
        // Mounting frame against the back and a base housing for the core.
        m.BevelBox(V(0, y + 0.05f * h, z), V(0.34f * w, 0.16f * h, 0.1f * l), 0.025f * w, hull, light);
        m.Cylinder(V(0, y - 0.02f * h, zc), Axis.Y, 0.08f * h, 0.11f * w, 0.1f * w, 8, dark, Dark);
        // Glowing core column and the two coil rings around it.
        m.Cylinder(V(0, y + 0.06f * h, zc), Axis.Y, 0.3f * h, 0.045f * w, 0.04f * w, 8, GlowC);
        Ring(m, V(0, y + 0.13f * h, zc), 0.1f * w, 0.025f * w, 0.035f * h, 10, light, Team);
        Ring(m, V(0, y + 0.25f * h, zc), 0.085f * w, 0.022f * w, 0.03f * h, 10, light, Team);
        // Struts from the frame to the coils.
        foreach (int k in (ReadOnlySpan<int>)[-1, 1])
        {
            m.Box(V(k * 0.12f * w, y + 0.19f * h, z - 0.045f * l), V(0.025f * w, 0.18f * h, 0.03f * l), dark);
            m.Box(V(k * 0.1f * w, y + 0.13f * h, z - 0.05f * l), V(0.02f * w, 0.02f * h, 0.08f * l), dark);
        }
        // Emitter cap with a glowing tip.
        m.Cylinder(V(0, y + 0.35f * h, zc), Axis.Y, 0.025f * h, 0.07f * w, 0.05f * w, 8, dark, Team);
        m.Sphere(V(0, y + 0.39f * h, zc), V(0.05f * w, 0.05f * w, 0.05f * w), 4, 6, GlowC);
    }
}
