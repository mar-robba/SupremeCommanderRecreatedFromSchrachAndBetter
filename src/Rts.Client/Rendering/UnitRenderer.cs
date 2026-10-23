using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// Draws units from interpolated snapshots with GPU instancing: one procedural model per blueprint
/// (<see cref="UnitModels"/>), one instanced draw per body/turret mesh, team color per instance.
/// Land units tilt with the terrain, air units bank into turns, ships bob. Turrets follow TurretHeading.
/// Units under construction are drawn as a rising hologram. Also renders the unit shadow map.
/// Meshes dither-fade out when zooming to the strategic view (icons fade in, see <see cref="IconRenderer"/>).
/// </summary>
public sealed unsafe partial class UnitRenderer : IDisposable
{
    private readonly BlueprintDb _db;
    private readonly Terrain? _terrain;
    private readonly Dictionary<int, UnitModel> _models = new();
    private readonly List<UnitModel> _active = new();
    private readonly List<(UnitModel model, bool turret, Matrix4x4 m, Vector4 data)> _holo = new();
    private readonly ShadowMap _shadow;

    /// <summary>Debug: draw one model of every shape near the first start position (CLI --gallery).</summary>
    public static bool Gallery;
    private List<(UnitBlueprint bp, Vector3 pos)>? _gallery;

    // Stats (for the perf overlay).
    public int DrawnUnits { get; private set; }
    public int DrawCalls { get; private set; }

    public UnitRenderer(BlueprintDb db) : this(db, null) { }

    public UnitRenderer(BlueprintDb db, Terrain? terrain)
    {
        _db = db;
        _terrain = terrain;
        _shadow = new ShadowMap();
    }

    /// <summary>Interpolated world position/heading of a unit view.</summary>
    public static (Vector3 pos, float heading) Interpolate(in UnitView cur, SimSnapshot? prev, float alpha)
    {
        if (prev != null && prev.TryGetUnit(cur.Index, cur.Gen, out var pv))
        {
            float dh = Rts.Sim.Core.DMath.AngleDiff(pv.Heading, cur.Heading);
            return (Vector3.Lerp(pv.Pos, cur.Pos, alpha), pv.Heading + dh * alpha);
        }
        return (cur.Pos, cur.Heading);
    }

    public static Matrix4x4 ModelMatrix(Vector3 pos, float heading, Vector3 size)
    {
        // Model forward is +Z; heading h points along (cos h, sin h) in the (x, z) plane.
        var m = Matrix4x4.CreateScale(size) * Matrix4x4.CreateRotationY(MathF.PI * 0.5f - heading) * Matrix4x4.CreateTranslation(pos);
        return Matrix4x4.Transpose(m); // raylib expects column-major
    }

    /// <summary>Mesh visibility vs camera distance: 1 when close, 0 in the strategic view.</summary>
    public static float MeshFade(float camDistance) =>
        1f - Math.Clamp((camDistance - (IconRenderer.HideMeshDistance - 70f)) / 70f, 0f, 1f);

    public UnitModel ModelFor(UnitBlueprint bp)
    {
        if (!_models.TryGetValue(bp.Index, out var m))
        {
            m = UnitModels.Build(bp);
            _models[bp.Index] = m;
        }
        return m;
    }

    private UnitModel Model(int bpIndex) => ModelFor(_db[bpIndex]);

    private void Use(UnitModel m)
    {
        if (m.Body.Count == 0 && (m.Turret == null || m.Turret.Count == 0)) _active.Add(m);
    }

