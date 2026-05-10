using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Component.GUI;
using UltiSim.Core.SimObjects;
using Action = Lumina.Excel.Sheets.Action;

namespace UltiSim.Core;

// Drives the in-game _EnemyList addon by writing rows directly into its
// NumberArray / StringArray during PreRequestedUpdate. We deliberately don't go
// through UIState.Hate/Hater + HudManager copier — that path requires the BC to
// be findable via CharacterManager.LookupBattleCharaByEntityId, which means
// inserting the BC into CharacterManager._battleCharas, which triggers a per-frame
// CharacterManager update that attaches the BC into render-side caches (Skeleton,
// CharacterLookAtController). Those attachments aren't drained by
// DeleteObjectByIndex, so freeing the BC produces a ghost render that crashes the
// next render task on a freed Skeleton vtable.
//
// Writing the addon arrays directly avoids the resolver lookup entirely. The
// addon doesn't care that the EntityId doesn't resolve to a real BC — it just
// renders whatever we put in the slots. Layout comes from the typed
// EnemyListNumberArray / EnemyListStringArray wrappers in FFXIVClientStructs.
internal sealed unsafe class EnmityHud : IDisposable
{
    private const int EnemyListSize = 8;
    private const string AddonName = "_EnemyList";

    // Within EnemyListStringArray each member is { EnemyName at +0, Castname at +1 }.
    // String writes have to go through StringArrayData.SetValue (managed: true) so
    // the engine owns the byte buffer; we can't assign CStringPointer fields directly.
    private const int StrEnemyName = 0;
    private const int StrCastname = 1;

    private readonly SimEnemy?[] slotEnemies = new SimEnemy?[EnemyListSize];
    private bool wasActive;

    public EnmityHud()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, AddonName, OnPreRequestedUpdate);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreRequestedUpdate, AddonName, OnPreRequestedUpdate);
    }

    // Snapshot which enemies should occupy each addon slot. Called from
    // SimWorld.Tick once per frame; OnPreRequestedUpdate reads slotEnemies later
    // in the same frame to populate the addon arrays.
    public void Refresh(IEnumerable<SimEnemy> enemies)
    {
        Array.Clear(slotEnemies);
        var written = 0;
        foreach (var e in enemies)
        {
            if (written >= EnemyListSize) break;
            if (!e.InEnemyList) continue;
            if (!e.IsAlive) continue;
            slotEnemies[written++] = e;
        }

        // The PreRequestedUpdate hook only fires when the engine sees a
        // subscribed array dirty (UpdateState != 0). Without this nudge the
        // addon stays empty until something else (e.g. the player selecting
        // an enemy) dirties Hate/Hater. Keep nudging for one frame after
        // slots empty so the listener clears our last-written rows.
        var nowActive = written > 0;
        if (nowActive || wasActive) MarkArraysDirty();
        wasActive = nowActive;
    }

    public void Clear()
    {
        Array.Clear(slotEnemies);
        wasActive = false;
    }

    private void OnPreRequestedUpdate(AddonEvent type, AddonArgs args)
    {
        // No scenario running — all slots null after Clear(). Let the game
        // populate the list without interference.
        var anyTracked = false;
        for (int i = 0; i < EnemyListSize; i++)
            if (slotEnemies[i] != null) { anyTracked = true; break; }
        if (!anyTracked) return;

        if (args is not AddonRequestedUpdateArgs reqArgs) return;
        var numArrays = (NumberArrayData**)reqArgs.NumberArrayData;
        var strArrays = (StringArrayData**)reqArgs.StringArrayData;
        if (numArrays == null || strArrays == null) return;

        var numArr = numArrays[(int)NumberArrayType.EnemyList];
        var strArr = strArrays[(int)StringArrayType.EnemyList];
        if (numArr == null || strArr == null) return;
        var enemyArr = (EnemyListNumberArray*)numArr->IntArray;
        if (enemyArr == null) return;

        var actionSheet = Plugin.DataManager.GetExcelSheet<Action>();

        var activeCount = 0;
        var firstEntityId = 0;
        for (int i = 0; i < EnemyListSize; i++)
        {
            var e = slotEnemies[i];
            if (e is null || !e.IsAlive)
            {
                WriteEmptyRow(ref enemyArr->Enemies[i], strArr, i);
                continue;
            }

            activeCount++;
            var entityId = (int)e.EntityId;
            if (firstEntityId == 0) firstEntityId = entityId;

            var castPercent = -1;
            var castName = string.Empty;
            if (e.IsCasting)
            {
                castPercent = (int)Math.Clamp(e.CastProgress * 100f, 0f, 100f);
                if (e.CastActionId != 0 && actionSheet.TryGetRow(e.CastActionId, out var action))
                    castName = action.Name.ExtractText() ?? string.Empty;
            }

            ref var enemy = ref enemyArr->Enemies[i];
            enemy.RemainingHPPercent = 100;
            enemy.MaxHPPercent = 100;
            enemy.CastPercent = castPercent;
            enemy.EntityId = entityId;
            enemy.ActiveInList = true;

            strArr->SetValue(i * 2 + StrEnemyName, e.DisplayName, managed: true);
            strArr->SetValue(i * 2 + StrCastname, castName, managed: true);
        }

        enemyArr->EnemyCount = activeCount;
        enemyArr->TargetEntityId = firstEntityId;
    }

    private static void MarkArraysDirty()
    {
        var holder = AtkStage.Instance()->AtkArrayDataHolder;
        if (holder == null) return;
        var numArr = holder->GetNumberArrayData((int)NumberArrayType.EnemyList);
        var strArr = holder->GetStringArrayData((int)StringArrayType.EnemyList);
        if (numArr != null) numArr->UpdateState = 1;
        if (strArr != null) strArr->UpdateState = 1;
    }

    private static void WriteEmptyRow(ref EnemyListNumberArray.EnemyListEnemyNumberArray enemy, StringArrayData* strArr, int i)
    {
        enemy.RemainingHPPercent = 0;
        enemy.MaxHPPercent = 0;
        enemy.CastPercent = -1;
        enemy.EntityId = 0;
        enemy.ActiveInList = false;

        strArr->SetValue(i * 2 + StrEnemyName, string.Empty, managed: true);
        strArr->SetValue(i * 2 + StrCastname, string.Empty, managed: true);
    }
}
