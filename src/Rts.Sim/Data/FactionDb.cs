using System.Text.Json;
using System.Text.Json.Serialization;
using Rts.Sim.Core;

namespace Rts.Sim.Data;

/// <summary>
/// The role names a faction file (<c>data/factions/&lt;id&gt;.json</c>, "Roles") may map to blueprint ids. Sim/AI code
/// never hardcodes faction blueprint ids: it asks <see cref="FactionDb.RoleBlueprint"/> for the army's faction's role
/// (-1 = the faction has no unit for that role; consumers must handle it). Loading rejects role names not listed here
/// (typo protection): a new role = a new constant here + a row in the docs/ARCHITECTURE.md "Factions" table.
/// </summary>
public static class FactionRoles
{
    // Engineers
    public const string EngineerT1 = "engineer_t1", EngineerT2 = "engineer_t2", EngineerT3 = "engineer_t3", Scu = "scu";
    // Land units
    public const string ScoutT1 = "scout_t1", TankT1 = "tank_t1", ArtilleryT1 = "artillery_t1", MobileAaT1 = "mobile_aa_t1";
    public const string AmphibiousT2 = "amphibious_t2", HeavyTankT2 = "heavy_tank_t2", MmlT2 = "mml_t2", MobileAaT2 = "mobile_aa_t2",
        MobileShieldT2 = "mobile_shield_t2", MobileStealthT2 = "mobile_stealth_t2";
    public const string AssaultT3 = "assault_t3", SiegeT3 = "siege_t3", SniperT3 = "sniper_t3", ArtilleryT3 = "artillery_t3";
    // Air units
    public const string AirScoutT1 = "air_scout_t1", InterceptorT1 = "interceptor_t1", BomberT1 = "bomber_t1", TransportT1 = "transport_t1";
    public const string FighterBomberT2 = "fighter_bomber_t2", GunshipT2 = "gunship_t2", TorpedoBomberT2 = "torpedo_bomber_t2",
        TransportT2 = "transport_t2";
    public const string AirSuperiorityT3 = "air_superiority_t3", SpyPlaneT3 = "spy_plane_t3", StratBomberT3 = "strat_bomber_t3",
        GunshipT3 = "gunship_t3";
    // Naval units
    public const string FrigateT1 = "frigate_t1", SubT1 = "sub_t1", DestroyerT2 = "destroyer_t2", CruiserT2 = "cruiser_t2",
        BattleshipT3 = "battleship_t3", CarrierT3 = "carrier_t3";
    // Factories
    public const string LandFactoryT1 = "land_factory_t1", LandFactoryT2 = "land_factory_t2", LandFactoryT3 = "land_factory_t3";
    public const string AirFactoryT1 = "air_factory_t1", AirFactoryT2 = "air_factory_t2", AirFactoryT3 = "air_factory_t3";
    public const string NavalFactoryT1 = "naval_factory_t1", NavalFactoryT2 = "naval_factory_t2", NavalFactoryT3 = "naval_factory_t3";
    public const string QuantumGateway = "quantum_gateway";
    // Economy
    public const string MexT1 = "mex_t1", MexT2 = "mex_t2", MexT3 = "mex_t3";
    public const string PgenT1 = "pgen_t1", PgenT2 = "pgen_t2", PgenT3 = "pgen_t3", Hydro = "hydro";
    public const string MassFabT2 = "mass_fab_t2", MassFabT3 = "mass_fab_t3", MassStorage = "mass_storage", EnergyStorage = "energy_storage";
    // Defense
    public const string PdT1 = "pd_t1", PdT2 = "pd_t2", PdT3 = "pd_t3", AaT1 = "aa_t1", AaT2 = "aa_t2", AaT3 = "aa_t3";
    public const string TorpedoT1 = "torpedo_t1", TorpedoT2 = "torpedo_t2", ShieldT2 = "shield_t2", ShieldT3 = "shield_t3";
    public const string Tml = "tml", Tmd = "tmd", ArtilleryInstallationT2 = "artillery_installation_t2",
        ArtilleryInstallationT3 = "artillery_installation_t3", NukeSilo = "nuke_silo", AntiNuke = "anti_nuke";
    // Intel
    public const string RadarT1 = "radar_t1", RadarT2 = "radar_t2", OmniT3 = "omni_t3", SonarT1 = "sonar_t1", SonarT2 = "sonar_t2",
        StealthFieldT2 = "stealth_field_t2";
    // Experimentals
    public const string ExpLand = "exp_land", ExpLand2 = "exp_land_2", ExpAir = "exp_air", ExpNaval = "exp_naval",
        ExpArtillery = "exp_artillery";

