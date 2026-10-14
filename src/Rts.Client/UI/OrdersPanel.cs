using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Mode = Rts.Client.Input.SelectionController.CommandMode;

namespace Rts.Client.UI;

/// <summary>
/// Order buttons with SupCom-like hotkeys: Stop (S), Move (M), Attack-move (A), Patrol (P), Guard/Assist (G),
/// Self-destruct (Ctrl+K, asks for confirmation), and when they apply: Capture (E, engineers / commander), Pause (H,
/// pausable units), Dive / Surface (D, submarines) and the transport orders (<see cref="TransportInput"/>): Unload /
/// Launch (U), Transport-to (E), Ferry (F), Dock (D); extra rows appear for them. E and D are shared
/// (<see cref="SharedHotkeys"/>): the Capture button shows no key while transports are selected (E = transport-to).
/// Hotkeys themselves are handled by SelectionController / UnitToggles / TransportInput.
/// </summary>
public sealed class OrdersPanel
{
    private readonly GameContext _ctx;
    private readonly SelectionController _sel;

    private enum Btn { Stop, Move, AttackMove, Patrol, Guard, SelfDestruct, Launch, Capture, Pause, Dive, Unload, TransportTo, Ferry, Dock }

    public OrdersPanel(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
    }

    /// <summary>Panel width for the given UI scale.</summary>
    public static float Width => 3 * Ui.P(48) + 4 * Ui.P(6);

    public void Draw(Rectangle r, SimSnapshot snap)
    {
        Ui.Panel(r);
        float y = Ui.PanelTitle(r, "Orders");

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

        var buttons = new List<(Btn id, string label, string key, bool enabled, bool active)>
        {
            (Btn.Stop, "Stop", "S", true, false),
            anyLauncher && !canMove ? (Btn.Launch, "Launch", "L", true, _sel.Mode == Mode.Launch) : (Btn.Move, "Move", "M", canMove, _sel.Mode == Mode.Move),
            (Btn.AttackMove, "Attack", "A", canMove, _sel.Mode == Mode.AttackMove),
            (Btn.Patrol, "Patrol", "P", canMove, _sel.Mode == Mode.Patrol),
            (Btn.Guard, anyEngineer || anyFactory ? "Assist" : "Guard", "G", canMove, _sel.Mode == Mode.Guard),
        };
        if (UnitToggles.AnyCapturer(_ctx, _sel.Selected))
        {
            // E is transport-to while transports are selected (SharedHotkeys): no key on the Capture button then.
            string key = UnitToggles.EKey(_ctx, _sel.Selected) == SharedHotkeys.EAction.Capture ? UnitToggles.CaptureKeyName : "";
            buttons.Add((Btn.Capture, "Capture", key, true, _sel.Mode == Mode.Capture));
        }
        _pause = UnitToggles.PauseState(_ctx, _sel.Selected);
        if (_pause.Any) buttons.Add((Btn.Pause, _pause.AllOn ? "Resume" : "Pause", UnitToggles.PauseKeyName, true, _pause.AllOn));
        _dive = UnitToggles.DiveState(_ctx, _sel.Selected);
        if (_dive.Any) buttons.Add((Btn.Dive, _dive.AllOn ? "Surface" : "Dive", UnitToggles.DiveKeyName, true, false));
        var tb = TransportInput.ButtonsFor(CargoSelection.Of(_ctx, _sel.Selected, snap));
        if ((tb & TransportButtons.Unload) != 0) buttons.Add((Btn.Unload, "Unload", "U", true, _sel.Mode == Mode.Unload));
        if ((tb & TransportButtons.Launch) != 0) buttons.Add((Btn.Unload, "Launch", "U", true, _sel.Mode == Mode.Unload));
        if ((tb & TransportButtons.TransportTo) != 0) buttons.Add((Btn.TransportTo, "Transport", "E", true, _sel.Mode == Mode.TransportTo));
        if ((tb & TransportButtons.Ferry) != 0) buttons.Add((Btn.Ferry, "Ferry", "F", true, _sel.Mode == Mode.Ferry));
        if ((tb & TransportButtons.Dock) != 0) buttons.Add((Btn.Dock, "Dock", "D", true, false));
        buttons.Add((Btn.SelfDestruct, "Destruct", "^K", true, _sel.PendingSelfDestruct != null));

        float pad = Ui.P(6);
        int rows = (buttons.Count + 2) / 3;
        float bw = (r.Width - 4 * pad) / 3f;
        float bh = MathF.Min(bw, (r.Y + r.Height - y - (rows + 1) * pad) / rows);
        for (int i = 0; i < buttons.Count; i++)
        {
            var b = buttons[i];
            var br = new Rectangle(MathF.Round(r.X + pad + (i % 3) * (bw + pad)), MathF.Round(y + pad * 0.5f + (i / 3) * (bh + pad)), MathF.Round(bw), MathF.Round(bh));
            var tint = b.id == Btn.SelfDestruct ? new Color(80, 36, 34, 255) : (Color?)null;
            var st = Ui.Button(br, b.enabled, b.active, tint);
            DrawGlyph(b.id, br, b.enabled, _pause.AllOn, _dive.AllOn);
            int fs = Ui.F(10);
            float lw = Ui.Measure(b.label, fs);
            Ui.Text(b.label, br.X + (br.Width - lw) * 0.5f, br.Y + br.Height - fs - Ui.P(4), fs, b.enabled ? Ui.TextColor : Ui.TextFaint);
            if (b.key.Length > 0) Ui.HotkeyBadge(br, b.key);
            if (st.Left) Execute(b.id);
            if (st.Hover) Ui.Tooltip(br, b.label == "Destruct" ? "Self-destruct  [Ctrl+K]" : b.key.Length == 0 ? Title(b.id, b.label) : $"{Title(b.id, b.label)}  [{b.key}]", Description(b.id, b.label));
        }
    }

    private UnitToggles.ToggleState _pause, _dive;

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
            case Btn.Unload: TransportInput.StartUnload(_ctx, _sel, Ui.Shift); break;
            case Btn.TransportTo: TransportInput.StartMode(_sel, Mode.TransportTo); break;
            case Btn.Ferry: TransportInput.StartMode(_sel, Mode.Ferry); break;
            case Btn.Dock: TransportInput.DockNow(_ctx, _sel, Ui.Shift); break;
        }
    }

    private static string Title(Btn id, string label) => id switch
    {
        Btn.AttackMove => "Attack-move",
        Btn.Guard => label == "Assist" ? "Guard / Assist" : "Guard",
        Btn.TransportTo => "Transport to",
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
        Btn.Dock => "The selected aircraft fly to the nearest air staging / carrier to repair and refuel. (Right-click a carrier: dock there.)",
        _ => "",
    };

    private static void DrawGlyph(Btn id, Rectangle br, bool enabled, bool paused, bool submerged)
    {
        float labelH = Ui.F(10) + Ui.P(4);
        var c = new Vector2(br.X + br.Width * 0.5f, br.Y + (br.Height - labelH) * 0.5f + Ui.P(2));
        float s = MathF.Min(br.Width, br.Height - labelH) * 0.28f;
        var col = enabled ? new Color(220, 230, 240, 255) : new Color(90, 95, 105, 255);
        float t = MathF.Max(2f, s * 0.2f);
        switch (id)
        {
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
}
