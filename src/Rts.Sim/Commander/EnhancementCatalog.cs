using System.Text.Json;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Commander;

/// <summary>
/// Enhancement variants. For every blueprint with <see cref="UnitBlueprint.Enhancements"/> (the "base", e.g. the
/// ACU), every combination of one-or-no enhancement per <see cref="EnhancementSlot"/> becomes a generated blueprint
/// (id "base+Enh1+Enh2") whose stats already include the effects (numbers add up along the prerequisite chain).
/// Installing / removing an enhancement swaps the unit's blueprint in place (same slot and handle), so every system
/// that reads blueprint data (weapons, build lists, build rate, production, shields, silos, intel) just works.
/// Variants are appended after all file blueprints (file indices stay stable), keep the base's Name/Categories/Visual,
/// and are hidden from spawn lists (<see cref="UnitBlueprint.IsVariant"/>).
/// Combination index: mixed radix over the slots, digit 0 = empty, 1.. = the slot's enhancements in array order.
/// </summary>
public static class EnhancementCatalog
{
    /// <summary>Start of BlueprintDb.FinalizeDb: resolves prerequisites and adds the variant blueprints (once).</summary>
    public static void GenerateVariants(BlueprintDb db)
    {
        int n = db.Count;
        for (int b = 0; b < n; b++)
            if (db[b].EnhancementBase >= 0) return; // already generated (FinalizeDb called again)
        var bases = new List<UnitBlueprint>();
        for (int b = 0; b < n; b++)
            if (db[b].Enhancements.Length > 0) bases.Add(db[b]);
        foreach (var bp in bases) Generate(db, bp);
    }

