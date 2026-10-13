namespace Rts.Client.Input;

/// <summary>
/// D and E are shared by the unit toggles (<see cref="UnitToggles"/>: dive, capture) and the transport orders
/// (<see cref="TransportInput"/>: dock, transport-to); what they do depends on the selection. Pure (unit-tested).
/// <list type="bullet">
/// <item>D: the selected submarines dive / surface AND the selected aircraft dock at the nearest air carrier (each unit
///   gets the action that applies to it; a mixed selection does both).</item>
/// <item>E: transport-to when air transports are selected, otherwise capture when engineers / commanders are.</item>
/// </list>
/// The order buttons show the key only on the button it triggers (see UI/OrdersPanel).
/// </summary>
public static class SharedHotkeys
{
    public enum EAction : byte { None, Capture, TransportTo }

    /// <summary>What E does: transports win over capturers.</summary>
    public static EAction ResolveE(bool anyCapturer, bool anyTransport) =>
        anyTransport ? EAction.TransportTo : anyCapturer ? EAction.Capture : EAction.None;

    /// <summary>What D does: dive / surface the subs, dock the aircraft (both when both are selected).</summary>
    public static (bool Dive, bool Dock) ResolveD(bool anySub, bool anyAircraft) => (anySub, anyAircraft);
}
