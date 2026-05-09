using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace UltiSim.Core.SimObjects;

public sealed unsafe class SimPartyMember : SimNpc
{
    public byte ClassJob { get; }
    public string DisplayName { get; }

    internal SimPartyMember(uint index, PartyRole role, byte classJob, string name) : base(index)
    {
        Role = role;
        ClassJob = classJob;
        DisplayName = name;
    }

    private float deadElapsed;

    internal override void OnKilled()
    {
        StopMoving();
        var bc = BattleCharaPtr;
        if (bc == null) return;
        ApplyDeadState(bc);
        KoAnimation.Play(bc);
        deadElapsed = 0f;
    }

    // Re-stamp dead-state each frame so engine-driven resets (status ticks,
    // packets, transient animation state changes) can't sit them back up.
    // BaseOverride pin is delayed until after the intro to avoid skipping the
    // fall animation.
    public override void Tick(float deltaSeconds)
    {
        base.Tick(deltaSeconds);
        if (!Dead) return;
        var bc = BattleCharaPtr;
        if (bc == null) return;
        ApplyDeadState(bc);
        deadElapsed += deltaSeconds;
        if (deadElapsed >= KoAnimation.IntroDurationSeconds) KoAnimation.PinLoop(bc);
    }

    // The "the engine considers this character dead" state: HP=0 and MP=0 so
    // the party-list slot reads as KO'd, and CharacterMode=Dead so internal
    // engine paths (party UI dead overlay, targeting, etc.) treat them as a
    // corpse. Direct field writes — we already manage animation explicitly,
    // so SetMode's side effects aren't wanted here.
    private static void ApplyDeadState(BattleChara* bc)
    {
        if (bc->Health != 0) bc->Health = 0;
        if (bc->Mana != 0) bc->Mana = 0;
        var ch = (Character*)bc;
        if (ch->Mode != CharacterModes.Dead) ch->Mode = CharacterModes.Dead;
    }

}
