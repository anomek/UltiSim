using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace UltiSim.Core;

// Wrappers around native VfxContainer functions FFXIVClientStructs doesn't bind.
// SetTether allocates/releases the VFX pointer at Tether+0x08 — writing the
// Tether struct directly leaves a dangling vfx, so the line keeps drawing.
//
// Static VFX (omens): setting CastInfo manually doesn't trigger Character::StartCast's
// omen spawn. We call the same StaticVfxCreate / StaticVfxRun / StaticVfxRemove
// trio that VFXEditor and FFXIV-RaidsRewritten use — proven sigs, simple layout.
internal static unsafe class VfxFunctions
{
    private delegate void SetTetherDelegate(VfxContainer* self, byte tetherIndex, ushort tetherId, ulong targetId, byte tetherProgress);
    private delegate IntPtr StaticVfxCreateDelegate(byte* path, byte* pool);
    private delegate IntPtr StaticVfxRunDelegate(IntPtr vfx, float a1, uint a2);
    private delegate IntPtr StaticVfxRemoveDelegate(IntPtr vfx);
    private delegate IntPtr ActorVfxCreateDelegate(byte* path, IntPtr caster, IntPtr target, float a4, byte a5, ushort a6, byte a7);
    private delegate IntPtr ActorVfxRemoveDelegate(IntPtr vfx, byte a2);

    [StructLayout(LayoutKind.Explicit)]
    public struct StaticVfxStruct
    {
        [FieldOffset(0x38)] public byte Flags;
        [FieldOffset(0x50)] public Vector3 Position;
        [FieldOffset(0x60)] public Quaternion Rotation;
        [FieldOffset(0x70)] public Vector3 Scale;
        [FieldOffset(0x248)] public byte SomeFlags;
    }

    private static SetTetherDelegate? setTether;
    private static StaticVfxCreateDelegate? staticVfxCreate;
    private static StaticVfxRunDelegate? staticVfxRun;
    private static StaticVfxRemoveDelegate? staticVfxRemove;
    private static ActorVfxCreateDelegate? actorVfxCreate;
    private static ActorVfxRemoveDelegate? actorVfxRemove;
    private static byte[]? poolBytes;