    public void Draw(SimSnapshot cur, SimSnapshot? prev, float alpha, Color[] armyColors)
    {
        var fv = FrameView.Capture();
        float fade = MeshFade(Atmosphere.CameraDistance);
        DrawnUnits = 0;
        DrawCalls = 0;
        _submerged.Clear();
        if (fade <= 0.001f) return;
        float time = Atmosphere.Time;

        foreach (var m in _active) { m.Body.Clear(); m.Turret?.Clear(); }
        _active.Clear();
        _holo.Clear();

        Span<Vector4> teamCols = stackalloc Vector4[Math.Max(1, armyColors.Length)];
        for (int a = 0; a < armyColors.Length; a++)
            teamCols[a] = new Vector4(armyColors[a].R / 255f, armyColors[a].G / 255f, armyColors[a].B / 255f, 0);

        for (int k = 0; k < cur.UnitCount; k++)
        {
            ref var v = ref cur.Units[k];
            var model = Model(v.Bp);
            Vector3 pos;
            float heading, turret, turn = 0;
            if (prev != null && prev.TryGetUnit(v.Index, v.Gen, out var pv))
            {
                pos = Vector3.Lerp(pv.Pos, v.Pos, alpha);
                turn = Rts.Sim.Core.DMath.AngleDiff(pv.Heading, v.Heading);
                heading = pv.Heading + turn * alpha;
                turret = pv.TurretHeading + Rts.Sim.Core.DMath.AngleDiff(pv.TurretHeading, v.TurretHeading) * alpha;
            }
            else
            {
                pos = v.Pos;
                heading = v.Heading;
                turret = v.TurretHeading;
            }
            float midY = (model.MinY + model.MaxY) * 0.5f;
            if (!fv.SphereVisible(pos + new Vector3(0, midY, 0), model.Radius)) continue;
            DrawnUnits++;
            var col = (uint)v.Army < (uint)teamCols.Length ? teamCols[v.Army] : new Vector4(0.7f, 0.7f, 0.7f, 0);
            bool building = (v.Flags & UnitFlags.UnderConstruction) != 0;
            if (!building && IsSubmerged(model, pos)) { _submerged.Add((model, v.Index, pos, heading, turret, col)); continue; } // UnitRenderer.Submerged.cs
            if (!building && (v.Status & UnitStatus.Cloaked) != 0) continue; // translucent shimmer in DrawStatus (UnitRenderer.Status.cs)
            AddUnit(model, v.Index, pos, heading, turret, turn, col, building, building ? v.BuildFrac : 1f, time);
            // In-place upgrade: the target model grows around the structure as a construction hologram.
            if (v.UpgradeBp >= 0) AddUnit(Model(v.UpgradeBp), v.Index, pos, heading, turret, turn, col, true, MathF.Max(0.02f, v.UpgradeFrac), time);
        }

        AddCargo(cur, prev, alpha, teamCols, fv, time); // units inside transports / docked aircraft: UnitRenderer.Cargo.cs

        // Fog of war: last-seen enemy structures ("ghosts"), greyed team color; the fog dims them further.
        for (int k = 0; k < cur.GhostCount; k++)
        {
            ref var g = ref cur.Ghosts[k];
            var model = Model(g.Bp);
            if (!fv.SphereVisible(g.Pos + new Vector3(0, (model.MinY + model.MaxY) * 0.5f, 0), model.Radius)) continue;
            var tc = (uint)g.Army < (uint)teamCols.Length ? teamCols[g.Army] : new Vector4(0.7f, 0.7f, 0.7f, 0);
            AddUnit(model, -1 - k, g.Pos, g.Heading, g.Heading, 0, Vector4.Lerp(tc, new Vector4(0.45f, 0.45f, 0.45f, 0), 0.5f), false, 1f, time);
        }

        if (Gallery) AddGallery(time, teamCols);

        FrameProfiler.Sub("units:gather");
        // 1) Shadow pass (units only).
        if (_shadow.Supported)
        {
            var target = fv.CamPos + fv.CamForward * Atmosphere.CameraDistance;
            _shadow.Fit(fv, target, Atmosphere.CameraDistance);
            _shadow.Begin();
            var dp = Shaders.UnitDepth;
            dp.Use();
            dp.SetMatrix("viewProj", _shadow.LightVPGl);
            foreach (var m in _active)
            {
                m.Body.Draw();
                m.Turret?.Draw();
            }
            _shadow.End();
        }
        FrameProfiler.Sub("units:shadow");

        // 2) Opaque lit pass.
        var sp = Shaders.UnitLit;
        sp.Use();
        sp.SetMatrix("viewProj", fv.ViewProjGl);
        Atmosphere.Apply(sp, fv);
        sp.Set("fade", fade);
        ShadowMap.Bind(sp);
        FogRenderer.Bind(sp);
        sp.Use();
        foreach (var m in _active)
        {
            if (_shadow.Supported) { m.Body.DrawAgain(); m.Turret?.DrawAgain(); }
            else { m.Body.Draw(); m.Turret?.Draw(); }
            DrawCalls += m.Turret != null ? 2 : 1;
        }

        // 3) Construction holograms (translucent, no depth writes).
        if (_holo.Count > 0)
        {
            foreach (var m in _active) { m.Body.Clear(); m.Turret?.Clear(); }
            var used = new HashSet<InstancedMesh>();
            foreach (var (model, isTurret, mtx, data) in _holo)
            {
                var im = isTurret ? model.Turret! : model.Body;
                if (!used.Contains(im)) { im.Clear(); used.Add(im); }
                im.Add(mtx, data);
            }
            var hp = Shaders.UnitHolo;
            hp.Use();
            hp.SetMatrix("viewProj", fv.ViewProjGl);
            Atmosphere.Apply(hp, fv);
            hp.Set("fade", fade);
            hp.Use();
            Rlgl.DisableDepthMask();
            foreach (var im in used) { im.Draw(); DrawCalls++; }
            Rlgl.EnableDepthMask();
            foreach (var im in used) im.Clear();
        }
        Rlgl.DisableShader();
    }

