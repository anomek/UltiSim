namespace UltiSim.Core.SimObjects;

// Game-object rules for UltiSim.Core.SimObjects:
// 1. SimObjects may tick to update state.
// 2. SimObjects own their children and cascade Tick and Despawn.
// 3. SimObjects must be created via their parent's spawn API
//    (e.g. SimWorld.SpawnEnemy), never constructed directly by consumers.
// 4. Tick and Despawn must be safe to call repeatedly, including after Despawn.
// 5. Helpers (status writers, VFX bridges, native function wrappers, HUD
//    mirrors) live in UltiSim.Core, NOT here.
public interface ISimObject
{
    void Tick(float deltaSeconds);
    void Despawn();
}
