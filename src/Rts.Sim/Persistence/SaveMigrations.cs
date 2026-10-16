using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Persistence;

/// <summary>
/// Upgrades files written with an older container version (<see cref="SaveContainer.Version"/>) on load, one version
/// step at a time: <see cref="MigrateHeader"/> edits the header JSON (before it becomes a <see cref="GameInfo"/>),
/// <see cref="MigratePayload"/> may rewrite the decompressed payload. Both run for saves and replays; file lists
/// (<see cref="SaveContainer.ReadInfo"/>) only migrate the header.
/// <para>How to change the file format: bump <see cref="SaveContainer.Version"/>, add a <see cref="Steps"/> entry
/// and a <c>case</c> for the old version below, and extend <c>SaveMigrationTests</c> (it writes a file of every older
/// version from a current game and loads it). Raise <see cref="SaveContainer.MinVersion"/> only to drop support.</para>
/// <para>Limit: the payload is still checked type by type against this build's field layouts (an added/removed sim
/// field makes the file incompatible); a payload step is where such a conversion would go.</para>
/// </summary>
public static class SaveMigrations
{
    /// <summary>Every format change, oldest first: the version it upgrades from and what changed.</summary>
    public static readonly (ushort From, string What)[] Steps =
    [
        (1, "v2: every player in the header has a faction id (v1 files from before factions have \"\": they were UEF)"),
    ];

    /// <summary>Upgrades the header JSON of a version <paramref name="from"/> file to the current version.</summary>
    internal static string MigrateHeader(ushort from, string json)
    {
        if (from >= SaveContainer.Version) return json;
        JsonObject head;
        try { head = JsonNode.Parse(json) as JsonObject ?? throw new SaveFormatException("Damaged file (bad header)"); }
        catch (System.Text.Json.JsonException e) { throw new SaveFormatException("Damaged file (bad header)", e); }
        for (int v = from; v < SaveContainer.Version; v++)
        {
            switch (v)
            {
                case 1: V1PlayerFactions(head); break;
            }
        }
        return head.ToJsonString();
    }

    /// <summary>Upgrades the decompressed payload of a version <paramref name="from"/> file (no step changes it yet).</summary>
    internal static byte[] MigratePayload(ushort from, byte[] payload, SaveFileKind kind)
    {
        for (int v = from; v < SaveContainer.Version; v++)
        {
            switch (v)
            {
                case 1: break; // v1 → v2: header only
            }
        }
        return payload;
    }

    private static void V1PlayerFactions(JsonObject head)
    {
        if (head["Players"] is not JsonArray players) return;
        foreach (var p in players)
        {
            if (p is not JsonObject po) continue;
            if (po["Faction"] is not JsonValue f || string.IsNullOrEmpty(f.GetValue<string>()))
                po["Faction"] = FactionDb.DefaultFaction;
        }
    }
}