    private static void Generate(BlueprintDb db, UnitBlueprint bp)
    {
        var enh = bp.Enhancements;
        for (int e = 0; e < enh.Length; e++)
        {
            var x = enh[e];
            if (string.IsNullOrEmpty(x.Id)) throw new InvalidDataException($"Blueprint '{bp.Id}': enhancement without Id");
            for (int k = 0; k < e; k++)
                if (string.Equals(enh[k].Id, x.Id, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Blueprint '{bp.Id}': duplicate enhancement '{x.Id}'");
            if (x.BuildTime <= 0) x.BuildTime = 1;
            x.PrerequisiteIndex = -1;
            if (!string.IsNullOrEmpty(x.Prerequisite))
            {
                int p = Find(bp, x.Prerequisite);
                if (p < 0 || p == e) throw new InvalidDataException($"Blueprint '{bp.Id}': enhancement '{x.Id}' has unknown prerequisite '{x.Prerequisite}'");
                if (enh[p].Slot != x.Slot) throw new InvalidDataException($"Blueprint '{bp.Id}': enhancement '{x.Id}' prerequisite must be in the same slot");
                x.PrerequisiteIndex = p;
            }
        }
        for (int e = 0; e < enh.Length; e++)
        {
            // Prerequisite chains must end (no cycles).
            int steps = 0;
            for (int p = enh[e].PrerequisiteIndex; p >= 0; p = enh[p].PrerequisiteIndex)
                if (++steps > enh.Length) throw new InvalidDataException($"Blueprint '{bp.Id}': prerequisite cycle at '{enh[e].Id}'");
        }

        int total = 1;
        for (int s = 0; s < EnhancementSlots.Count; s++) total *= Radix(bp, s);
        bp.EnhancementBase = bp.Index;
        bp.Installed = [-1, -1, -1];
        bp.EnhancementVariants = new int[total];
        bp.EnhancementVariants[0] = bp.Index;
        string json = JsonSerializer.Serialize(bp, BlueprintDb.JsonOptions);
        var installed = new int[EnhancementSlots.Count];
        for (int code = 1; code < total; code++)
        {
            Decode(bp, code, installed);
            var v = JsonSerializer.Deserialize<UnitBlueprint>(json, BlueprintDb.JsonOptions)!;
            var ids = new List<string> { bp.Id };
            for (int s = 0; s < installed.Length; s++)
                if (installed[s] >= 0) ids.Add(enh[installed[s]].Id);
            v.Id = string.Join('+', ids);
            v.BuiltBy = [];
            v.UpgradesTo = "";
            v.Enhancements = enh;
            for (int s = 0; s < installed.Length; s++)
            {
                if (installed[s] < 0) continue;
                var chain = new List<int>();
                for (int e = installed[s]; e >= 0; e = enh[e].PrerequisiteIndex) chain.Add(e);
                chain.Reverse(); // prerequisite effects first
                foreach (int e in chain) ApplyEffects(bp, v, enh[e]);
            }
            if (v.Weapons.Length > UnitStore.MaxWeapons)
                throw new InvalidDataException($"Blueprint '{v.Id}': more than {UnitStore.MaxWeapons} weapons");
            db.Add(v);
            v.EnhancementBase = bp.Index;
            v.Installed = (int[])installed.Clone();
            bp.EnhancementVariants[code] = v.Index;
        }
    }

    private static void ApplyEffects(UnitBlueprint bp, UnitBlueprint v, EnhancementBlueprint x)
    {
        var fx = x.Effects;
        foreach (var m in fx.Weapons)
        {
            if ((uint)m.Weapon >= (uint)bp.Weapons.Length)
                throw new InvalidDataException($"Blueprint '{bp.Id}': enhancement '{x.Id}' modifies unknown weapon {m.Weapon}");
            var wb = v.Weapons[m.Weapon];
            wb.Damage = wb.Damage * m.DamageMult + m.DamageAdd;
            wb.Range += m.RangeAdd;
            wb.DamageRadius += m.DamageRadiusAdd;
            wb.RateOfFire *= m.RateOfFireMult;
        }
        int offset = v.Weapons.Length;
        if (fx.AddWeapons.Length > 0) v.Weapons = [.. v.Weapons, .. fx.AddWeapons.Select(Clone)];
        if (fx.Silo != null)
        {
            var silo = Clone(fx.Silo);
            silo.WeaponIndex += offset;
            v.Silo = silo;
        }
        if (fx.Shield != null) v.Shield = Clone(fx.Shield);
        if (fx.Teleport != null) v.Teleport = Clone(fx.Teleport);
        v.MaxHealth += fx.MaxHealthAdd;
        v.RegenRate += fx.RegenRateAdd;
        v.Economy.BuildRate += fx.BuildRateAdd;
        v.Economy.ProductionMass += fx.ProductionMassAdd;
        v.Economy.ProductionEnergy += fx.ProductionEnergyAdd;
        v.Economy.MaintenanceEnergy += fx.MaintenanceEnergyAdd;
        v.Intel.VisionRadius += fx.VisionRadiusAdd;
        v.Intel.RadarStealth |= fx.RadarStealth; // stealth/cloak: OR-ed along the chain, field radius replaces
        v.Intel.SonarStealth |= fx.SonarStealth;
        v.Intel.Cloak |= fx.Cloak;
        if (fx.StealthFieldRadius > 0) v.Intel.StealthFieldRadius = fx.StealthFieldRadius;
        if (fx.BuildListFrom.Length > 0) v.BuildListFrom = [.. v.BuildListFrom, .. fx.BuildListFrom];
    }

    private static T Clone<T>(T x) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(x, BlueprintDb.JsonOptions), BlueprintDb.JsonOptions)!;

    /// <summary>End of BlueprintDb.FinalizeDb (after BuiltBy): merges <see cref="UnitBlueprint.BuildListFrom"/> build
    /// lists into BuildIndices; variants start from their base's final list.</summary>
    public static void ResolveBuildLists(BlueprintDb db)
    {
        // Base blueprints: a few passes so chains (A from B from C) settle.
        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;
            foreach (var bp in db.All)
            {
                if (bp.IsVariant || bp.BuildListFrom.Length == 0) continue;
                changed |= MergeFrom(db, bp, bp.BuildListFrom);
            }
            if (!changed) break;
        }
        foreach (var v in db.All)
        {
            if (!v.IsVariant) continue;
            v.BuildIndices = (int[])db[v.EnhancementBase].BuildIndices.Clone();
            MergeFrom(db, v, v.BuildListFrom);
        }
    }

    private static bool MergeFrom(BlueprintDb db, UnitBlueprint bp, string[] from)
    {
        bool changed = false;
        foreach (var id in from)
        {
            if (!db.TryGet(id, out var src) || src == bp) continue;
            List<int>? add = null;
            foreach (int b in src.BuildIndices)
            {
                if (b == bp.Index || b == bp.EnhancementBase || Array.IndexOf(bp.BuildIndices, b) >= 0) continue;
                if (add != null && add.Contains(b)) continue;
                (add ??= new List<int>()).Add(b);
            }
            if (add == null) continue;
            bp.BuildIndices = [.. bp.BuildIndices, .. add];
            changed = true;
        }
        return changed;
    }

    // ------------------------------------------------------------------ lookups (immutable data, any thread)

    public static int Find(UnitBlueprint bp, string id)
    {
        var enh = bp.Enhancements;
        for (int e = 0; e < enh.Length; e++)
            if (string.Equals(enh[e].Id, id, StringComparison.OrdinalIgnoreCase)) return e;
        return -1;
    }

