using Rts.Client.UI;

namespace Rts.Client.Menus;

/// <summary>(11b-1) The voice announcer rows of the Audio tab: "Voice" on/off and "Voice volume".</summary>
public sealed partial class SettingsScreen
{
    private void VoiceRows()
    {
        Row("Voice", "Spoken announcements (in Italian): commander or base under attack,\nnuclear launch detected, experimental detected, low mass / energy...\nThe alert sounds and messages stay either way.");
        bool on = ToggleControl(_s.VoiceOn);
        NextRow();
        float vol = _s.VoiceOn ? VolumeRow(6, "Voice volume", _s.VoiceVolume) : DisabledVolumeRow("Voice volume", _s.VoiceVolume);
        if (on != _s.VoiceOn || vol != _s.VoiceVolume)
        {
            (_s.VoiceOn, _s.VoiceVolume) = (on, vol);
            _s.ApplyVoice(_app.Audio.Settings);
            _dirty = true;
        }
    }

    /// <summary>The volume row greyed out (voice off): shows the value, takes no input.</summary>
    private float DisabledVolumeRow(string label, float value)
    {
        MenuUi.Label(label, _lx, _ry, _rowH);
        var bar = Ui.R(_cx, _ry + Ui.P(9), Ui.P(250), Ui.P(12));
        Raylib_cs.Raylib.DrawRectangleRec(bar, ColorUtil.WithAlpha(Ui.TextFaint, 60));
        Ui.Text($"{value * 100:0}% (voice off)", bar.X + bar.Width + Ui.P(10), _ry + (_rowH - Ui.F(11f)) * 0.5f, Ui.F(11f), Ui.TextFaint);
        NextRow();
        return value;
    }
}