    /// <summary>Every known role, in the documented order.</summary>
    public static readonly string[] All =
    [
        EngineerT1, EngineerT2, EngineerT3, Scu,
        ScoutT1, TankT1, ArtilleryT1, MobileAaT1, AmphibiousT2, HeavyTankT2, MmlT2, MobileAaT2, MobileShieldT2, MobileStealthT2,
        AssaultT3, SiegeT3, SniperT3, ArtilleryT3,
        AirScoutT1, InterceptorT1, BomberT1, TransportT1, FighterBomberT2, GunshipT2, TorpedoBomberT2, TransportT2,
        AirSuperiorityT3, SpyPlaneT3, StratBomberT3, GunshipT3,
        FrigateT1, SubT1, DestroyerT2, CruiserT2, BattleshipT3, CarrierT3,
        LandFactoryT1, LandFactoryT2, LandFactoryT3, AirFactoryT1, AirFactoryT2, AirFactoryT3,
        NavalFactoryT1, NavalFactoryT2, NavalFactoryT3, QuantumGateway,
        MexT1, MexT2, MexT3, PgenT1, PgenT2, PgenT3, Hydro, MassFabT2, MassFabT3, MassStorage, EnergyStorage,
        PdT1, PdT2, PdT3, AaT1, AaT2, AaT3, TorpedoT1, TorpedoT2, ShieldT2, ShieldT3,
        Tml, Tmd, ArtilleryInstallationT2, ArtilleryInstallationT3, NukeSilo, AntiNuke,
        RadarT1, RadarT2, OmniT3, SonarT1, SonarT2, StealthFieldT2,
        ExpLand, ExpLand2, ExpAir, ExpNaval, ExpArtillery,
    ];

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);
    public static bool IsKnown(string role) => Known.Contains(role);
}

/// <summary>
/// One playable faction, loaded from <c>data/factions/&lt;id&gt;.json</c>. Immutable after loading (like UnitBlueprint).
/// </summary>
public sealed class FactionBlueprint
{
    /// <summary>Lowercase id ("uef"); also the file name and the blueprint id prefix of its units.</summary>
    public string Id = "";
    /// <summary>Display name ("UEF").</summary>
    public string Name = "";
    /// <summary>Blueprint id of the starting commander.</summary>
    public string Commander = "";
    /// <summary>Optional UI accent color "#RRGGBB" ("" = none).</summary>
    public string Color = "";
    public string Description = "";
    /// <summary>Role name (<see cref="FactionRoles"/>) → blueprint id. Missing roles = the faction has no such unit.</summary>
    public Dictionary<string, string> Roles = new();

    /// <summary>Position in <see cref="FactionDb.All"/>.</summary>
    [JsonIgnore] public int Index;
    [JsonIgnore] public int CommanderIndex = -1;
    /// <summary><see cref="Color"/> as 0xRRGGBBAA (0 = no accent color).</summary>
    [JsonIgnore] public uint ColorRgba;
    private Dictionary<string, int> _roleIndices = new(StringComparer.Ordinal);
    /// <summary>Resolved <see cref="Roles"/>: role → blueprint index.</summary>
    [JsonIgnore] public IReadOnlyDictionary<string, int> RoleIndices => _roleIndices;

    /// <summary>Blueprint index of a role, -1 if this faction has none.</summary>
    public int Role(string role) => _roleIndices.TryGetValue(role, out int i) ? i : -1;
    public bool HasRole(string role) => _roleIndices.ContainsKey(role);

    internal void SetRoleIndices(Dictionary<string, int> roles) => _roleIndices = roles;
}

/// <summary>
/// All factions, in deterministic order (file name order = ordinal id order). Immutable after loading; reachable from
/// <see cref="BlueprintDb.Factions"/> (loaded with the blueprints, read from any thread). Ids are case-insensitive.
/// </summary>
public sealed class FactionDb
{
    /// <summary>Faction of an army/slot that doesn't choose one.</summary>
    public const string DefaultFaction = "uef";
    /// <summary>Slot value meaning "pick one", resolved deterministically from the seed by <see cref="Resolve"/>.</summary>
    public const string Random = "random";

    private readonly List<FactionBlueprint> _all = [];
    private readonly Dictionary<string, int> _byId = new(StringComparer.OrdinalIgnoreCase);

    public static FactionDb Empty => new();

    public IReadOnlyList<FactionBlueprint> All => _all;
    public int Count => _all.Count;

    /// <summary>The faction with this id, null if unknown.</summary>
    public FactionBlueprint? Get(string? id) => id != null && _byId.TryGetValue(id, out int i) ? _all[i] : null;

