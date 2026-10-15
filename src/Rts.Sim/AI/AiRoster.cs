using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Blueprint indices of every unit an AI army can use, resolved once per brain from its faction's roles
/// (<see cref="FactionDb.RoleBlueprint"/>; -1 = the faction has no such unit, every consumer handles it). Tiered roles
/// are arrays indexed by tech level 1..3 (index 0 unused). The AI never names blueprint ids.
/// </summary>
public sealed class AiRoster
{
    public readonly int[] Engineer = [-1, -1, -1, -1];
    public readonly int Scu;
    // Land
    public readonly int Scout, Tank1, Artillery1, MobileAa1;
    public readonly int Amphibious2, HeavyTank2, Mml2, MobileAa2, MobileShield2;
    public readonly int Assault3, Siege3, Sniper3, Artillery3;
    // Air
    public readonly int AirScout1, Interceptor1, Bomber1, Transport1;
    public readonly int FighterBomber2, Gunship2, TorpedoBomber2, Transport2, AirStaging;
    public readonly int AirSuperiority3, SpyPlane3, StratBomber3, Gunship3;
    // Naval
    public readonly int Frigate1, Sub1, Destroyer2, Cruiser2, Battleship3, Carrier3;
    // Factories (by tier)
    public readonly int[] LandFactory = [-1, -1, -1, -1];
    public readonly int[] AirFactory = [-1, -1, -1, -1];
    public readonly int[] NavalFactory = [-1, -1, -1, -1];
    public readonly int Gateway;
    // Economy
    public readonly int[] Mex = [-1, -1, -1, -1];
    public readonly int[] Pgen = [-1, -1, -1, -1];
    public readonly int Hydro, MassFab2, MassFab3, MassStorage, EnergyStorage;
    // Defense / strategic
    public readonly int[] PointDefense = [-1, -1, -1, -1];
    public readonly int[] AntiAir = [-1, -1, -1, -1];
    public readonly int Torpedo1, Torpedo2, Shield2, Shield3, Tml, Tmd, ArtilleryInst2, ArtilleryInst3, NukeSilo, AntiNuke;
    // Intel
    public readonly int Radar1, Radar2, Omni3, Sonar1, Sonar2;
    // Experimentals
    public readonly int ExpLand, ExpLand2, ExpAir, ExpNaval, ExpArtillery;

