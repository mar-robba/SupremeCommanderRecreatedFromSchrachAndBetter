using System.Numerics;
using Rts.Client.Menus;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Client.Tests;

/// <summary><c>--scene transport</c>: option defaults and the scripted setup (headless, no window).</summary>
public class TransportSceneTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    [Fact]
    public void SceneSpawnsLoadedCarriersThatKeepTheirCargo()
    {
        var o = ClientOptions.Parse(["--scene", "transport"]);
        Assert.Equal(MapPreset.Archipelago, o.Preset);
        Assert.True(o.NoFog);
        var cfg = GameConfig.FromOptions(o);
        var sim = GameSession.CreateSimulation(Db, cfg, out _);
        var w = sim.World;
        Assert.NotNull(cfg.SceneCamera);
        Assert.All(w.Armies, a => Assert.False(a.IsAI));

        int Find(string id)
        {
            int bp = Db.IndexOf(id);
            for (int k = 0; k < w.Units.AliveCount; k++)
                if (w.Units.Bp[w.Units.AliveList[k]] == bp) return w.Units.AliveList[k];
            return -1;
        }
        int c14 = Find("uef_t2_transport"), staging = Find("uef_t2_air_staging"), atlantis = Find("uef_x_atlantis");
        Assert.True(c14 >= 0 && staging >= 0 && atlantis >= 0, $"c14 {c14} staging {staging} atlantis {atlantis}");
        Assert.Equal(12, w.Cargo.UsedSlots(w, c14));
        Assert.Equal(3, w.Cargo.Count(staging));
        Assert.Equal(13, w.Cargo.Count(atlantis));
        var c14Start = w.Units.Pos[c14];

        for (int t = 0; t < 55; t++) sim.Step();

        // The C-14 is on its way with the full load, the staging still repairs, the Atlantis keeps its deck and builds.
        Assert.Equal(12, w.Cargo.UsedSlots(w, c14));
        Assert.True(Vector2.Distance(c14Start, w.Units.Pos[c14]) > 20f, $"C-14 at {w.Units.Pos[c14]} (from {c14Start})");
        Assert.Equal(3, w.Cargo.Count(staging));
        Assert.Equal(13, w.Cargo.Count(atlantis));
        Assert.True(w.Units.IsActive(w.Units.Factory[atlantis].Current), "Atlantis builds");
        // Tanks boarded the C-6 / the ferry by walking up.
        int land = 0;
        int c6 = Db.IndexOf("uef_t1_transport");
        for (int k = 0; k < w.Units.AliveCount; k++)
        {
            int i = w.Units.AliveList[k];
            if (w.Units.Bp[i] == c6) land += w.Cargo.Count(i);
        }
        Assert.True(land >= 4, $"{land} units in the C-6s");
    }
}
