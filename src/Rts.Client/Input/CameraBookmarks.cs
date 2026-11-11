using Rts.Client.Persistence;

namespace Rts.Client.Input;

/// <summary>A saved camera view: ground focus (x, z), zoom distance and rotation.</summary>
public readonly record struct CameraView(float X, float Z, float Distance, float Yaw);

/// <summary>
/// (7b, G14) Camera bookmarks: <see cref="Slots"/> saved views (Ctrl+Alt+1..5 saves, Alt+1..5 goes there, rotation
/// included). Pure state (unit-tested) + the key handling. Lives on the <see cref="SelectionController"/> (one per
/// game); client only: not part of saves or replays.
/// </summary>
public sealed class CameraBookmarks
{
    public const int Slots = 5;
    private readonly CameraView?[] _slots = new CameraView?[Slots];

    public static bool ValidSlot(int slot) => slot >= 0 && slot < Slots;

    /// <summary>Stores a view in a slot (0-based); false for a slot out of range.</summary>
    public bool Save(int slot, CameraView view)
    {
        if (!ValidSlot(slot)) return false;
        _slots[slot] = view;
        return true;
    }

    /// <summary>The view of a slot, or null when it is empty / out of range.</summary>
    public CameraView? Recall(int slot) => ValidSlot(slot) ? _slots[slot] : null;

    public bool Has(int slot) => Recall(slot).HasValue;

    public void Clear() => Array.Clear(_slots);

    /// <summary>Called once per frame by <see cref="SelectionController"/> (works in replays too).</summary>
    public void HandleHotkeys(GameContext ctx)
    {
        var cam = ctx.Camera;
        int save = Hotkeys.BookmarkSave.PressedIndex();
        if (save >= 0 && Save(save, new CameraView(cam.Focus.X, cam.Focus.Z, cam.TargetDistance, cam.Yaw)))
        {
            Toasts.Show($"Camera bookmark {save + 1} saved (Alt+{save + 1}: go there)");
            return;
        }
        int go = Hotkeys.BookmarkRecall.PressedIndex();
        if (go < 0) return;
        if (Recall(go) is CameraView v)
        {
            cam.SetYaw(v.Yaw);
            cam.LookAt(v.X, v.Z, v.Distance);
        }
        else Toasts.Show($"Camera bookmark {go + 1} is empty (Ctrl+Alt+{go + 1} saves the view)");
    }
}
