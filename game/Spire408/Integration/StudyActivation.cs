using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using Spire408.Core;

namespace Spire408.Integration;

internal static class StudyActivation
{
    private sealed record Ticket(CardModel Card, CombatId? Combat, string RunId);
    private static readonly ConditionalWeakTable<PlayCardAction, Ticket> Tickets = new();

    public static void Reserve(PlayCardAction action, CardModel card)
    {
        if (card.Owner.RunState is not RunState state || ModEntry.Active(state) is not { } run
            || !CombatManager.Instance.IsInProgress || card.Owner.PlayerCombatState?.Phase != PlayerTurnPhase.Play) return;
        ModEntry.EnsureBattle(state, run);
        if (!run.Battle.Armed) return;
        bool reserved = StudyEngine.TakeActivation(run);
        ModEntry.Persist(state, run);
        if (reserved) Tickets.Add(action, new Ticket(card, CombatManager.Instance.CurrentCombatId, run.RunId));
    }

    public static bool Consume(PlayerChoiceContext context, CardModel card)
    {
        if (context is not GameActionPlayerChoiceContext { Action: PlayCardAction action }
            || !Tickets.TryGetValue(action, out var ticket)) return false;
        Tickets.Remove(action);
        return ReferenceEquals(ticket.Card, card) && CombatManager.Instance.IsCurrentLiveCombat(ticket.Combat)
            && ModEntry.Active(card.Owner.RunState) is { } run && ticket.RunId == run.RunId;
    }
}

// Capture intent before enqueueing, so cards already in the queue cannot steal it.
[HarmonyPatch(typeof(PlayCardAction), MethodType.Constructor, new[] { typeof(CardModel), typeof(Creature) })]
internal static class ReserveStudyActivationPatch
{
    private static void Postfix(PlayCardAction __instance, CardModel cardModel) => StudyActivation.Reserve(__instance, cardModel);
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.OnEndedTurnLocally))]
internal static class ClearStudyActivationAtEndTurn
{
    private static void Prefix()
    {
        if (RunManager.Instance.DebugOnlyGetState() is not { } state || ModEntry.Active(state) is not { } run
            || !run.Battle.Armed) return;
        run.Battle.Armed = false;
        ModEntry.Persist(state, run);
    }
}
