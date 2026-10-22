using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.World;



/// <summary>Diagnostics for AI matches: cumulative mass each army has put into construction and upgrades, by what
/// was built (mass = build progress x blueprint cost, so units that die unfinished count what they got). Printed in
/// the status line as <c>spent=army/eng/mex/mexUp/fac/facUp/power/store/def/other/enhancements</c> (k mass).</summary>
public sealed class SpendTracker
{
    public const int Army = 0, Eng = 1, Mex = 2, MexUp = 3, Fac = 4, FacUp = 5, Power = 6, Store = 7, Def = 8, Other = 9, Enh = 10, Count = 11;
    private readonly float[,] _spent;
    private int[] _gen = [];
    private float[] _built = [], _upg = [], _upgCost = [];
    private int[] _upgTarget = [];

    public SpendTracker(int armies) => _spent = new float[armies, Count];

    public void Scan(SimWorld w)
    {
        var u = w.Units;
        if (_gen.Length != u.Capacity)
        {
            _gen = new int[u.Capacity]; Array.Fill(_gen, -1);
            _built = new float[u.Capacity]; _upg = new float[u.Capacity]; _upgCost = new float[u.Capacity]; _upgTarget = new int[u.Capacity];
        }
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            int a = u.Army[i];
            if ((uint)a >= (uint)_spent.GetLength(0)) continue;
            if (_gen[i] != u.Gen[i]) { _gen[i] = u.Gen[i]; _built[i] = 0; _upg[i] = 0; _upgTarget[i] = -1; }
            var bp = w.BpOf(i);
            if (bp.Has(UnitCategory.Commander) && _built[i] == 0) _built[i] = bp.Economy.BuildCostMass; // starting units are free
            float built = u.IsComplete(i) ? bp.Economy.BuildCostMass : u.BuildFraction[i] * bp.Economy.BuildCostMass;
            if (_upgTarget[i] >= 0 && _upgTarget[i] == bp.Index)
            {
                // The upgrade finished: the structure now has the target blueprint.
                _spent[a, Cat(w.Blueprints[_upgTarget[i]], true)] += MathF.Max(0, _upgCost[i] - _upg[i]);
                _upgTarget[i] = -1; _upg[i] = 0; _built[i] = built;
            }
            if (built > _built[i]) { _spent[a, Cat(bp, false)] += built - _built[i]; _built[i] = built; }
            ref var up = ref u.Upgrade[i];
            if (up.Active && (uint)up.TargetBp < (uint)w.Blueprints.Count)
            {
                if (_upgTarget[i] != up.TargetBp) { _upgTarget[i] = up.TargetBp; _upg[i] = 0; _upgCost[i] = Rts.Sim.Systems.UpgradeSystem.Cost(w, i, up.TargetBp).Mass; }
                float done = up.Fraction * _upgCost[i];
                if (done > _upg[i]) { _spent[a, Cat(w.Blueprints[up.TargetBp], true)] += done - _upg[i]; _upg[i] = done; }
            }
            else if (!up.Active && _upgTarget[i] >= 0 && _upgTarget[i] != bp.Index) { _upgTarget[i] = -1; _upg[i] = 0; } // cancelled
        }
    }

    private static int Cat(UnitBlueprint bp, bool upgrade)
    {
        if (!bp.IsStructure) return upgrade ? Enh : bp.Has(UnitCategory.Engineer) || bp.Has(UnitCategory.Commander) ? Eng : Army;
        if (bp.Has(UnitCategory.MassExtractor)) return upgrade ? MexUp : Mex;
        if (bp.Has(UnitCategory.Factory)) return upgrade ? FacUp : Fac;
        if (bp.Has(UnitCategory.PowerGenerator)) return Power;
        if (bp.Has(UnitCategory.MassStorage) || bp.Has(UnitCategory.EnergyStorage)) return Store;
        if (bp.Has(UnitCategory.Defense) || bp.Has(UnitCategory.Shield)) return Def;
        return Other;
    }

    public string Format(int army)
    {
        var sb = new System.Text.StringBuilder(" spent=");
        for (int c = 0; c < Count; c++)
        {
            if (c > 0) sb.Append('/');
            sb.Append((_spent[army, c] / 1000f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
