using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Spire408.Integration;

// An execution-context permit follows awaited native effects, never unrelated game tasks.
// The native method's synchronous caller is restored as soon as it returns its Task.
internal sealed class IntrinsicGoldScope
{
    private static readonly AsyncLocal<IntrinsicGoldScope?> Current = new();
    private readonly IntrinsicGoldScope? _previous;
    private readonly Player _player;
    private bool _active = true, _restored;
    private IntrinsicGoldScope(Player player) { _player = player; _previous = Current.Value; Current.Value = this; }
    public static IntrinsicGoldScope? Enter(Player player) => ModEntry.Active(player.RunState) != null ? new(player) : null;
    public static bool Allows(Player player)
    {
        for (var scope = Current.Value; scope != null; scope = scope._previous)
            if (scope._active && ReferenceEquals(scope._player, player)) return true;
        return false;
    }
    public void RestoreCaller() { if (_restored) return; Current.Value = _previous; _restored = true; }
    public async Task Complete(Task task) { try { await task; } finally { _active = false; } }
    public async Task<T> Complete<T>(Task<T> task) { try { return await task; } finally { _active = false; } }
    public void Abort() { RestoreCaller(); _active = false; }
}

[HarmonyPatch]
internal static class NativeIntrinsicGoldPatch
{
    // All direct GainGold callers belonging to native cards/relics in game 0.111.0.
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(HandOfGreed), "OnPlay");
        yield return AccessTools.Method(typeof(SpoilsMap), "OnQuestComplete");
        foreach (var type in new[] {typeof(CursedPearl), typeof(GoldenPearl), typeof(OldCoin), typeof(SignetRing)})
            yield return AccessTools.Method(type, "AfterObtained");
        yield return AccessTools.Method(typeof(LuckyFysh), "AfterCardChangedPiles");
        yield return AccessTools.Method(typeof(MawBank), "AfterRoomEntered");
    }
    private static void Prefix(object __instance, out IntrinsicGoldScope? __state)
    {
        var player = __instance switch { CardModel card => card.Owner, RelicModel relic => relic.Owner, _ => null };
        __state = player != null ? IntrinsicGoldScope.Enter(player) : null;
    }
    private static void Postfix(ref Task __result, IntrinsicGoldScope? __state)
    {
        if (__state == null) return;
        __state.RestoreCaller();
        __result = __state.Complete(__result);
    }
    private static Exception? Finalizer(Exception? __exception, IntrinsicGoldScope? __state)
    {
        if (__exception != null) __state?.Abort();
        return __exception;
    }
}

internal static class IntrinsicGoldRewards
{
    public static bool Allows(GoldReward reward)
    {
        if (reward.Player.RunState is not RunState state || ModEntry.Active(state) is not { } run
            || state.CurrentRoom is not CombatRoom room || !room.ExtraRewards.TryGetValue(reward.Player, out var rewards)) return false;
        int index = rewards.IndexOf(reward);
        return index >= 0 && run.IntrinsicGoldRewardKeys.Contains(ModEntry.BattleId(state) + "/" + index);
    }
}

// Royalties is paid through a selectable combat reward, rather than GainGold in its hook.
// Save the extra-reward index so source information also survives a native save/load.
[HarmonyPatch(typeof(RoyaltiesPower), nameof(RoyaltiesPower.AfterCombatEnd))]
internal static class RoyaltiesGoldSourcePatch
{
    private static void Prefix(RoyaltiesPower __instance, CombatRoom room, out int __state) =>
        __state = __instance.Owner.Player is { } player && room.ExtraRewards.TryGetValue(player, out var rewards) ? rewards.Count : 0;
    private static void Postfix(RoyaltiesPower __instance, CombatRoom room, int __state, ref Task __result)
    {
        if (__instance.Owner.Player is { } player) __result = Record(__result, player, room, __state);
    }
    private static async Task Record(Task original, Player player, CombatRoom room, int start)
    {
        await original;
        if (player.RunState is not RunState state || ModEntry.Active(state) is not { } run
            || !room.ExtraRewards.TryGetValue(player, out var rewards)) return;
        for (int i = start; i < rewards.Count; i++)
            if (rewards[i] is GoldReward) run.IntrinsicGoldRewardKeys.Add(ModEntry.BattleId(state) + "/" + i);
        ModEntry.Persist(state, run);
    }
}

[HarmonyPatch(typeof(GoldReward), "OnSelect")]
internal static class SelectIntrinsicGoldRewardPatch
{
    private static void Prefix(GoldReward __instance, out IntrinsicGoldScope? __state) =>
        __state = IntrinsicGoldRewards.Allows(__instance) ? IntrinsicGoldScope.Enter(__instance.Player) : null;
    private static void Postfix(ref Task<bool> __result, IntrinsicGoldScope? __state)
    {
        if (__state == null) return;
        __state.RestoreCaller();
        __result = __state.Complete(__result);
    }
    private static Exception? Finalizer(Exception? __exception, IntrinsicGoldScope? __state)
    {
        if (__exception != null) __state?.Abort();
        return __exception;
    }
}
