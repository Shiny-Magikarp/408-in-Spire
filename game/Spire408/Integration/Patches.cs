using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Spire408.Models;
using Spire408.UI;

namespace Spire408.Integration;

[HarmonyPatch(typeof(NCharacterSelectScreen), "OnEmbarkPressed")]
internal static class EmbarkPatch
{
    private static readonly HashSet<NCharacterSelectScreen> Bypass = [];
    private static bool Prefix(NCharacterSelectScreen __instance, StartRunLobby ____lobby)
    {
        if (Bypass.Remove(__instance)) return true;
        if (!ModEntry.Ready || ____lobby.NetService.Type != NetGameType.Singleplayer || ModEntry.PendingRun != null) return true;
        ModEntry.ShowSetup(() =>
        {
            Bypass.Add(__instance);
            try { AccessTools.Method(typeof(NCharacterSelectScreen), "OnEmbarkPressed").Invoke(__instance, [null]); }
            finally { Bypass.Remove(__instance); }
        });
        return false;
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuClosed))]
internal static class ClearPendingSetup
{
    private static void Postfix() => ModEntry.PendingRun = null;
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewSingleplayer))]
internal static class NewRunPatch
{
    private static void Postfix(RunState state)
    {
        if (ModEntry.PendingRun is not { } run) return;
        ModEntry.PendingRun = null;
        ModEntry.Runs.Set(state, run);
        if (run.Enabled) foreach (var player in state.Players)
        {
            player.Gold = 0;
            player.AddRelicInternal(ModelDb.Relic<StudyBook>().ToMutable());
        }
    }
}

[HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.GainGold))]
internal static class BlockGoldCommand
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Player player, ref Task __result)
    {
        if (ModEntry.Active(player.RunState) == null || IntrinsicGoldScope.Allows(player)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.Gold), MethodType.Setter)]
internal static class BlockGoldSetter
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Player __instance, int value)
    {
        if (value <= __instance.Gold || GoldGate.Consume(__instance, value) || IntrinsicGoldScope.Allows(__instance)) return true;
        // Constructor/load objects are not members of the live run, so their saved balance is preserved.
        var live = RunManager.Instance.DebugOnlyGetState();
        return live == null || !live.Players.Any(p => ReferenceEquals(p, __instance)) || ModEntry.Active(live) == null;
    }
}

[HarmonyPatch(typeof(RewardsSet), nameof(RewardsSet.GenerateWithoutOffering))]
internal static class HideGoldReward
{
    private static void Postfix(RewardsSet __instance, ref Task __result)
    {
        if (ModEntry.Active(__instance.Player.RunState) == null) return;
        __result = Filter(__result, __instance);
    }
    private static async Task Filter(Task original, RewardsSet set)
    {
        await original;
        set.Rewards.RemoveAll(r => r is GoldReward gold && !IntrinsicGoldRewards.Allows(gold));
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatVictory))]
internal static class VictoryPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(IRunState runState, ref Task __result) => __result = Finish(__result, runState);
    private static async Task Finish(Task original, IRunState state)
    {
        await original;
        if (state is RunState runState) ModEntry.Settle(runState);
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.OnPlayWrapper))]
internal static class QuizBeforeCard
{
    private static readonly HashSet<CardModel> Resume = [];
    private static bool Prefix(CardModel __instance, PlayerChoiceContext choiceContext, Creature? target,
        bool isAutoPlay, ResourceInfo resources, bool skipCardPileVisuals, ref Task __result)
    {
        if (Resume.Remove(__instance)) return true;
        if (isAutoPlay || ModEntry.Active(__instance.Owner.RunState) == null) return true;
        if (!StudyActivation.Consume(choiceContext, __instance)) return true;
        __result = Play(__instance, choiceContext, target, resources, skipCardPileVisuals);
        return false;
    }
    private static async Task Play(CardModel card, PlayerChoiceContext ctx, Creature? target, ResourceInfo resources, bool skip)
    {
        var combatId = CombatManager.Instance.CurrentCombatId;
        bool boosted = await ModEntry.Quiz(card.Owner, card.Owner.PlayerCombatState?.TurnNumber ?? 0);
        if (!CombatManager.Instance.IsCurrentLiveCombat(combatId)) return;
        if (boosted) StudyCardBoost.Begin(card);
        try
        {
            Resume.Add(card);
            Task action;
            try { action = card.OnPlayWrapper(ctx, target, false, resources, skip); }
            finally { Resume.Remove(card); }
            await action;
        }
        finally { if (boosted) StudyCardBoost.End(card); }
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
internal static class CombatStudyHudPatch
{
    private static void Postfix(NCombatUi __instance, CombatState state)
    {
        if (ModEntry.Active(state.RunState) is not { } run || state.RunState is not RunState runState) return;
        ModEntry.EnsureBattle(runState, run);
        ModEntry.Persist(runState, run);
        if (__instance.GetNodeOrNull<StudyCombatHud>("StudyCombatHud") != null) return;
        var hud = GD.Load<PackedScene>("res://Spire408/Scenes/StudyCombatHud.tscn").Instantiate<StudyCombatHud>();
        hud.Configure(runState);
        __instance.AddChild(hud);
    }
}
