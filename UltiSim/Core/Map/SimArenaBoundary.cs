using System.Numerics;
using UltiSim.Core.SimObjects;

namespace UltiSim.Core.Map;

// Per-frame arena fence. Walks every active party member each tick (player
// included, since SimParty exposes the player slot through ActiveMembers) and
// kills anyone whose XZ distance from `center` exceeds `radius`. Geometry is
// XZ-plane only — Y is ignored, matching how scenarios reason about positions.
//
// Also spawns a floor-ring omen VFX at the boundary so the limit is visible.
//
// Added to SimWorld.children via MapController.EnforceArenaBoundary so it gets
// cleared as a normal scenario child on Reset.
internal sealed unsafe class SimArenaBoundary : ISimObject
{
    // Donut omen has a fixed inner/outer ratio of 0.82. Scale by radius/0.82 so the
    // inner edge aligns with the kill boundary (outer edge extends ~4.4y beyond it).
    private const string RingVfxPath = "vfx/omen/eff/gl_sircle_1109w.avfx";

    private readonly SimParty party;
    private readonly float radiusSq;
    private readonly string cause;
    private readonly VfxFunctions.StaticVfxStruct* ringVfx;

    public bool IsAlive => true;

    internal SimArenaBoundary(SimParty party, SimWorld world, float radius, string cause, bool showVfx = true)
    {
        this.party = party;
        this.radiusSq = radius * radius;
        this.cause = cause;

        if (showVfx && Plugin.DataManager.FileExists(RingVfxPath))
            ringVfx = VfxFunctions.SpawnStaticVfx(RingVfxPath, new Placement(world.ScenarioOrigin, 0f), new Vector3(radius / 0.82f, 1f, radius / 0.82f));
    }

    public void Tick(float deltaSeconds)
    {
        // Member positions are scenario-local; the boundary is centered on local zero.
        foreach (var member in party.ActiveMembers())
        {
            var p = member.Position;
            if (p.X * p.X + p.Z * p.Z > radiusSq) member.Die(cause);
        }
    }

    public void Despawn()
    {
        VfxFunctions.RemoveStaticVfx(ringVfx);
    }
}