    private static SetTetherDelegate? ResolveSetTether()
    {
        if (setTether != null) return setTether;
        try
        {
            var addr = Plugin.SigScanner.ScanText("E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 0F B6 54 24 ?? 45 33 C0");
            setTether = Marshal.GetDelegateForFunctionPointer<SetTetherDelegate>(addr);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"VfxFunctions: failed to resolve SetTether: {ex.Message}");
        }
        return setTether;
    }

    private static StaticVfxCreateDelegate? ResolveStaticVfxCreate()
    {
        if (staticVfxCreate != null) return staticVfxCreate;
        try
        {
            var addr = Plugin.SigScanner.ScanText("E8 ?? ?? ?? ?? F3 0F 10 35 ?? ?? ?? ?? 48 89 43 08");
            staticVfxCreate = Marshal.GetDelegateForFunctionPointer<StaticVfxCreateDelegate>(addr);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"VfxFunctions: failed to resolve StaticVfxCreate: {ex.Message}");
        }
        return staticVfxCreate;
    }

    private static StaticVfxRunDelegate? ResolveStaticVfxRun()
    {
        if (staticVfxRun != null) return staticVfxRun;
        try
        {
            var addr = Plugin.SigScanner.ScanText("E8 ?? ?? ?? ?? B0 02 EB 02");
            staticVfxRun = Marshal.GetDelegateForFunctionPointer<StaticVfxRunDelegate>(addr);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"VfxFunctions: failed to resolve StaticVfxRun: {ex.Message}");
        }
        return staticVfxRun;
    }

    private static StaticVfxRemoveDelegate? ResolveStaticVfxRemove()
    {
        if (staticVfxRemove != null) return staticVfxRemove;
        try
        {
            var addr = Plugin.SigScanner.ScanText("40 53 48 83 EC 20 48 8B D9 48 8B 89 ?? ?? ?? ?? 48 85 C9 74 28 33 D2 E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9");
            staticVfxRemove = Marshal.GetDelegateForFunctionPointer<StaticVfxRemoveDelegate>(addr);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"VfxFunctions: failed to resolve StaticVfxRemove: {ex.Message}");
        }
        return staticVfxRemove;
    }

    public static void SetTether(Character* chara, byte slot, ushort tetherId, GameObjectId targetId, byte progress)
    {
        if (chara == null) return;
        var fn = ResolveSetTether();
        if (fn == null) return;
        fn(&chara->Vfx, slot, tetherId, (ulong)targetId, progress);
    }

    public static void ClearTether(Character* chara, byte slot)
        => SetTether(chara, slot, 0, default, 0);

    private static ActorVfxCreateDelegate? ResolveActorVfxCreate()
    {
        if (actorVfxCreate != null) return actorVfxCreate;
        try
        {
            var addr = Plugin.SigScanner.ScanText("40 53 55 56 57 48 81 EC ?? ?? ?? ?? 0F 29 B4 24 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 0F B6 AC 24 ?? ?? ?? ?? 0F 28 F3 49 8B F8");
            actorVfxCreate = Marshal.GetDelegateForFunctionPointer<ActorVfxCreateDelegate>(addr);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"VfxFunctions: failed to resolve ActorVfxCreate: {ex.Message}");
        }
        return actorVfxCreate;
    }

    // ActorVfxRemove sig sits at the start of an instruction sequence whose 8th byte is
    // the start of an LEA <reg>, [rip+disp32] that loads the actual function pointer.
    // We skip the 7 bytes of `0F 11 48 10 48 8D 05`, read the disp32, and follow the
    // RIP-relative reference to the real address. Same dance VFXEditor / raids-rewritten use.
    private static ActorVfxRemoveDelegate? ResolveActorVfxRemove()
    {
        if (actorVfxRemove != null) return actorVfxRemove;
        try
        {
            var sig = Plugin.SigScanner.ScanText("0F 11 48 10 48 8D 05");
            var temp = sig + 7;
            var disp = Marshal.ReadInt32(temp);
            var addr = Marshal.ReadIntPtr(temp + disp + 4);
            actorVfxRemove = Marshal.GetDelegateForFunctionPointer<ActorVfxRemoveDelegate>(addr);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"VfxFunctions: failed to resolve ActorVfxRemove: {ex.Message}");
        }
        return actorVfxRemove;
    }

    public static StaticVfxStruct* SpawnStaticVfx(string path, Vector3 position, float rotation, Vector3 scale)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var create = ResolveStaticVfxCreate();
        var run = ResolveStaticVfxRun();
        if (create == null || run == null) return null;

        poolBytes ??= Encoding.UTF8.GetBytes("Client.System.Scheduler.Instance.VfxObject\0");

        var pathBytes = Encoding.UTF8.GetBytes(path + "\0");
        StaticVfxStruct* vfx;
        fixed (byte* pathPtr = pathBytes)
        fixed (byte* poolPtr = poolBytes)
        {
            vfx = (StaticVfxStruct*)create(pathPtr, poolPtr);
        }
        if (vfx == null) return null;

        vfx->Position = position;
        var q = Quaternion.CreateFromYawPitchRoll(rotation, 0f, 0f);
        vfx->Rotation = q;
        vfx->Scale = scale;
        vfx->Flags |= 0x2;          // mark dirty so position/rotation/scale apply
        vfx->SomeFlags &= 0xF7;     // clear flag that sometimes hides the vfx

        run((IntPtr)vfx, 0f, 0xFFFFFFFF);
        return vfx;
    }

    public static void RemoveStaticVfx(StaticVfxStruct* vfx)
    {
        if (vfx == null) return;
        var fn = ResolveStaticVfxRemove();
        if (fn == null) return;
        fn((IntPtr)vfx);
    }

    // Spawns an entity-attached VFX (follows caster/target, used for head markers and
    // similar continuous effects). Path must exist in game data — caller validates.
    public static IntPtr SpawnActorVfx(string path, Character* caster, Character* target)
    {
        if (string.IsNullOrEmpty(path) || caster == null || target == null) return IntPtr.Zero;
        var fn = ResolveActorVfxCreate();
        if (fn == null) return IntPtr.Zero;

        var bytes = Encoding.UTF8.GetBytes(path + "\0");
        fixed (byte* p = bytes)
        {
            return fn(p, (IntPtr)caster, (IntPtr)target, -1f, 0, 0, 0);
        }
    }

    public static void RemoveActorVfx(IntPtr vfx)
    {
        if (vfx == IntPtr.Zero) return;
        var fn = ResolveActorVfxRemove();
        if (fn == null) return;
        fn(vfx, 0);
    }
}
