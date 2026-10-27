using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace Spire408.Integration;

// A scope lasts exactly as long as the original manual OnPlayWrapper task.
// It never changes a card's base values or leaks into queued automatic plays.
internal static class StudyCardBoost
{
    private static readonly HashSet<CardModel> Active = [];
    public static void Begin(CardModel card) => Active.Add(card);
    public static void End(CardModel card) => Active.Remove(card);
    public static bool Applies(CardModel? card, CardPlay? play) => card != null && play != null
        && ReferenceEquals(play.Card, card) && !play.IsAutoPlay && Active.Contains(card);
}
