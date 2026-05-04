using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using UltiSim.Core.SimObjects;

namespace UltiSim.Core;

// Spawns and configures the eight party members around the scenario origin.
// Reads PartyPresets for the player's job (the player's own slot is null in
// the preset list — that role gets the SimPlayer reference instead), builds
// each non-player BattleChara as a Lalafell PC, registers it in
// CharacterManager, and stores the resulting SimPartyMember (or SimPlayer)
// into the supplied SimParty. Game is the entry point — it computes the
// origin and delegates here.
internal static unsafe class PartyCreator
{
    private const byte RaceLalafell = 3;
    private const byte TribePlainsfolk = 5;
    private const byte SexFemale = 1;
    private const byte BodyTypeAdult = 1;

    private const float RingRadius = 2.5f;
    private const float RadiusJitter = 0.6f;
    private const float AngleJitter = 0.4f;

    private static readonly Random Rng = new();

    public static void Populate(SimParty party, SimPlayer player, uint playerJob, Vector3 origin)
    {
        var presets = PartyPresets.ForPlayerJob(playerJob);
        var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();

        for (int i = 0; i < presets.Count; i++)
        {
            var preset = presets[i];
            if (preset == null)
            {
                // Player's own job slot — wire the SimPlayer in directly so
                // Party.Get(role) returns a uniform SimCharacter.
                party.SetSlot((PartyRole)i, player);
                continue;
            }

            var angle = (i / (float)presets.Count) * MathF.Tau
                        + ((float)Rng.NextDouble() - 0.5f) * AngleJitter;
            var distance = RingRadius + ((float)Rng.NextDouble() - 0.5f) * RadiusJitter;
            var pos = new Vector3(
                origin.X + MathF.Sin(angle) * distance,
                origin.Y,
                origin.Z + MathF.Cos(angle) * distance);
            var facingPlayer = MathF.Atan2(origin.X - pos.X, origin.Z - pos.Z);

            var member = Spawn(preset, (PartyRole)i, pos, facingPlayer, itemSheet);
            if (member != null) party.SetSlot((PartyRole)i, member);
        }
    }

    private static SimPartyMember? Spawn(PartyMemberPreset preset, PartyRole role, Vector3 position, float rotation, ExcelSheet<Item> itemSheet)
    {
        if (!BattleCharaSpawn.CreateBattleChara(out var idx, out var obj)) return null;

        var chara = (BattleChara*)obj;
        chara->ObjectKind = ObjectKind.Pc;
        chara->Position = position;
        chara->Rotation = MathUtil.NormalizeRotation(rotation);
        chara->Scale = 1f;
        chara->ModelContainer.ModelCharaId = 0;
        chara->ModelContainer.ModelSkeletonId = 0;

        WriteCustomize(chara);
        WriteEquipment(chara, preset, itemSheet);
        BattleCharaSpawn.WriteName(obj, preset.Name);
        obj->RenderFlags = 0;

        chara->TargetableStatus = ObjectTargetableFlags.IsTargetable;
        chara->HitboxRadius = 0.5f;
        chara->MaxHealth = 100_000;
        chara->Health = 100_000;
        chara->MaxMana = 10_000;
        chara->Mana = 10_000;
        chara->Battalion = 0;
        chara->IsHostile = false;
        chara->InCombat = false;
        chara->IsPartyMember = true;
        chara->IsAllianceMember = false;
        chara->IsFriend = false;
        chara->IsOffhandDrawn = false;
        chara->Timeline.IsWeaponDrawn = false;
        chara->CastInfo.IsCasting = false;
        chara->Mode = CharacterModes.Normal;
        chara->ModeParam = 0;
        chara->ClassJob = preset.ClassJob;
        chara->Level = preset.Level;

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player != null)
        {
            var localChara = (Character*)player.Address;
            chara->HomeWorld = localChara->HomeWorld;
            chara->CurrentWorld = localChara->CurrentWorld;
        }

        SimPartyMember.RegisterInCharacterManager(chara);

        Plugin.Log.Info($"PartyCreator: spawned {preset.Name} ({role}, job {preset.ClassJob}) at index {idx}");
        return new SimPartyMember(idx, role, preset.ClassJob, preset.Name);
    }

    private static void WriteCustomize(BattleChara* chara)
    {
        ref var c = ref chara->DrawData.CustomizeData;
        c.Race = RaceLalafell;
        c.Sex = SexFemale;
        c.BodyType = BodyTypeAdult;
        c.Height = 50;
        c.Tribe = TribePlainsfolk;
        c.Face = 1;
        c.Hairstyle = 1;
        c.SkinColor = 1;
        c.EyeColorRight = 1;
        c.EyeColorLeft = 1;
        c.HairColor = 1;
        c.HighlightsColor = 1;
        c.TattooColor = 1;
        c.Eyebrows = 1;
        c.Nose = 1;
        c.Jaw = 1;
        c.LipColorFurPattern = 1;
        c.MuscleMass = 50;
        c.TailShape = 1;
        c.BustSize = 50;
        c.FacePaintColor = 1;
    }

    private static void WriteEquipment(BattleChara* chara, PartyMemberPreset preset, ExcelSheet<Item> itemSheet)
    {
        ApplyItem(chara, DrawDataContainer.EquipmentSlot.Head, preset.Head, itemSheet);
        ApplyItem(chara, DrawDataContainer.EquipmentSlot.Body, preset.Body, itemSheet);
        ApplyItem(chara, DrawDataContainer.EquipmentSlot.Hands, preset.Hands, itemSheet);
        ApplyItem(chara, DrawDataContainer.EquipmentSlot.Legs, preset.Legs, itemSheet);
        ApplyItem(chara, DrawDataContainer.EquipmentSlot.Feet, preset.Feet, itemSheet);
    }

    private static void ApplyItem(BattleChara* chara, DrawDataContainer.EquipmentSlot slot, uint itemRowId, ExcelSheet<Item> itemSheet)
    {
        if (itemRowId == 0) return;
        if (!itemSheet.TryGetRow(itemRowId, out var item))
        {
            Plugin.Log.Warning($"PartyCreator: Item row {itemRowId} for slot {slot} not found");
            return;
        }
        chara->DrawData.Equipment(slot).Value = item.ModelMain;
    }
}
