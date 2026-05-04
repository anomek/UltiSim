using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace UltiSim.Core.SimObjects;

// Wraps a native GameObject (typically the duty Exit portal or scenery clutter)
// that we want to suppress for the duration of a scenario. The native API operates
// per-object — DisableDraw + RenderFlags Model/Nameplate to hide, EnableDraw +
// flag clear to restore — so each hidden object is its own SimObject. The
// VisibilityFlags bits alone block interaction but not rendering; the DisableDraw
// is what actually tears down the draw object.
//
// We remember (ObjectIndex, BaseId) and re-resolve on Despawn so a moved or
// recreated object isn't mistaken for the original. Tick is a no-op today; if
// the game's event system later re-enables drawing on the persistent object, a
// re-assert in Tick is the natural place for it.
public sealed unsafe class SimHiddenObject : ISimObject
{
    private readonly ushort objectIndex;
    private readonly uint baseId;
    private bool hidden;

    private SimHiddenObject(ushort objectIndex, uint baseId)
    {
        this.objectIndex = objectIndex;
        this.baseId = baseId;
        hidden = true;
    }

    // Walks ObjectTable for the first object matching baseId, hides it, and
    // returns a SimHiddenObject tracking the suppression. Returns null if no
    // such object is in the current zone.
    public static SimHiddenObject? Hide(uint baseId)
    {
        foreach (var go in Plugin.ObjectTable)
        {
            if (go.BaseId != baseId) continue;
            var obj = (GameObject*)go.Address;
            obj->DisableDraw();
            obj->RenderFlags |= VisibilityFlags.Model | VisibilityFlags.Nameplate;
            return new SimHiddenObject(go.ObjectIndex, baseId);
        }
        return null;
    }

    public void Tick(float deltaSeconds) { }

    public void Despawn()
    {
        if (!hidden) return;
        var obj = Lookup();
        if (obj != null)
        {
            obj->RenderFlags &= ~(VisibilityFlags.Model | VisibilityFlags.Nameplate);
            obj->EnableDraw();
        }
        hidden = false;
    }

    private GameObject* Lookup()
    {
        var obj = (GameObject*)Plugin.ObjectTable.GetObjectAddress(objectIndex);
        if (obj == null || obj->BaseId != baseId) return null;
        return obj;
    }
}
