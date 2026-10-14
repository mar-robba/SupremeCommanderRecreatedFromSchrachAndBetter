using System.Numerics;
using Raylib_cs;
using Rts.Client.Rendering;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;

namespace Rts.Client;

/// <summary>Shared client-side services passed to UI/input/render modules.</summary>
public sealed class GameContext
{
    public required BlueprintDb Db;
    public required Terrain Terrain;
    public required SimRunner Runner;
    public required SnapshotInterpolator Snapshots;
    public required RtsCamera Camera;
    public required TerrainRenderer TerrainRenderer;
    public required FeedbackMarkers Feedback;
    public int LocalArmy;
    public Color[] ArmyColors = [];
    /// <summary>Fog of war renderer (null before it is created).</summary>
    public FogRenderer? Fog;
    /// <summary>Watching a replay: no orders (selection and info panels only), see Rts.Client/Persistence.</summary>
    public bool ReadOnly;

    /// <summary>Sandbox: units of every army can be selected and ordered (commands are split per army,
    /// see <see cref="Input.ArmyCommands"/>). False in normal games.</summary>
    public bool ControlAnyArmy;

    /// <summary>Can the player select / order units of this army?</summary>
    public bool Controls(int army) => army == LocalArmy || ControlAnyArmy;

    public bool IsEnemy(int army) => AreEnemies(LocalArmy, army);

    /// <summary>True if armies a and b are on different teams.</summary>
    public bool AreEnemies(int a, int b)
    {
        var armies = Snapshots.Current.Armies;
        return a != b && (uint)a < (uint)armies.Length && (uint)b < (uint)armies.Length && armies[b].Team != armies[a].Team;
    }

    public Vector3? PickGround(Vector2 screen)
    {
        var ray = Raylib.GetScreenToWorldRay(screen, Camera.Camera);
        return TerrainRenderer.Pick(ray, Camera.Distance * 4f + 200f);
    }
}

/// <summary>Short-lived visual markers for issued orders.</summary>
public sealed class FeedbackMarkers
{
    private readonly List<(Vector3 pos, OrderType type, double t)> _markers = new();

    public void AddClickMarker(Vector3 pos, OrderType type) => _markers.Add((pos, type, Raylib.GetTime()));

    public void Draw()
    {
        double now = Raylib.GetTime();
        _markers.RemoveAll(m => now - m.t > 0.6);
        foreach (var (pos, type, t) in _markers)
        {
            float k = (float)((now - t) / 0.6);
            var c = type switch { OrderType.AttackMove => Color.Red, OrderType.Patrol => Color.SkyBlue, _ => Color.Green };
            Raylib.DrawCircle3D(pos + new Vector3(0, 0.2f, 0), 1.5f * (1 - k) + 0.3f, Vector3.UnitX, 90, ColorUtil.WithAlpha(c, (byte)(255 * (1 - k))));
        }
    }
}