    private void AddUnit(UnitModel model, int index, Vector3 pos, float heading, float turretYaw, float turnPerTick,
                         Vector4 teamCol, bool building, float buildFrac, float time)
    {
        var fwd = new Vector3(MathF.Cos(heading), 0, MathF.Sin(heading));
        var up = Vector3.UnitY;
        if (model.Air)
        {
            pos.Y += MathF.Sin(time * 1.7f + index * 0.37f) * 0.08f;
            float bank = Math.Clamp(turnPerTick * 4.5f, -0.75f, 0.75f);
            var side0 = Vector3.Cross(up, fwd);
            up = Vector3.Normalize(up * MathF.Cos(bank) - side0 * MathF.Sin(bank));
        }
        else if (model.Naval)
        {
            float t = time * 1.3f + index * 0.61f;
            pos.Y += MathF.Sin(t) * 0.05f;
            var side0 = Vector3.Cross(up, fwd);
            up = Vector3.Normalize(up + side0 * (MathF.Sin(t * 0.8f) * 0.03f) + fwd * (MathF.Sin(t * 1.1f) * 0.02f));
        }
        else if (!model.Structure && _terrain != null)
        {
            var n = _terrain.GetNormal(pos.X, pos.Z);
            if (pos.Y > _terrain.WaterLevel + 0.05f || model.Hover == false) up = Vector3.Normalize(Vector3.Lerp(Vector3.UnitY, n, 0.85f));
        }
        var z = Vector3.Normalize(fwd - up * Vector3.Dot(fwd, up));
        var x = Vector3.Cross(up, z);
        var m = new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, pos.X, pos.Y, pos.Z, 1);

        float height = model.MaxY - model.MinY;
        float cut = model.MinY + Math.Clamp(buildFrac, 0, 1) * height;
        if (building) _holo.Add((model, false, m, new Vector4(teamCol.X, teamCol.Y, teamCol.Z, cut)));
        else
        {
            Use(model);
            model.Body.Add(m, teamCol);
        }

