using System.Numerics;
using Raylib_cs;
using Rts.Client.Persistence;
using Rts.Client.UI;
using Rts.Sim.Orders;

namespace Rts.Client.Input;

/// <summary>
/// (7b) Map pings and the alert jump. Alt+left click = alert ping, Alt+Shift+left = attack ping, Alt+right = move ping
/// (on the map here, on the minimap in UI/Minimap), Y = alert ping at the cursor; each sends a <see cref="MapPingCommand"/>
/// (the sim throttles, replays and lockstep carry it; UI/PingMarkers draws the snapshot's pings). Backspace = jump the
/// camera to the last alert of <see cref="SelectionController.Alerts"/> (again: the next older one).
/// </summary>
public sealed class PingInput
{
    private readonly GameContext _ctx;
    private readonly AlertLog _log;

    public PingInput(GameContext ctx, AlertLog log)
    {
        _ctx = ctx;
        _log = log;
    }

    /// <summary>Kind for an Alt+click: left = alert (Shift: attack), right = move. Pure.</summary>
    public static PingKind KindFor(bool right, bool shift) => right ? PingKind.Move : shift ? PingKind.Attack : PingKind.Alert;

    public void Send(PingKind kind, Vector2 pos)
    {
        if (_ctx.ReadOnly) return; // replays / observers: no commands
        _ctx.Runner.Enqueue(new MapPingCommand(_ctx.LocalArmy, kind, pos));
    }

    /// <summary>World input (SelectionController.Update). True = an Alt+click was used for a ping this frame.</summary>
    public bool HandleWorld(bool mouseOverUi)
    {
        if (mouseOverUi || _ctx.ReadOnly) return false;
        bool alt = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
        bool left = Raylib.IsMouseButtonPressed(MouseButton.Left), right = Raylib.IsMouseButtonPressed(MouseButton.Right);
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        if (alt && (left || right))
        {
            if (_ctx.PickGround(Raylib.GetMousePosition()) is Vector3 g) Send(KindFor(right, shift), new Vector2(g.X, g.Z));
            return true;
        }
        if (Hotkeys.PingAlert.Pressed() && _ctx.PickGround(Raylib.GetMousePosition()) is Vector3 p)
            Send(PingKind.Alert, new Vector2(p.X, p.Z));
        return false;
    }

    /// <summary>Hotkeys that work over the UI too (SelectionController.HandleHotkeys): Backspace.</summary>
    public void HandleKeys()
    {
        if (!Hotkeys.JumpToAlert.Pressed()) return;
        if (_log.NextJump(Raylib.GetTime()) is not AlertEntry e) { Toasts.Show("No alerts yet"); return; }
        _ctx.Camera.LookAt(e.Pos.X, e.Pos.Z, MathF.Min(_ctx.Camera.Distance, 140f));
    }
}
