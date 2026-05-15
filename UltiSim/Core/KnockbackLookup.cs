using Lumina.Excel;
using LuminaKnockback = Lumina.Excel.Sheets.Knockback;

namespace UltiSim.Core;

// Resolves a Knockback-sheet row id directly to its Distance/Speed pair.
// Scenarios get the row id from their parser-emitted timeline (the parser
// decodes it from the corresponding ACT type-22 effect entry) and pass it
// straight to SimParty/SimPartySlot — no intermediate action-id table.
internal static class KnockbackLookup
{
    private static readonly ExcelSheet<LuminaKnockback> Sheet =
        Plugin.DataManager.GetExcelSheet<LuminaKnockback>();

    public static bool TryGet(uint knockbackId, out float distance, out float speed)
    {
        distance = 0f;
        speed = 0f;
        if (!Sheet.TryGetRow(knockbackId, out var row))
        {
            Plugin.Log.Warning($"KnockbackLookup: Knockback row {knockbackId} missing in sheet");
            return false;
        }
        distance = row.Distance;
        speed = row.Speed;
        return true;
    }
}
