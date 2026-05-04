using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace UltiSim.Core.SimObjects;

// Bulk-applied waymark layout. The native API (MarkingController.PlacePreset /
// ClearFieldMarkers) is all-or-nothing, so this models all eight slots as one
// SimObject — there's no per-slot lifecycle. Tick is a no-op; once placed the
// layout sits until Despawn (or scenario reset) tears it down.
public sealed unsafe class SimWaymarks : ISimObject
{
    private bool placed;

    internal SimWaymarks(IReadOnlyList<Waymark> waymarks, Vector3 origin)
    {
        Apply(waymarks, origin);
    }

    private void Apply(IReadOnlyList<Waymark> waymarks, Vector3 origin)
    {
        if (waymarks.Count == 0) return;
        var controller = MarkingController.Instance();
        if (controller == null)
        {
            Plugin.Log.Warning("SimWaymarks: MarkingController unavailable");
            return;
        }

        var placement = default(MarkerPresetPlacement);
        for (int i = 0; i < waymarks.Count; i++)
        {
            var wm = waymarks[i];
            var idx = (int)wm.Slot;
            if (idx < 0 || idx > 7) continue;
            var world = origin + wm.Offset;
            placement.Active[idx] = true;
            placement.X[idx] = (int)System.MathF.Round(world.X * 1000f);
            placement.Y[idx] = (int)System.MathF.Round(world.Y * 1000f);
            placement.Z[idx] = (int)System.MathF.Round(world.Z * 1000f);
        }

        var result = controller->PlacePreset(&placement);
        if (result != 0) Plugin.Log.Warning($"SimWaymarks: PlacePreset returned {result}");
        placed = true;
    }

    public void Tick(float deltaSeconds) { }

    public void Despawn()
    {
        if (!placed) return;
        var controller = MarkingController.Instance();
        if (controller != null) controller->ClearFieldMarkers();
        placed = false;
    }
}
