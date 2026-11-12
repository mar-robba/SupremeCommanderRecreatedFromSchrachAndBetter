using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Mode = Rts.Client.Input.SelectionController.CommandMode;

namespace Rts.Client.UI;

/// <summary>
/// Order buttons with SupCom-like hotkeys: Stop (S), Move (M), Attack-move (A), Patrol (P), Guard/Assist (G),
/// Self-destruct (Ctrl+K, asks for confirmation), and when they apply: Capture (E, engineers / commander), Pause (H,
/// pausable units), Dive / Surface (D, submarines), Overcharge (O, commanders), Teleport (J, commanders with Personal Teleporter) and the transport orders (<see cref="TransportInput"/>): Unload /
/// Launch (U), Transport-to (E), Ferry (F), Dock (D); extra rows appear for them. E and D are shared
/// (<see cref="SharedHotkeys"/>): the Capture button shows no key while transports are selected (E = transport-to).
/// Hotkeys themselves are handled by SelectionController / UnitToggles / TransportInput.
/// </summary>
public sealed class OrdersPanel
{
    private readonly GameContext _ctx;
    private readonly SelectionController _sel;

    private enum Btn { Stop, Move, AttackMove, Patrol, Guard, SelfDestruct, Launch, Capture, Pause, Dive, Unload, TransportTo, Ferry, Dock, FireState, Overcharge, Teleport }

    public OrdersPanel(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
    }

    private readonly List<(Btn id, string label, string key, bool enabled, bool active)> _buttons = new();
    private int _cols = 3;

    /// <summary>Button size and gap for the current UI scale.</summary>
    private static float ButtonW => Ui.P(48);
    private static float Pad => Ui.P(6);

    /// <summary>Minimum panel width (3 columns).</summary>
    public static float Width => 3 * ButtonW + 4 * Pad;

    /// <summary>Builds this frame's buttons and returns the panel width: at most 3 rows, so a big selection (loaded
    /// transports, subs, launchers…) gets extra columns instead of squashed buttons.</summary>
    public float Refresh(SimSnapshot snap)
    {
        BuildButtons(snap);
        _cols = Math.Max(3, (_buttons.Count + 2) / 3);
        return _cols * ButtonW + (_cols + 1) * Pad;
    }