        if (model.Turret == null) return;
        float yaw = model.TurretMode == TurretMode.Spin ? (building ? heading : time * model.SpinSpeed + index * 0.7f) : (building ? heading : turretYaw);
        var tf = new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw));
        var tz = tf - up * Vector3.Dot(tf, up);
        tz = tz.LengthSquared() > 1e-6f ? Vector3.Normalize(tz) : z;
        var tx = Vector3.Cross(up, tz);
        foreach (var pivot in model.TurretPivots)
        {
            var wp = Vector3.Transform(pivot, m);
            var tm = new Matrix4x4(tx.X, tx.Y, tx.Z, 0, up.X, up.Y, up.Z, 0, tz.X, tz.Y, tz.Z, 0, wp.X, wp.Y, wp.Z, 1);
            if (building) _holo.Add((model, true, tm, new Vector4(teamCol.X, teamCol.Y, teamCol.Z, cut - pivot.Y)));
            else model.Turret.Add(tm, teamCol);
        }
    }

    private void AddGallery(float time, ReadOnlySpan<Vector4> teamCols)
    {
        if (_terrain == null) return;
        if (_gallery == null)
        {
            _gallery = new();
            var shapes = new (string shape, Vector3 size, UnitCategory cats)[]
            {
                ("commander", new(2, 2.6f, 2), UnitCategory.Commander), ("engineer", new(0.8f, 0.6f, 1), 0),
                ("tank", new(1, 0.7f, 1.4f), 0), ("bot", new(0.9f, 1.2f, 0.8f), 0), ("artillery", new(1, 0.7f, 1.4f), 0),
                ("aa", new(1, 0.8f, 1.3f), UnitCategory.AntiAir), ("scout", new(0.6f, 0.5f, 0.9f), 0),
                ("fighter", new(1.4f, 0.5f, 1.6f), 0), ("bomber", new(1.8f, 0.6f, 2), 0), ("gunship", new(1.6f, 0.7f, 1.8f), 0),
                ("transport", new(2.2f, 0.9f, 3.2f), 0), ("factory", new(6, 2.2f, 6), 0), ("factory", new(6, 1.8f, 6), UnitCategory.Air),
                ("mex", new(1.8f, 1.2f, 1.8f), 0), ("pgen", new(1.8f, 1.6f, 1.8f), 0), ("pgen", new(3.6f, 2.4f, 3.6f), UnitCategory.HydroCarbon),
                ("storage", new(1.8f, 1.0f, 1.8f), UnitCategory.MassStorage), ("storage", new(1.8f, 1.4f, 1.8f), UnitCategory.EnergyStorage),
                ("defense", new(1.6f, 1.6f, 1.6f), 0), ("defense", new(1.6f, 1.4f, 1.6f), UnitCategory.AntiAir), ("radar", new(1.4f, 3, 1.4f), 0),
                ("wall", new(1, 1, 1), 0), ("frigate", new(2.2f, 2.2f, 7), 0), ("destroyer", new(3, 2.6f, 10), 0), ("cruiser", new(3.4f, 3, 12), 0),
                ("sub", new(1.6f, 1.2f, 6), 0), ("experimental", new(9, 7, 10), 0), ("box", new(1, 1, 1), 0),
                ("battleship", new(5, 4.2f, 18), 0), ("sonar", new(1.4f, 2.2f, 1.4f), UnitCategory.Sonar), ("sonar", new(1.7f, 3, 1.7f), UnitCategory.Sonar | UnitCategory.Tech2),
                ("torpedo_launcher", new(1.6f, 1, 1.6f), UnitCategory.AntiNaval), ("torpedo_bomber", new(1.9f, 0.6f, 2.2f), UnitCategory.Air | UnitCategory.AntiNaval),
                ("naval_factory", new(10, 3.4f, 10), UnitCategory.Naval | UnitCategory.Tech3),
            };
            var origin = _terrain.StartPositions[0] + new Vector2(22, -10);
            Console.WriteLine($"Gallery origin: {origin}");
            int i = 0;
            foreach (var (shape, size, cats) in shapes)
            {
                bool air = shape is "fighter" or "bomber" or "gunship" or "transport";
                bool structure = shape is "factory" or "mex" or "pgen" or "storage" or "defense" or "radar" or "wall";
                var bp = new UnitBlueprint
                {
                    Id = "gallery_" + i, Index = -1000 - i,
                    Visual = new VisualBlueprint { Shape = shape, Size = size },
                    CategoryFlags = cats | (structure ? UnitCategory.Structure : UnitCategory.Mobile) | (air ? UnitCategory.Air : 0),
                    Weapons = shape is "engineer" or "factory" or "mex" or "pgen" or "storage" or "radar" or "wall" ? [] : [new WeaponBlueprint()],
                };
                if (air) bp.Movement.Layer = MotionLayer.Air;
                int col = i % 7, row = i / 7;
                var p = origin + new Vector2(col * 9f, row * 12f);
                float y = _terrain.GetHeight(p) + (air ? 4f : 0f);
                if (shape is "frigate" or "destroyer" or "cruiser" or "sub") y = MathF.Max(y, _terrain.WaterLevel);
                _gallery.Add((bp, new Vector3(p.X, y, p.Y)));
                i++;
            }
        }
        int n = 0;
        foreach (var (bp, pos) in _gallery)
        {
            var model = ModelFor(bp);
            float h = MathF.PI * 0.5f - 0.6f;
            bool holo = n == 2;
            AddUnit(model, n, pos, h, h + MathF.Sin(time) * 0.8f, 0, teamCols[n % teamCols.Length], holo, holo ? 0.25f + 0.5f * (MathF.Sin(time) * 0.5f + 0.5f) : 1f, time);
            n++;
        }
    }

    public void Dispose()
    {
        foreach (var m in _models.Values)
        {
            m.Body.Dispose();
            m.Turret?.Dispose();
        }
        _models.Clear();
        _shadow.Dispose();
    }
}
