using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using UltiSim.Core.SimObjects;
using Action = Lumina.Excel.Sheets.Action;

namespace UltiSim.Core;

// Mirrors the spawned-enemy list into UIState.Hate / UIState.Hater each frame
// so the in-game enmity HUD shows our enemies. Hate/Hater don't carry cast info
// and the game's HudManager copier doesn't read CastInfo from our spawned
// BattleChara, so the cast bar stays empty if we only write Hate/Hater.
//
// Direct AtkArrayData writes during Framework.Update don't stick either —
// Dalamud's Update fires before CSFramework::Tick, and the copier overwrites
// our slot writes. Instead we hook _EnemyList's PreRequestedUpdate, which
// fires right before the addon reads its arrays (after the copier), and
// inject CastPercent / Castname there.
//
// Requires each SimEnemy to be registered in CharacterManager._battleCharas so the
// HudManager's EntityId lookup succeeds. See reference_charactermanager_register.md
// for the low-density zone caveat.
internal sealed unsafe class EnmityHud : IDisposable
{
    private const int EnemyListSize = 8;
    private const string AddonName = "_EnemyList";

    private readonly SimEnemy?[] slotEnemies = new SimEnemy?[EnemyListSize];

    public EnmityHud()
    {
         Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, AddonName, OnPreRequestedUpdate);
    }

    public void Refresh(IEnumerable<SimEnemy> enemies)
    {
        Array.Clear(slotEnemies);

        var ui = UIState.Instance();
        if (ui == null) return;

        
        ref var hate = ref ui->Hate;
        ref var hater = ref ui->Hater;
        
        var written = 0;
        foreach (var e in enemies)
        {
            if (written >= 32) break;
            if (!e.InEnemyList) continue;
            var entityId = e.EntityId;
            if (entityId == 0) continue;

            hate.HateInfo[written].EntityId = entityId;
            hate.HateInfo[written].Enmity = 100;

            ref var slot = ref hater.Haters[written];
            slot.EntityId = entityId;
            slot.Enmity = 100;
            WriteName(ref slot, e.DisplayName);

            if (written < slotEnemies.Length) slotEnemies[written] = e;
            written++;
        }

        if (written == 0)
        {
            hate.HateArrayLength = 0;
            hate.HateTargetId = 0;
            hater.HaterCount = 0;
            return;
        }

        hate.HateArrayLength = written;
        hate.HateTargetId = hater.Haters[0].EntityId;
        hater.HaterCount = written;
    }

    public void Clear()
    {
        var ui = UIState.Instance();
        if (ui != null)
        {
            ui->Hate.HateArrayLength = 0;
            ui->Hate.HateTargetId = 0;
            ui->Hater.HaterCount = 0;
        }
        Array.Clear(slotEnemies);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreRequestedUpdate, AddonName, OnPreRequestedUpdate);
    }

    // Fired by Dalamud right before _EnemyList reads its AtkArrayData each frame.
    // The game's HudManager copier has already populated HP / EntityId / etc by
    // this point; we slot in cast info on top, keyed by our slotEnemies order
    // (which matches the order we wrote to Hater).
    private void OnPreRequestedUpdate(AddonEvent type, AddonArgs args)
    {
        if (args is not AddonRequestedUpdateArgs reqArgs) return;
        var numArrays = (NumberArrayData**)reqArgs.NumberArrayData;
        var strArrays = (StringArrayData**)reqArgs.StringArrayData;
        if (numArrays == null || strArrays == null) return;

        var numArr = numArrays[(int)NumberArrayType.EnemyList];
        var strArr = strArrays[(int)StringArrayType.EnemyList];
        if (numArr == null || strArr == null) return;

        var actionSheet = Plugin.DataManager.GetExcelSheet<Action>();

        for (int i = 0; i < EnemyListSize; i++)
        {
            var e = slotEnemies[i];
            var castPercent = -1;
            var castName = string.Empty;

            if (e is { IsCasting: true })
            {
                castPercent = (int)Math.Clamp(e.CastProgress * 100f, 0f, 100f);
                if (e.CastActionId != 0 && actionSheet.TryGetRow(e.CastActionId, out var action))
                    castName = action.Name.ExtractText() ?? string.Empty;
            }

            numArr->SetValue(5 + i * 6 + 2, castPercent);
            strArr->SetValue(1 + i * 2, castName, managed: true);
        }
    }

    private static void WriteName(ref HaterInfo slot, string name)
    {
        var max = Math.Min(name.Length, 63);
        for (int i = 0; i < max; i++) slot.Name[i] = (byte)name[i];
        slot.Name[max] = 0;
    }
}