    private static int Radix(UnitBlueprint bp, int slot)
    {
        int r = 1;
        foreach (var x in bp.Enhancements) if ((int)x.Slot == slot) r++;
        return r;
    }

    private static int Digit(UnitBlueprint bp, int slot, int enh)
    {
        if (enh < 0) return 0;
        int d = 1;
        for (int e = 0; e < enh; e++) if ((int)bp.Enhancements[e].Slot == slot) d++;
        return d;
    }

    private static void Decode(UnitBlueprint bp, int code, int[] installed)
    {
        for (int s = 0; s < EnhancementSlots.Count; s++)
        {
            int r = Radix(bp, s);
            int d = code % r;
            code /= r;
            installed[s] = -1;
            if (d == 0) continue;
            int k = 0;
            for (int e = 0; e < bp.Enhancements.Length; e++)
            {
                if ((int)bp.Enhancements[e].Slot != s) continue;
                if (++k == d) { installed[s] = e; break; }
            }
        }
    }

    /// <summary>Blueprint index of the base's variant with these installed enhancements (per slot, -1 = empty).</summary>
    public static int VariantIndex(BlueprintDb db, UnitBlueprint any, ReadOnlySpan<int> installed)
    {
        if (!any.IsEnhanceable) return -1;
        var bp = db[any.EnhancementBase];
        int code = 0, mul = 1;
        for (int s = 0; s < EnhancementSlots.Count; s++)
        {
            code += Digit(bp, s, installed[s]) * mul;
            mul *= Radix(bp, s);
        }
        return bp.EnhancementVariants[code];
    }

    /// <summary>The variant of <paramref name="cur"/> with <paramref name="slot"/> set to enhancement <paramref name="enh"/> (-1 = empty).</summary>
    public static int WithSlot(BlueprintDb db, UnitBlueprint cur, EnhancementSlot slot, int enh)
    {
        if (!cur.IsEnhanceable) return -1;
        Span<int> inst = stackalloc int[EnhancementSlots.Count];
        cur.Installed.CopyTo(inst);
        inst[(int)slot] = enh;
        return VariantIndex(db, cur, inst);
    }

    /// <summary>Is enhancement <paramref name="e"/> installed on <paramref name="cur"/> (directly or as a prerequisite of what is installed)?</summary>
    public static bool HasInstalled(UnitBlueprint cur, int e)
    {
        if (!cur.IsEnhanceable || (uint)e >= (uint)cur.Enhancements.Length) return false;
        for (int x = cur.Installed[(int)cur.Enhancements[e].Slot]; x >= 0; x = cur.Enhancements[x].PrerequisiteIndex)
            if (x == e) return true;
        return false;
    }

    /// <summary>Can <paramref name="cur"/> start installing enhancement <paramref name="e"/>? Its slot must be empty
    /// (no prerequisite) or hold exactly its prerequisite.</summary>
    public static bool CanInstall(UnitBlueprint cur, int e)
    {
        if (!cur.IsEnhanceable || (uint)e >= (uint)cur.Enhancements.Length) return false;
        var x = cur.Enhancements[e];
        int inSlot = cur.Installed[(int)x.Slot];
        return inSlot == x.PrerequisiteIndex;
    }

    /// <summary>The enhancement that turns blueprint <paramref name="from"/> into <paramref name="to"/> (one slot
    /// gains an enhancement), or -1 if they are not such a pair.</summary>
    public static int Added(BlueprintDb db, int from, int to)
    {
        if ((uint)from >= (uint)db.Count || (uint)to >= (uint)db.Count) return -1;
        var a = db[from];
        var b = db[to];
        if (!a.IsEnhanceable || a.EnhancementBase != b.EnhancementBase) return -1;
        int found = -1;
        for (int s = 0; s < EnhancementSlots.Count; s++)
        {
            if (a.Installed[s] == b.Installed[s]) continue;
            if (found >= 0 || b.Installed[s] < 0) return -1;
            found = b.Installed[s];
        }
        return found;
    }

    /// <summary>Cost of "upgrading" blueprint <paramref name="from"/> into enhancement variant <paramref name="to"/>.</summary>
    public static bool TryGetCost(BlueprintDb db, int from, int to, out (float Mass, float Energy, float Time) cost)
    {
        int e = Added(db, from, to);
        if (e < 0) { cost = default; return false; }
        var x = db[from].Enhancements[e];
        cost = (x.BuildCostMass, x.BuildCostEnergy, x.BuildTime);
        return true;
    }
}
