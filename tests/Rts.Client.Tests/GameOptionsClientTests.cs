using Rts.Client.Menus;
using Rts.Client.Persistence;
using Rts.Client.UI;
using Rts.Net.Lobby;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Persistence;

namespace Rts.Client.Tests;

/// <summary>(6b) Game options on the client: CLI --options, setup defaults, loads, the lobby config, the HUD unit counter.</summary>
public class GameOptionsClientTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    [Fact]
    public void CliOptionsReachTheSimulation()
    {
        var o = ClientOptions.Parse(["--options", "victory=supremacy,cap=250,fog=explored"]);
        Assert.Equal(StartScreen.Game, o.StartScreen);
        var cfg = GameConfig.FromOptions(o);
        Assert.Equal(VictoryCondition.Supremacy, cfg.Options.Victory);
        Assert.Equal(250, cfg.Options.UnitCap);
        var sim = GameSession.CreateSimulation(Db, cfg, out _);
        Assert.True(sim.World.Options.SameAs(cfg.Options));
        Assert.Equal(250, sim.World.Armies[0].UnitCap);

        // Without --options: today's CLI behavior (cheats on, so the debug keys/demo spawns keep working).
        Assert.True(GameConfig.FromOptions(ClientOptions.Parse(["--seed", "3"])).Options.SameAs(new GameOptions()));
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--options", "cap=zero"]));
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--options", "colour=red"]));
    }

    [Fact]
    public void SetupDefaultsAndCopiesKeepTheOptions()
    {
        var cfg = new GameConfig();
        Assert.True(cfg.Options.SameAs(GameOptions.SetupDefaults()));
        cfg.Options.UnitCap = 500;
        var copy = cfg.Clone();
        copy.Options.UnitCap = 750;
        Assert.Equal(500, cfg.Options.UnitCap);
        Assert.Equal(500, cfg.ToSkirmish(out _).Options!.UnitCap);

        // A loaded game's config (restart) keeps the options of the header; old files (null) = defaults.
        cfg.Options.Prebuilt = true;
        var sim = GameSession.CreateSimulation(Db, cfg, out int local);
        var info = GameInfo.ForSkirmish(cfg.ToSkirmish(out _), sim, local, false);
        Assert.True(ClientGameData.ConfigFor(info).Options.SameAs(cfg.Options));
        info.Options = null;
        Assert.True(ClientGameData.ConfigFor(info).Options.SameAs(new GameOptions()));

        // Multiplayer: the lobby's options go into the session config.
        var l = LobbyState.CreateDefault("Host");
        l.Options.Game.Victory = VictoryCondition.Annihilation;
        Assert.Equal(VictoryCondition.Annihilation, NetGameConfig.From(l, 0).Options.Victory);
    }

    [Fact]
    public void UnitCapCounterLevelsAndOneAlert()
    {
        Assert.Equal("units 123/500", UnitCapCounter.Format(123, 500));
        Assert.Equal(UnitCapCounter.Level.Normal, UnitCapCounter.LevelOf(449, 500));
        Assert.Equal(UnitCapCounter.Level.Near, UnitCapCounter.LevelOf(450, 500));
        Assert.Equal(UnitCapCounter.Level.Full, UnitCapCounter.LevelOf(500, 500));
        var c = new UnitCapCounter();
        Assert.False(c.ShouldAlert(499, 500));
        Assert.True(c.ShouldAlert(500, 500));
        Assert.False(c.ShouldAlert(500, 500));
        Assert.False(c.ShouldAlert(470, 500)); // still near: not re-armed
        Assert.False(c.ShouldAlert(500, 500));
        Assert.False(c.ShouldAlert(400, 500)); // below 90%: re-armed
        Assert.True(c.ShouldAlert(500, 500));
    }
}