    /// <summary>Blueprint index of a faction's role; -1 if the faction or the role is unknown/missing.</summary>
    public int RoleBlueprint(string? factionId, string role) => Get(factionId)?.Role(role) ?? -1;

    /// <summary>Blueprint index of a faction's commander, -1 if the faction is unknown.</summary>
    public int CommanderOf(string? factionId) => Get(factionId)?.CommanderIndex ?? -1;

    /// <summary>
    /// The concrete faction id for a slot choice: null/"" → <see cref="DefaultFaction"/> (else the first faction),
    /// "random" → one of <see cref="All"/> picked from (seed, army) — the same for every machine and every run.
    /// Unknown ids are returned unchanged (the caller reports them).
    /// </summary>
    public string Resolve(string? choice, int seed, int army)
    {
        if (string.IsNullOrWhiteSpace(choice))
            return Get(DefaultFaction)?.Id ?? (_all.Count > 0 ? _all[0].Id : DefaultFaction);
        if (string.Equals(choice, Random, StringComparison.OrdinalIgnoreCase))
        {
            if (_all.Count == 0) return DefaultFaction;
            var rng = DetRandom.For((ulong)(uint)seed, 0, army, 0xFAC7);
            return _all[rng.Range(0, _all.Count)].Id;
        }
        return Get(choice)?.Id ?? choice;
    }

    /// <summary>Builds a validated db from in-memory factions (tests, tools). See <see cref="LoadDirectory"/>.</summary>
    public static FactionDb Create(BlueprintDb blueprints, IEnumerable<FactionBlueprint> factions)
    {
        var db = new FactionDb();
        foreach (var f in factions.OrderBy(f => f.Id.ToLowerInvariant(), StringComparer.Ordinal)) db.Add(f, blueprints);
        return db;
    }

    /// <summary>
    /// Loads every <c>*.json</c> in the directory (one faction per file, id defaults to the file name) and validates
    /// it against the blueprints: commander and every role must reference existing blueprints, role names must be
    /// known <see cref="FactionRoles"/>, the color must be "#RRGGBB". A missing directory gives an empty db.
    /// </summary>
    public static FactionDb LoadDirectory(string dir, BlueprintDb blueprints)
    {
        var list = new List<FactionBlueprint>();
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
            {
                try
                {
                    var f = JsonSerializer.Deserialize<FactionBlueprint>(File.ReadAllText(file), BlueprintDb.JsonOptions)
                            ?? throw new InvalidDataException("empty file");
                    if (string.IsNullOrEmpty(f.Id)) f.Id = Path.GetFileNameWithoutExtension(file);
                    list.Add(f);
                }
                catch (Exception e) when (e is not InvalidDataException)
                {
                    throw new InvalidDataException($"Error loading faction '{file}': {e.Message}", e);
                }
            }
        }
        return Create(blueprints, list);
    }

    private void Add(FactionBlueprint f, BlueprintDb bps)
    {
        if (string.IsNullOrWhiteSpace(f.Id)) throw new InvalidDataException("Faction without Id");
        if (string.Equals(f.Id, Random, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("'random' is not a valid faction id");
        if (_byId.ContainsKey(f.Id)) throw new InvalidDataException($"Duplicate faction id '{f.Id}'");
        if (string.IsNullOrEmpty(f.Name)) f.Name = f.Id.ToUpperInvariant();
        f.CommanderIndex = bps.IndexOf(f.Commander);
        if (f.CommanderIndex < 0) throw new InvalidDataException($"Faction '{f.Id}': Commander references unknown blueprint '{f.Commander}'");
        var roles = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (role, id) in f.Roles)
        {
            if (!FactionRoles.IsKnown(role))
                throw new InvalidDataException($"Faction '{f.Id}': unknown role '{role}' (add it to FactionRoles and the docs table)");
            int bp = bps.IndexOf(id);
            if (bp < 0) throw new InvalidDataException($"Faction '{f.Id}': role '{role}' references unknown blueprint '{id}'");
            roles[role] = bp;
        }
        f.SetRoleIndices(roles);
        f.ColorRgba = ParseColor(f);
        f.Index = _all.Count;
        _all.Add(f);
        _byId[f.Id] = f.Index;
    }

    private static uint ParseColor(FactionBlueprint f)
    {
        if (string.IsNullOrEmpty(f.Color)) return 0;
        string s = f.Color.TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
            throw new InvalidDataException($"Faction '{f.Id}': Color must be \"#RRGGBB\", got '{f.Color}'");
        return (rgb << 8) | 0xFF;
    }
}