    private void BuildButtons(SimSnapshot snap)
    {
        _buttons.Clear();
        bool anyMobile = false, anyFactory = false, anyEngineer = false, anyLauncher = StrategicOrders.AnyLauncher(_ctx, _sel);
        foreach (var h in _sel.Selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v)) continue;
            var bp = _ctx.Db[v.Bp];
            anyMobile |= bp.IsMobile;
            anyFactory |= bp.Has(UnitCategory.Factory);
            anyEngineer |= bp.Has(UnitCategory.Engineer);
        }
        bool canMove = anyMobile || anyFactory;
        var b = _buttons;
        b.Add((Btn.Stop, "Stop", Hotkeys.Stop.Label, true, false));
        b.Add(anyLauncher && !canMove ? (Btn.Launch, "Launch", Hotkeys.Launch.Label, true, _sel.Mode == Mode.Launch) : (Btn.Move, "Move", Hotkeys.Move.Label, canMove, _sel.Mode == Mode.Move));
        b.Add((Btn.AttackMove, "Attack", Hotkeys.AttackMove.Label, canMove, _sel.Mode == Mode.AttackMove));
        b.Add((Btn.Patrol, "Patrol", Hotkeys.Patrol.Label, canMove, _sel.Mode == Mode.Patrol));
        b.Add((Btn.Guard, anyEngineer || anyFactory ? "Assist" : "Guard", Hotkeys.Guard.Label, canMove, _sel.Mode == Mode.Guard));
        if (anyLauncher && canMove) b.Add((Btn.Launch, "Launch", Hotkeys.Launch.Label, true, _sel.Mode == Mode.Launch));
        _fire = UnitToggles.FireStateOf(_ctx, _sel.Selected);
        if (_fire.Any) b.Add((Btn.FireState, _fire.Mixed ? "Mixed" : UnitToggles.FireStateShort(_fire.State), Hotkeys.FireState.Label, true, !_fire.Mixed && _fire.State != FireState.FireAtWill));
        if (UnitToggles.AnyCapturer(_ctx, _sel.Selected))
        {
            // E is transport-to while transports are selected (SharedHotkeys): no key on the Capture button then.
            string key = !Hotkeys.SameBinding(Hotkeys.Capture, Hotkeys.TransportTo) || UnitToggles.EKey(_ctx, _sel.Selected) == SharedHotkeys.EAction.Capture ? UnitToggles.CaptureKeyName : "";
            b.Add((Btn.Capture, "Capture", key, true, _sel.Mode == Mode.Capture));
        }
        _pause = UnitToggles.PauseState(_ctx, _sel.Selected);
        if (_pause.Any) b.Add((Btn.Pause, _pause.AllOn ? "Resume" : "Pause", UnitToggles.PauseKeyName, true, _pause.AllOn));
        _dive = UnitToggles.DiveState(_ctx, _sel.Selected);
        if (_dive.Any) b.Add((Btn.Dive, _dive.AllOn ? "Surface" : "Dive", UnitToggles.DiveKeyName, true, false));
        _oc = UnitToggles.OverchargeState(_ctx, _sel.Selected); // lit while armed (OverchargeButton)
        if (_oc.Any) b.Add((Btn.Overcharge, "OC", UnitToggles.OverchargeKeyName, true, _oc.On > 0));
        _tp = TeleportInput.State(_ctx, _sel.Selected);
        if (_tp.Any) b.Add((Btn.Teleport, "TP", Hotkeys.Teleport.Label, true, _sel.Mode == Mode.Teleport));
        var tb = TransportInput.ButtonsFor(CargoSelection.Of(_ctx, _sel.Selected, snap));
        if ((tb & TransportButtons.Unload) != 0) b.Add((Btn.Unload, "Unload", Hotkeys.Unload.Label, true, _sel.Mode == Mode.Unload));
        if ((tb & TransportButtons.Launch) != 0) b.Add((Btn.Unload, "Launch", Hotkeys.Unload.Label, true, _sel.Mode == Mode.Unload));
        if ((tb & TransportButtons.TransportTo) != 0) b.Add((Btn.TransportTo, "Transport", Hotkeys.TransportTo.Label, true, _sel.Mode == Mode.TransportTo));
        if ((tb & TransportButtons.Ferry) != 0) b.Add((Btn.Ferry, "Ferry", Hotkeys.Ferry.Label, true, _sel.Mode == Mode.Ferry));
        if ((tb & TransportButtons.Dock) != 0) b.Add((Btn.Dock, "Dock", Hotkeys.Dock.Label, true, false));
        b.Add((Btn.SelfDestruct, "Destruct", "^K", true, _sel.PendingSelfDestruct != null));
    }

    public void Draw(Rectangle r, SimSnapshot snap)
    {
        Ui.Panel(r);
        float y = Ui.PanelTitle(r, "Orders");
        if (_buttons.Count == 0) Refresh(snap);

        float pad = Pad;
        int cols = _cols;
        int rows = (_buttons.Count + cols - 1) / cols;
        float bw = (r.Width - (cols + 1) * pad) / cols;
        float bh = MathF.Min(bw, (r.Y + r.Height - y - (rows + 1) * pad) / rows);
        for (int i = 0; i < _buttons.Count; i++)
        {
            var b = _buttons[i];
            var br = new Rectangle(MathF.Round(r.X + pad + (i % cols) * (bw + pad)), MathF.Round(y + pad * 0.5f + (i / cols) * (bh + pad)), MathF.Round(bw), MathF.Round(bh));
            var tint = b.id == Btn.SelfDestruct ? new Color(80, 36, 34, 255) : (Color?)null;
            var st = Ui.Button(br, b.enabled, b.active, tint);
            if (b.id == Btn.Overcharge) OverchargeButton.DrawGlyph(br, _oc);
            else if (b.id == Btn.Teleport) TeleportButton.DrawGlyph(br, _tp, b.active);
            else DrawGlyph(b.id, br, b.enabled, _pause.AllOn, _dive.AllOn, _fire);
            int fs = Ui.F(10);
            string label = Ui.Fit(b.label, br.Width - Ui.P(2), fs);
            float lw = Ui.Measure(label, fs);
            Ui.Text(label, br.X + (br.Width - lw) * 0.5f, br.Y + br.Height - fs - Ui.P(4), fs, b.enabled && (b.id != Btn.Overcharge || _oc.Ready) ? Ui.TextColor : Ui.TextFaint);
            if (b.key.Length > 0) Ui.HotkeyBadge(br, b.key);
            if (st.Left) Execute(b.id);
            else if (st.Right && b.id == Btn.FireState) UnitToggles.CycleFireState(_ctx, _sel.Selected, -1);
            if (st.Hover)
            {
                string key = b.id == Btn.SelfDestruct ? Hotkeys.SelfDestruct.Label : b.key;
                if (b.id == Btn.Overcharge) OverchargeButton.Tooltip(br, key, _oc);
                else if (b.id == Btn.Teleport) TeleportButton.Tooltip(br, key, _tp);
                else Ui.Tooltip(br, key.Length == 0 ? Title(b.id, b.label) : $"{Title(b.id, b.label)}  [{key}]", Description(b.id, b.label));
            }
        }
        _buttons.Clear(); // rebuilt by the next Refresh (Hud layout)
    }

    private UnitToggles.ToggleState _pause, _dive;
    private UnitToggles.FireSelection _fire;
    private UnitToggles.OverchargeSelection _oc;
    private TeleportInput.TeleportSelection _tp;

    private void Execute(Btn id)
    {
        switch (id)
        {
            case Btn.Stop: _sel.IssueStop(); break;
            case Btn.Move: _sel.SetMode(Mode.Move); break;
            case Btn.AttackMove: _sel.SetMode(Mode.AttackMove); break;
            case Btn.Patrol: _sel.SetMode(Mode.Patrol); break;
            case Btn.Guard: _sel.SetMode(Mode.Guard); break;
            case Btn.Launch: _sel.SetMode(Mode.Launch); break;
            case Btn.SelfDestruct: _sel.PendingSelfDestruct ??= _sel.Selected.ToArray(); break;
            case Btn.Capture: _sel.SetMode(Mode.Capture); break;
            case Btn.Pause: UnitToggles.TogglePause(_ctx, _sel.Selected); break;
            case Btn.Dive: UnitToggles.ToggleDive(_ctx, _sel.Selected); break;
            case Btn.Overcharge: UnitToggles.ToggleOvercharge(_ctx, _sel.Selected); break;
            case Btn.Teleport: _sel.SetMode(Mode.Teleport); break;
            case Btn.Unload: TransportInput.StartUnload(_ctx, _sel, Ui.Shift); break;
            case Btn.TransportTo: TransportInput.StartMode(_sel, Mode.TransportTo); break;
            case Btn.Ferry: TransportInput.StartMode(_sel, Mode.Ferry); break;
            case Btn.Dock: TransportInput.DockNow(_ctx, _sel, Ui.Shift); break;
            case Btn.FireState: UnitToggles.CycleFireState(_ctx, _sel.Selected, Ui.Shift ? -1 : 1); break;
        }
    }

    private static string Title(Btn id, string label) => id switch
    {
        Btn.AttackMove => "Attack-move",
        Btn.Guard => label == "Assist" ? "Guard / Assist" : "Guard",
        Btn.TransportTo => "Transport to",
        Btn.FireState => label == "Mixed" ? "Fire state: mixed" : "Fire state: " + label switch { "Return" => "Return fire", "Hold" => "Hold fire", _ => "Fire at will" },
        _ => label,
    };

    private static string Description(Btn id, string label) => id switch
    {
        Btn.Stop => "Clears all orders of the selected units.",
        Btn.Move => "Click a destination. Shift queues waypoints. (Right-click on the ground also moves.)",
        Btn.AttackMove => "Move to a point engaging enemies on the way; click an enemy to attack it.",
        Btn.Patrol => "Patrol between the current position and the clicked point. Shift adds patrol points.",
        Btn.Guard => "Click a friendly unit: combat units guard it, engineers and factories assist its construction.",
        Btn.SelfDestruct => "Destroys the selected units (asks for confirmation).",
        Btn.Capture => "Click an enemy unit: engineers and the commander take it over (costs energy; commanders can't be captured). Shift queues. Right-click also captures when only engineers are selected.",
        Btn.Pause => "Pauses / resumes production, construction, upgrades, missile stockpiles and upkeep (shields, radar, fabricators). Paused units still move and fight.",
        Btn.Dive => "Submarines: dive (hidden, only torpedoes and sonar reach them) or surface (visible, hit by surface weapons).",
        Btn.Launch => "Click a ground target: one selected launcher with the target in range fires a missile (it waits if its stockpile is empty).",
        Btn.Unload when label == "Launch" => "Docked aircraft take off (air staging: at once; Atlantis: click the launch point).",
        Btn.Unload => "Click the drop point: the transports land there and unload. (Right-click on the ground also unloads.)",
        Btn.TransportTo => "Click a destination: the selected units are shared among the selected transports, picked up and dropped there.",
        Btn.Ferry => "Transports: click the pickup beacon, then the drop point - they shuttle units left at the beacon. Land units: click a beacon to wait there.",
        Btn.FireState => "Click: next state, right-click: previous (Fire at will → Return fire → Hold fire). Return fire: weapons pick targets only for a few seconds after the unit is hit. Hold fire: only attack orders and attack-move targets you click fire. Units capturing something never shoot it.",
        Btn.Dock => "The selected aircraft fly to the nearest air staging / carrier to repair and refuel. (Right-click a carrier: dock there.)",
        _ => "",
    };

    private static void DrawGlyph(Btn id, Rectangle br, bool enabled, bool paused, bool submerged, UnitToggles.FireSelection fire)
    {
        float labelH = Ui.F(10) + Ui.P(4);
        var c = new Vector2(br.X + br.Width * 0.5f, br.Y + (br.Height - labelH) * 0.5f + Ui.P(2));
        float s = MathF.Min(br.Width, br.Height - labelH) * 0.28f;
        var col = enabled ? new Color(220, 230, 240, 255) : new Color(90, 95, 105, 255);
        float t = MathF.Max(2f, s * 0.2f);
        switch (id)
        {
            case Btn.FireState: DrawFireGlyph(c, s, t, enabled ? fire.Mixed ? new Color(200, 200, 210, 255) : FireColor(fire.State) : col, fire.Mixed ? FireState.FireAtWill : fire.State); break;
            case Btn.Launch: StrategicHud.DrawLaunchGlyph(c, s, t, enabled, col); break;
            case Btn.Capture: CursorHint.DrawFlag(c, s * 0.9f, enabled ? CursorHint.CaptureColor : col); break;
            case Btn.Pause:
                if (paused) // resume: play triangle
                    Raylib.DrawTriangle(c + new Vector2(-s * 0.6f, -s * 0.8f), c + new Vector2(-s * 0.6f, s * 0.8f), c + new Vector2(s * 0.8f, 0), Ui.Good);
                else
                {
                    var amber = enabled ? new Color(245, 200, 90, 255) : col;
                    Raylib.DrawRectangleV(c + new Vector2(-s * 0.7f, -s * 0.8f), new Vector2(s * 0.5f, s * 1.6f), amber);
                    Raylib.DrawRectangleV(c + new Vector2(s * 0.2f, -s * 0.8f), new Vector2(s * 0.5f, s * 1.6f), amber);
                }
                break;
            case Btn.Dive:
            {
                var sea = enabled ? new Color(90, 170, 255, 255) : col;
                // Wave line, arrow down (dive) or up (surface).
                for (int k = 0; k < 3; k++)
                    Raylib.DrawRing(c + new Vector2(-s * 0.7f + k * s * 0.7f, -s * 0.05f), s * 0.3f, s * 0.3f + t * 0.7f, 0, 180, 8, sea);
                float dir = submerged ? -1 : 1;
                var tip = c + new Vector2(0, dir * s * 1.05f);
                Raylib.DrawLineEx(c - new Vector2(0, dir * s * 0.75f), tip - new Vector2(0, dir * s * 0.3f), t, Color.White);
                if (dir > 0) Raylib.DrawTriangle(tip, tip + new Vector2(s * 0.4f, -s * 0.45f), tip + new Vector2(-s * 0.4f, -s * 0.45f), Color.White);
                else Raylib.DrawTriangle(tip, tip + new Vector2(-s * 0.4f, s * 0.45f), tip + new Vector2(s * 0.4f, s * 0.45f), Color.White);
                break;
            }
            case Btn.Unload or Btn.TransportTo or Btn.Ferry or Btn.Dock: TransportGlyphs.Draw(id switch
            {
                Btn.Unload => TransportButtons.Unload, Btn.TransportTo => TransportButtons.TransportTo,
                Btn.Ferry => TransportButtons.Ferry, _ => TransportButtons.Dock,
            }, c, s, t, enabled ? TransportInput.Cyan : col); break;
            case Btn.Stop:
                Raylib.DrawRectangleV(c - new Vector2(s * 0.8f, s * 0.8f), new Vector2(s * 1.6f, s * 1.6f), enabled ? new Color(230, 80, 70, 255) : col);
                break;
            case Btn.Move:
                Raylib.DrawLineEx(c + new Vector2(-s, s * 0.6f), c + new Vector2(s * 0.5f, -s * 0.4f), t, enabled ? Ui.Good : col);
                Raylib.DrawTriangle(c + new Vector2(s, -s * 0.9f), c + new Vector2(s * 0.05f, -s * 0.75f), c + new Vector2(s * 0.75f, -s * 0.05f), enabled ? Ui.Good : col);
                break;
            case Btn.AttackMove:
            {
                var red = enabled ? new Color(240, 90, 80, 255) : col;
                Raylib.DrawRing(c, s * 0.62f, s * 0.62f + t, 0, 360, 24, red);
                Raylib.DrawLineEx(c - new Vector2(s, 0), c + new Vector2(s, 0), t, red);
                Raylib.DrawLineEx(c - new Vector2(0, s), c + new Vector2(0, s), t, red);
                break;
            }
            case Btn.Patrol:
            {
                var blue = enabled ? new Color(110, 180, 255, 255) : col;
                Raylib.DrawRing(c, s * 0.7f, s * 0.7f + t, 20, 160, 16, blue);
                Raylib.DrawRing(c, s * 0.7f, s * 0.7f + t, 200, 340, 16, blue);
                Raylib.DrawTriangle(c + new Vector2(-s * 1.05f, -s * 0.05f), c + new Vector2(-s * 0.35f, -s * 0.05f), c + new Vector2(-s * 0.7f, s * 0.45f), blue);
                Raylib.DrawTriangle(c + new Vector2(s * 1.05f, s * 0.05f), c + new Vector2(s * 0.35f, s * 0.05f), c + new Vector2(s * 0.7f, -s * 0.45f), blue);
                break;
            }
            case Btn.Guard:
            {
                var g = enabled ? new Color(240, 210, 90, 255) : col;
                Vector2[] pts =
                [
                    c + new Vector2(0, -s), c + new Vector2(-s * 0.85f, -s * 0.6f), c + new Vector2(-s * 0.7f, s * 0.3f),
                    c + new Vector2(0, s), c + new Vector2(s * 0.7f, s * 0.3f), c + new Vector2(s * 0.85f, -s * 0.6f),
                ];
                for (int i = 0; i < pts.Length; i++) Raylib.DrawLineEx(pts[i], pts[(i + 1) % pts.Length], t, g);
                break;
            }
            case Btn.SelfDestruct:
            {
                var red = enabled ? new Color(255, 110, 90, 255) : col;
                Raylib.DrawCircleV(c, s * 0.75f, red);
                Raylib.DrawLineEx(c - new Vector2(s * 0.4f, s * 0.4f), c + new Vector2(s * 0.4f, s * 0.4f), t, Color.Black);
                Raylib.DrawLineEx(c + new Vector2(-s * 0.4f, s * 0.4f), c + new Vector2(s * 0.4f, -s * 0.4f), t, Color.Black);
                Raylib.DrawLineEx(c + new Vector2(s * 0.55f, -s * 0.55f), c + new Vector2(s * 0.95f, -s * 0.95f), t * 0.8f, red);
                break;
            }
        }
    }

    public static Color FireColor(FireState s) => s switch
    {
        FireState.ReturnFire => new Color(245, 190, 70, 255),
        FireState.HoldFire => new Color(240, 90, 80, 255),
        _ => new Color(120, 225, 120, 255),
    };

    /// <summary>Crosshair (fire at will), crosshair inside a shield (return fire), crossed-out crosshair (hold fire).</summary>
    public static void DrawFireGlyph(Vector2 c, float s, float t, Color col, FireState state)
    {
        Raylib.DrawRing(c, s * 0.55f, s * 0.55f + t, 0, 360, 24, col);
        Raylib.DrawLineEx(c - new Vector2(s * 0.95f, 0), c - new Vector2(s * 0.3f, 0), t, col);
        Raylib.DrawLineEx(c + new Vector2(s * 0.3f, 0), c + new Vector2(s * 0.95f, 0), t, col);
        Raylib.DrawLineEx(c - new Vector2(0, s * 0.95f), c - new Vector2(0, s * 0.3f), t, col);
        Raylib.DrawLineEx(c + new Vector2(0, s * 0.3f), c + new Vector2(0, s * 0.95f), t, col);
        if (state == FireState.ReturnFire)
        {
            // Return arrow around the crosshair.
            Raylib.DrawRing(c, s * 1.05f, s * 1.05f + t * 0.8f, 200, 340, 12, col);
            var tip = c + new Vector2(MathF.Cos(MathF.PI * 340 / 180) * s * 1.1f, MathF.Sin(MathF.PI * 340 / 180) * s * 1.1f);
            Raylib.DrawTriangle(tip + new Vector2(s * 0.3f, -s * 0.15f), tip + new Vector2(-s * 0.25f, -s * 0.2f), tip + new Vector2(0, s * 0.3f), col);
        }
        else if (state == FireState.HoldFire)
            Raylib.DrawLineEx(c + new Vector2(-s * 0.85f, s * 0.85f), c + new Vector2(s * 0.85f, -s * 0.85f), t * 1.2f, col);
    }
}
