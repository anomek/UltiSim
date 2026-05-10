using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace UltiSim.Core.SimObjects;

public enum WaymarkSlot : int
{
    A = 0, B = 1, C = 2, D = 3,
    One = 4, Two = 5, Three = 6, Four = 7,
}

public sealed record Waymark(WaymarkSlot Slot, Vector3 Offset);

public static class WaymarkPresets
{
    // Ring of A,1,B,2,C,3,D,4 spaced 45° apart, A at north going clockwise.
    // Angle convention: offset = (sin(a)*r, 0, cos(a)*r), a=π is north (-Z).
    public static Waymark[] Ring(float radius)
    {
        var slots = new[]
        {
            WaymarkSlot.A, WaymarkSlot.One,
            WaymarkSlot.B, WaymarkSlot.Two,
            WaymarkSlot.C, WaymarkSlot.Three,
            WaymarkSlot.D, WaymarkSlot.Four,
        };
        var ring = new Waymark[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            var angle = MathF.PI - i * (MathF.PI / 4f);
            ring[i] = new Waymark(slots[i], new Vector3(radius * MathF.Sin(angle), 0, radius * MathF.Cos(angle)));
        }
        return ring;
    }
}

// Bulk-applied waymark layout. Writes directly into MarkingController._fieldMarkers
// instead of going through PlacePreset / ClearFieldMarkers — those are gated by
// territory ("No markers allowed in territory" return code 5) and would no-op
// in the overworld. The renderer reads _fieldMarkers each frame, so direct writes
// place client-side markers anywhere. Tick is a no-op; the layout sits until
// Despawn (or scenario reset) clears the slots we set.
public sealed unsafe class SimWaymarks : ISimObject
{
    private readonly List<int> placedSlots = new();

    internal SimWaymarks(IReadOnlyList<Waymark> waymarks, Vector3 origin)
    {
        if (waymarks.Count == 0) return;
        var controller = MarkingController.Instance();
        if (controller == null) { Plugin.Log.Warning("SimWaymarks: MarkingController unavailable"); return; }

        for (int i = 0; i < waymarks.Count; i++)
        {
            var wm = waymarks[i];
            var idx = (int)wm.Slot;
            if (idx < 0 || idx > 7) continue;
            var world = origin + wm.Offset;
            ref var slot = ref controller->FieldMarkers[idx];
            slot.Position = world;
            slot.X = (int)MathF.Round(world.X * 1000f);
            slot.Y = (int)MathF.Round(world.Y * 1000f);
            slot.Z = (int)MathF.Round(world.Z * 1000f);
            slot.Active = true;
            placedSlots.Add(idx);
        }
    }

    public bool IsAlive => placedSlots.Count > 0;
    public void Tick(float deltaSeconds) { }

    public void Despawn()
    {
        if (placedSlots.Count == 0) return;
        var controller = MarkingController.Instance();
        if (controller != null)
        {
            foreach (var idx in placedSlots)
                controller->FieldMarkers[idx].Active = false;
        }
        placedSlots.Clear();
    }
}
