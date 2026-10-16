using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Core;
using Rts.Sim.Snapshot;

namespace Rts.Client.UI;

/// <summary>
/// SupCom idle buttons above the minimap: idle engineers and idle factories of the local army (only shown when
/// there are some; not in replays). Click / hotkey (<see cref="Hotkeys.IdleEngineer"/>, <see cref="Hotkeys.IdleFactory"/>):
/// select the next idle one and center the camera on it; Shift: select all of them.
/// Idle = <see cref="SelectionMath.IsIdleEngineer"/> / <see cref="SelectionMath.IsIdleFactory"/>.
/// </summary>
public sealed class IdleButtons
{
    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    private readonly List<UnitView> _eng = new(), _fac = new();
    private Rectangle _engR, _facR;
    private long _lastEng = -1, _lastFac = -1;

    public IdleButtons(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
    }

    public bool Hit(System.Numerics.Vector2 m) =>
        (_engR.Width > 0 && Raylib.CheckCollisionPointRec(m, _engR)) || (_facR.Width > 0 && Raylib.CheckCollisionPointRec(m, _facR));

    public void Layout(SimSnapshot snap, Rectangle mini, float gap)
    {
        _eng.Clear();
        _fac.Clear();
        _engR = _facR = default;
        if (_ctx.ReadOnly) return;
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var v = ref snap.Units[k];
            if (v.Army != _ctx.LocalArmy) continue;
            var bp = _ctx.Db[v.Bp];
            if (SelectionMath.IsIdleEngineer(bp, v)) _eng.Add(v);
            else if (SelectionMath.IsIdleFactory(bp, v)) _fac.Add(v);
        }
        float s = Ui.P(38), x = mini.X, y = mini.Y - s - gap;
        if (_eng.Count > 0) { _engR = new Rectangle(x, y, s, s); x += s + gap; }
        if (_fac.Count > 0) _facR = new Rectangle(x, y, s, s);
    }

    public void Draw(SimSnapshot snap)
    {
        if (_ctx.ReadOnly) return;
        if (!_sel.InputBlocked)
        {
            if (Hotkeys.IdleEngineer.Pressed()) Pick(_eng, ref _lastEng, Ui.Shift);
            if (Hotkeys.IdleFactory.Pressed()) Pick(_fac, ref _lastFac, Ui.Shift);
        }
        DrawButton(_engR, _eng, ref _lastEng, "Idle engineers", Hotkeys.IdleEngineer.Label);
        DrawButton(_facR, _fac, ref _lastFac, "Idle factories", Hotkeys.IdleFactory.Label);
    }

    private void DrawButton(Rectangle r, List<UnitView> list, ref long last, string title, string key)
    {
        if (r.Width <= 0 || list.Count == 0) return;
        var st = Ui.Button(r);
        UnitIcons.Draw(Ui.Inset(r, Ui.P(2)), _ctx.Db[list[0].Bp], compact: true);
        string cnt = list.Count.ToString();
        int fs = Ui.F(11);
        float cw = Ui.Measure(cnt, fs, true) + Ui.P(6);
        var badge = new Rectangle(r.X + r.Width - cw - 2, r.Y + r.Height - fs - Ui.P(4), cw, fs + Ui.P(2));
        Raylib.DrawRectangleRec(badge, new Color(0, 0, 0, 200));
        Ui.Text(cnt, badge.X + Ui.P(3), badge.Y + Ui.P(1) - 1, fs, Ui.Warn, true);
        Ui.HotkeyBadge(r, key);
        if (st.Left) Pick(list, ref last, Ui.Shift);
        if (st.Hover) Ui.Tooltip(r, $"{title}: {list.Count}  [{key}]", "Click: select the next one and center the camera on it\nShift+click: select all of them");
    }

    private void Pick(List<UnitView> list, ref long last, bool all)
    {
        if (list.Count == 0) return;
        if (all)
        {
            _sel.SelectOnly(list.Select(v => new UnitHandle(v.Index, v.Gen)));
            Audio.UiSounds.Click();
            return;
        }
        var ids = list.Select(v => ((long)v.Index << 32) | (uint)v.Gen).ToList();
        int i = SelectionMath.NextIndex(ids, last);
        var u = list[i];
        last = ids[i];
        _sel.SelectOnly([new UnitHandle(u.Index, u.Gen)]);
        _ctx.Camera.LookAt(u.Pos.X, u.Pos.Z, _ctx.Camera.Distance);
        Audio.UiSounds.Click();
    }
}