    public AiRoster(BlueprintDb db, string faction)
    {
        var f = db.Factions;
        int R(string role) => f.RoleBlueprint(faction, role);
        Engineer[1] = R(FactionRoles.EngineerT1); Engineer[2] = R(FactionRoles.EngineerT2); Engineer[3] = R(FactionRoles.EngineerT3);
        Scu = R(FactionRoles.Scu);
        Scout = R(FactionRoles.ScoutT1); Tank1 = R(FactionRoles.TankT1); Artillery1 = R(FactionRoles.ArtilleryT1); MobileAa1 = R(FactionRoles.MobileAaT1);
        Amphibious2 = R(FactionRoles.AmphibiousT2); HeavyTank2 = R(FactionRoles.HeavyTankT2); Mml2 = R(FactionRoles.MmlT2);
        MobileAa2 = R(FactionRoles.MobileAaT2); MobileShield2 = R(FactionRoles.MobileShieldT2);
        Assault3 = R(FactionRoles.AssaultT3); Siege3 = R(FactionRoles.SiegeT3); Sniper3 = R(FactionRoles.SniperT3); Artillery3 = R(FactionRoles.ArtilleryT3);
        AirScout1 = R(FactionRoles.AirScoutT1); Interceptor1 = R(FactionRoles.InterceptorT1); Bomber1 = R(FactionRoles.BomberT1); Transport1 = R(FactionRoles.TransportT1);
        FighterBomber2 = R(FactionRoles.FighterBomberT2); Gunship2 = R(FactionRoles.GunshipT2); TorpedoBomber2 = R(FactionRoles.TorpedoBomberT2);
        Transport2 = R(FactionRoles.TransportT2); AirStaging = R(FactionRoles.AirStaging);
        AirSuperiority3 = R(FactionRoles.AirSuperiorityT3); SpyPlane3 = R(FactionRoles.SpyPlaneT3); StratBomber3 = R(FactionRoles.StratBomberT3);
        Gunship3 = R(FactionRoles.GunshipT3);
        Frigate1 = R(FactionRoles.FrigateT1); Sub1 = R(FactionRoles.SubT1); Destroyer2 = R(FactionRoles.DestroyerT2); Cruiser2 = R(FactionRoles.CruiserT2);
        Battleship3 = R(FactionRoles.BattleshipT3); Carrier3 = R(FactionRoles.CarrierT3);
        LandFactory[1] = R(FactionRoles.LandFactoryT1); LandFactory[2] = R(FactionRoles.LandFactoryT2); LandFactory[3] = R(FactionRoles.LandFactoryT3);
        AirFactory[1] = R(FactionRoles.AirFactoryT1); AirFactory[2] = R(FactionRoles.AirFactoryT2); AirFactory[3] = R(FactionRoles.AirFactoryT3);
        NavalFactory[1] = R(FactionRoles.NavalFactoryT1); NavalFactory[2] = R(FactionRoles.NavalFactoryT2); NavalFactory[3] = R(FactionRoles.NavalFactoryT3);
        Gateway = R(FactionRoles.QuantumGateway);
        Mex[1] = R(FactionRoles.MexT1); Mex[2] = R(FactionRoles.MexT2); Mex[3] = R(FactionRoles.MexT3);
        Pgen[1] = R(FactionRoles.PgenT1); Pgen[2] = R(FactionRoles.PgenT2); Pgen[3] = R(FactionRoles.PgenT3);
        Hydro = R(FactionRoles.Hydro); MassFab2 = R(FactionRoles.MassFabT2); MassFab3 = R(FactionRoles.MassFabT3);
        MassStorage = R(FactionRoles.MassStorage); EnergyStorage = R(FactionRoles.EnergyStorage);
        PointDefense[1] = R(FactionRoles.PdT1); PointDefense[2] = R(FactionRoles.PdT2); PointDefense[3] = R(FactionRoles.PdT3);
        AntiAir[1] = R(FactionRoles.AaT1); AntiAir[2] = R(FactionRoles.AaT2); AntiAir[3] = R(FactionRoles.AaT3);
        Torpedo1 = R(FactionRoles.TorpedoT1); Torpedo2 = R(FactionRoles.TorpedoT2);
        Shield2 = R(FactionRoles.ShieldT2); Shield3 = R(FactionRoles.ShieldT3);
        Tml = R(FactionRoles.Tml); Tmd = R(FactionRoles.Tmd);
        ArtilleryInst2 = R(FactionRoles.ArtilleryInstallationT2); ArtilleryInst3 = R(FactionRoles.ArtilleryInstallationT3);
        NukeSilo = R(FactionRoles.NukeSilo); AntiNuke = R(FactionRoles.AntiNuke);
        Radar1 = R(FactionRoles.RadarT1); Radar2 = R(FactionRoles.RadarT2); Omni3 = R(FactionRoles.OmniT3);
        Sonar1 = R(FactionRoles.SonarT1); Sonar2 = R(FactionRoles.SonarT2);
        ExpLand = R(FactionRoles.ExpLand); ExpLand2 = R(FactionRoles.ExpLand2); ExpAir = R(FactionRoles.ExpAir);
        ExpNaval = R(FactionRoles.ExpNaval); ExpArtillery = R(FactionRoles.ExpArtillery);
    }

    /// <summary>The highest tier (≤ maxTier) of a tiered role that exists, or -1.</summary>
    public static int Best(int[] tiers, int maxTier)
    {
        for (int t = Math.Min(3, maxTier); t >= 1; t--)
            if (tiers[t] >= 0) return tiers[t];
        return -1;
    }

    /// <summary>Can a unit of blueprint <paramref name="builder"/> build <paramref name="bp"/>?</summary>
    public static bool CanBuild(UnitBlueprint builder, int bp) => bp >= 0 && Array.IndexOf(builder.BuildIndices, bp) >= 0;
}
