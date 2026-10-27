using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using Spire408.AI;
using Spire408.Core;
using Spire408.UI;

namespace Spire408.Integration;

[HarmonyPatch(typeof(EventModel), "SetEventState")]
internal static class EssayEventOptionsPatch
{
    private static void Prefix(EventModel __instance, ref LocString description, ref IEnumerable<EventOption> eventOptions)
    {
        if (ModEntry.Active(__instance.Owner?.RunState) is not { } run) return;
        var original = eventOptions.ToList();
        // Normal native events restart at their initial page after loading. A
        // completed study transaction must restore its ending, not offer a
        // second, unrelated native option on that page.
        if (original.Count > 0 && !EssayEvents.TransactionInProgress
            && run.EssayEvents.TryGetValue(EssayEvents.Key(__instance, run), out var saved)
            && saved.OriginalFinished && saved.Claimed)
        {
            description = EssayEvents.CompletionDescription(__instance, run)!;
            eventOptions = Array.Empty<EventOption>();
            return;
        }
        if (original.Count == 0)
        {
            description = EssayEvents.CompletionDescription(__instance, run) ?? description;
            return;
        }
        eventOptions = EssayEvents.Wrap(__instance, original, run, description);
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
internal static class EssayTransactionSavePatch
{
    private static bool Prefix(ref Task __result)
    {
        if (!EssayEvents.TransactionInProgress) return true;
        // Keep the last complete room snapshot while the reward chooser and
        // native option are running. The separate profile already contains
        // the draft and immutable first grade. Save+quit can safely resume
        // from that snapshot instead of committing half of the transaction.
        __result = Task.CompletedTask;
        return false;
    }
}

public static class EssayEvents
{
    private static readonly System.Reflection.MethodInfo SetState = AccessTools.Method(typeof(EventModel), "SetEventState");
    private static readonly System.Reflection.MethodInfo Finish = AccessTools.Method(typeof(EventModel), "SetEventFinished");
    private static readonly HashSet<string> BusyEvents = [];
    public static bool TransactionInProgress => BusyEvents.Count > 0;
    private static readonly HashSet<string> BonusRelics = ["ANCHOR", "BAG_OF_PREPARATION", "BRONZE_SCALES", "LANTERN",
        "ODDLY_SMOOTH_STONE", "VAJRA", "PEN_NIB", "ORICHALCUM", "THE_BOOT", "MERCURY_HOURGLASS", "ART_OF_WAR"];

    public static string Key(EventModel model, StudyRun run) => $"{run.RunId}/essay/{model.Owner!.RunState.CurrentActIndex}/{model.Owner.RunState.TotalFloor}/{model.Id.Entry}";
    public static LocString? CompletionDescription(EventModel model, StudyRun run)
    {
        string key = Key(model, run);
        if (!run.EssayEvents.TryGetValue(key, out var challenge) || !challenge.Claimed
            || (!BusyEvents.Contains(key) && !challenge.OriginalFinished)) return null;
        var text = new LocString("events", "SPIRE408_CHALLENGE.finished");
        text.Add("Score", challenge.Attempt!.Score); text.Add("Gold", challenge.Gold); text.Add("Cost", challenge.Cost);
        return text;
    }
    public static string? CostFor(EventModel model, string key)
    {
        if (key.StartsWith("COLOSSAL_FLOWER.pages.", StringComparison.Ordinal)
            && (key.Contains(".options.EXTRACT_CURRENT_PRIZE_", StringComparison.Ordinal) || key.EndsWith(".options.EXTRACT_INSTEAD", StringComparison.Ordinal))) return "无新增代价；此前挖掘造成的伤害保留";
        return key switch
        {
            "SUNKEN_TREASURY.pages.INITIAL.options.FIRST_CHEST" => "无",
            "SUNKEN_TREASURY.pages.INITIAL.options.SECOND_CHEST" => "将一张“贪婪”诅咒加入牌组",
            "DENSE_VEGETATION.pages.INITIAL.options.TRUDGE_ON" => $"失去 {model.DynamicVars.HpLoss.IntValue} 点生命",
            "JUNGLE_MAZE_ADVENTURE.pages.INITIAL.options.SOLO_QUEST" => $"失去 {model.DynamicVars["SoloHp"].IntValue} 点生命",
            "JUNGLE_MAZE_ADVENTURE.pages.INITIAL.options.JOIN_FORCES" => "无",
            "LOST_WISP.pages.INITIAL.options.SEARCH" => "无",
            "SUNKEN_STATUE.pages.INITIAL.options.DIVE_INTO_WATER" => $"失去 {model.DynamicVars["HpLoss"].IntValue} 点生命",
            "THIS_OR_THAT.pages.INITIAL.options.PLAIN" => $"失去 {model.DynamicVars.HpLoss.IntValue} 点生命",
            "THE_LANTERN_KEY.pages.INITIAL.options.RETURN_THE_KEY" => "无",
            "TRIAL.pages.NOBLE.options.INNOCENT" => "将一张“悔恨”诅咒加入牌组",
            "TRASH_HEAP.pages.INITIAL.options.GRAB" => "无；原选项的一张随机卡牌仍保留",
            "ENDLESS_CONVEYOR.pages.ALL.options.GOLDEN_FYSH" => "无",
            _ => null
        };
    }
    public static IReadOnlyList<EventOption> Wrap(EventModel model, List<EventOption> original, StudyRun run, LocString? pageDescription = null)
    {
        if (run.EssayBankHash.Length == 0) EssayEngine.Initialize(run, ModEntry.Essays, ModEntry.Profile);
        string eventKey = Key(model, run);
        var result = new List<EventOption>(); bool replaced = false;
        var description = pageDescription ?? model.Description ?? model.InitialDescription;
        foreach (var option in original)
        {
            string? cost = CostFor(model, option.TextKey);
            if (cost == null) { result.Add(option); continue; }
            replaced = true;
            bool hasQuestion = run.EssayEvents.ContainsKey(eventKey) || run.EssayCursor < run.EssayDeck.Count;
            bool paid = run.EssayEvents.TryGetValue(eventKey, out var prior) && prior.Claimed;
            string? questionId = prior?.QuestionId ?? run.EssayDeck.ElementAtOrDefault(run.EssayCursor);
            bool needsModel = ModEntry.Essays.Questions.FirstOrDefault(q => q.Id == questionId)?.Parts.Any(p => p.Kind is "concept" or "rubric") == true;
            string reason = !run.Settings.Essays ? "本局未启用大题" : run.EssayBankHash != ModEntry.Essays.Hash ? "大题库版本与存档不一致"
                : !hasQuestion ? "当前筛选没有剩余大题" : needsModel && !WakuuJudge.FilesPresent ? "瓦库离线模型未安装" : paid ? "本事件已结算大题奖励" : "";
            var title = new LocString("events", "SPIRE408_CHALLENGE.title");
            var body = new LocString("events", reason.Length == 0 ? "SPIRE408_CHALLENGE.description" : "SPIRE408_CHALLENGE.locked");
            body.Add("Cost", cost); body.Add("Reason", reason);
            Func<Task>? callback = reason.Length == 0 ? () => Run(model, option, original, description, cost) : null;
            var replacement = new EventOption(model, callback, title, body, option.TextKey, option.HoverTips);
            if (option.WillKillPlayer != null) replacement.ThatWillKillPlayerIf(option.WillKillPlayer);
            result.Add(replacement);
        }
        if (replaced)
        {
            result.Add(new EventOption(model, () =>
            {
                Finish.Invoke(model, [new LocString("events", "SPIRE408_CHALLENGE.leave")]);
                return Task.CompletedTask;
            }, "SPIRE408_LEAVE"));
            if (model.Owner!.RunState is RunState state) ModEntry.Persist(state, run);
        }
        return result;
    }
    public static async Task Run(EventModel model, EventOption original, List<EventOption> originals, LocString description, string cost)
    {
        if (model.Owner?.RunState is not RunState state || ModEntry.Active(state) is not { } run) return;
        string key = Key(model, run);
        if (!BusyEvents.Add(key)) return;
        try
        {
            var challenge = EssayEngine.Issue(run, key, state.CurrentActIndex);
            if (challenge == null) { Restore(); return; }
            ModEntry.Persist(state, run);
            var q = ModEntry.Essays.Questions.Single(x => x.Id == challenge.QuestionId);
            if (ModEntry.Profile.EssayAttempts.TryGetValue(key, out var restored))
            {
                if (restored.QuestionId != q.Id || restored.Revision != q.Revision) throw new InvalidDataException("已提交大题与题库版本不一致");
                challenge.Attempt = restored;
            }
            var tree = (SceneTree)Engine.GetMainLoop();
            while (NModalContainer.Instance?.OpenModal != null) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            var host = NModalContainer.Instance ?? throw new InvalidOperationException("找不到弹窗容器");
            var view = GD.Load<PackedScene>("res://Spire408/Scenes/EssayPanel.tscn").Instantiate<EssayPanel>();
            var closed = new TaskCompletionSource<EssayCloseReason>(TaskCreationOptions.RunContinuationsAsynchronously);
            var draft = challenge.Attempt?.Answers ?? ModEntry.Profile.EssayDrafts.GetValueOrDefault(key) ?? [];
            view.Configure(q, draft, challenge.Attempt, cost, !run.EssayBonusActs.Contains(challenge.Act), async (answers, token) =>
            {
                // The model has no access to game commands. Calculation runs off
                // the Godot thread, and only this fixed rubric can award points.
                var result = await Task.Run(() => EssayEngine.Grade(q, answers, WakuuJudge.JudgeAnswer,
                    q.Parts.Any(p => p.Kind is "concept" or "rubric") ? WakuuJudge.ScoringVersion : "deterministic-rubric", token), token)
                    .WaitAsync(TimeSpan.FromSeconds(20), token);
                token.ThrowIfCancellationRequested();
                EssayEngine.Record(run, ModEntry.Profile, challenge, q, result);
                ModEntry.Store.Save("learning"); ModEntry.Persist(state, run);
                return challenge.Attempt!;
            }, initiallyWrong: ModEntry.Profile.EssayWrong.ContainsKey(q.Id),
                manualWrong: ModEntry.Profile.EssayManualWrong.Contains(q.Id));
            view.WrongChanged += value => EssayLibraryFlow.MarkWrong(q, value);
            view.DraftChanged += answers =>
            {
                ModEntry.Profile.EssayDrafts[key] = answers;
                ModEntry.Store.Save("learning");
            };
            view.Closed += reason => { closed.TrySetResult(reason); host.Clear(); };
            view.TreeExiting += () => closed.TrySetResult(EssayCloseReason.Back);
            host.Add(view);
            var reason = await closed.Task;
            if (reason == EssayCloseReason.Back) { Restore(); return; }
            if (reason == EssayCloseReason.Abandon)
            { Finish.Invoke(model, [new LocString("events", "SPIRE408_CHALLENGE.leave")]); return; }
            challenge.Cost = cost;
            await Claim(state, run, challenge, model.Owner);
            if (!challenge.OriginalFinished)
            {
                await original.Chosen(); // Vanilla costs/side effects, with its gold still blocked.
                challenge.OriginalFinished = true;
            }
            ModEntry.Persist(state, run);
            BusyEvents.Remove(key);
            await SaveManager.Instance.SaveRun(state.CurrentRoom!, saveProgress: false);
        }
        catch (Exception error)
        {
            GD.PushError("[Spire408] 大题挑战未完成，已保留评分和草稿：" + error);
            Restore();
        }
        finally { BusyEvents.Remove(key); }

        void Restore() { if (!model.IsFinished) SetState.Invoke(model, [description, originals]); }
    }
    public static async Task Claim(RunState state, StudyRun run, EssayChallenge challenge, Player player)
    {
        int amount = EssayEngine.Claim(run, challenge);
        if (amount > 0)
        {
            GoldGate.Grant(player, amount);
            var history = state.CurrentMapPointHistoryEntry?.GetEntry(player.NetId);
            if (history != null) history.GoldGained += amount;
        }
        PrepareBonus(state, challenge, player);
        ModEntry.Persist(state, run);
        await SaveManager.Instance.SaveRun(state.CurrentRoom!, saveProgress: false);
        if (challenge.BonusClaimed || challenge.Bonus == "none") return;
        if (challenge.Bonus == "relic" && challenge.RelicId.Length > 0)
        {
            if (!player.Relics.Any(r => r.Id.Entry == challenge.RelicId))
                await RelicCmd.Obtain(ModelDb.AllRelics.Single(r => r.Id.Entry == challenge.RelicId).ToMutable(), player);
        }
        else if (challenge.Bonus == "card" && challenge.CardIds.Count > 0)
        {
            var cards = challenge.CardIds.Select(id => state.CreateCard(ModelDb.AllCards.Single(c => c.Id.Entry == id), player)).ToList();
            var options = CardCreationOptions.ForNonCombatWithDefaultOdds([player.Character.CardPool]);
            var reward = new CardReward(cards, CardCreationSource.Other, player, options) { CanReroll = false };
            await RewardsCmd.OfferCustom(player, [reward]);
        }
        challenge.BonusClaimed = true;
        ModEntry.Persist(state, run);
        await SaveManager.Instance.SaveRun(state.CurrentRoom!, saveProgress: false);
    }
    public static void PrepareBonus(RunState state, EssayChallenge challenge, Player player)
    {
        string Order(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(challenge.Key + "/bonus/" + id)));
        if (challenge.Bonus == "relic" && challenge.RelicId.Length == 0)
            challenge.RelicId = ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(state.UnlockState)
                .Where(r => BonusRelics.Contains(r.Id.Entry) && r.IsAllowed(state) && !player.Relics.Any(owned => owned.Id == r.Id))
                .OrderBy(r => Order(r.Id.Entry), StringComparer.Ordinal).Select(r => r.Id.Entry).FirstOrDefault() ?? "";
        if (challenge.Bonus == "card" && challenge.CardIds.Count == 0)
            challenge.CardIds = player.Character.CardPool.GetUnlockedCards(state.UnlockState, CardMultiplayerConstraint.SingleplayerOnly)
                .Where(c => c.Rarity == CardRarity.Rare).OrderBy(c => Order(c.Id.Entry), StringComparer.Ordinal).Take(3).Select(c => c.Id.Entry).ToList();
    }
}
