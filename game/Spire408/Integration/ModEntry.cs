using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Spire408.Core;
using Spire408.UI;
using Spire408.Models;
using Spire408.AI;
using MegaCrit.Sts2.Core.Models.RelicPools;
using STS2RitsuLib.Content;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Interop;
using STS2RitsuLib.RunData;
using STS2RitsuLib.Utils.Persistence;

namespace Spire408.Integration;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Id = "Spire408";
    public static QuestionBank Bank { get; private set; } = null!;
    public static EssayBank Essays { get; private set; } = null!;
    public static Dictionary<string, Question> Questions { get; private set; } = [];
    public static RunSavedData<StudyRun> Runs { get; private set; } = null!;
    public static ModDataStore Store { get; private set; } = null!;
    public static StudyRun? PendingRun { get; set; }
    public static bool Ready { get; private set; }
    public static LearningProfile Profile
    {
        get
        {
            var profile = Store.Get<LearningProfile>("learning");
            if (QuestionQuality.ReconcileProfile(Bank, profile)) Store.Save("learning");
            return profile;
        }
    }
    public static StudySettings Preferences => Store.Get<StudySettings>("preferences");
    public static string ErrorMessage { get; private set; } = "";

    public static void Initialize()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            WakuuRuntime.Initialize(System.IO.Path.GetDirectoryName(assembly.Location)!);
            var logger = RitsuLibFramework.CreateLogger(Id);
            ModTypeDiscoveryHub.RegisterModAssembly(Id, assembly);
            RitsuLibFramework.EnsureGodotScriptsRegistered(assembly, logger);
            RitsuLibFramework.GetContentRegistry(Id).RegisterRelic<EventRelicPool, StudyBook>(
                ModelPublicEntryOptions.FromFullPublicEntry(StudyBook.PublicEntry));
            Bank = QuestionBank.Parse(Godot.FileAccess.GetFileAsString("res://Spire408/Data/questions.json"));
            Essays = EssayBank.Parse(Godot.FileAccess.GetFileAsString("res://Spire408/Data/essays.json"));
            Questions = Bank.Questions.ToDictionary(q => q.Id);
            using (RitsuLibFramework.BeginModDataRegistration(Id))
            {
                Store = RitsuLibFramework.GetDataStore(Id);
                Store.Register("preferences", "preferences.json", SaveScope.Profile, () => new StudySettings(), autoCreateIfMissing: true);
                Store.Register("learning", "learning.json", SaveScope.Profile, () => new LearningProfile(), autoCreateIfMissing: true);
                Runs = RitsuLibFramework.GetRunSavedDataStore(Id).Register("study_run", () => new StudyRun());
            }
            new Harmony("Spire408.v1").PatchAll(assembly);
            Ready = true;
            GD.Print($"[Spire408] Loaded {Bank.Questions.Count} questions; game API 0.111.0.");
#if SMOKE
            Spire408.Tests.GameSmoke.Install();
#endif
        }
        catch (Exception ex)
        {
            ErrorMessage = "408 in Spire 初始化失败：" + ex.Message;
            GD.PushError("[Spire408] " + ex);
            new Harmony("Spire408.v1").UnpatchAll("Spire408.v1");
        }
    }

    public static StudyRun? Active(IRunState? state)
    {
        if (!Ready || state is not RunState runState || runState.Players.Count != 1) return null;
        if (!Runs.TryGet(runState, out var run) || !run.Enabled) return null;
        if (Bank.QualityUpdate.CompatibleFrom.Contains(run.BankHash) && QuestionQuality.UpgradeRun(Bank, run))
            Persist(runState, run);
        if (EssayEngine.UpgradeRun(run, Essays)) Persist(runState, run);
        return run;
    }

    public static void Persist(RunState state, StudyRun run) => Runs.Set(state, run);
    public static string BattleId(RunState state) => $"{state.CurrentActIndex}:{state.TotalFloor}:{state.CurrentRoom?.Id}";
    public static void EnsureBattle(RunState state, StudyRun run)
    {
        if (state.CurrentRoom is not CombatRoom room) return;
        int cap = room.RoomType switch { RoomType.Elite => StudyEngine.EliteCap, RoomType.Boss => StudyEngine.BossCap, _ => StudyEngine.NormalCap };
        StudyEngine.BeginBattle(run, BattleId(state), cap);
    }

    public static void ShowSetup(Action accepted, Action? cancelled = null, StudySettings? draft = null)
    {
        if (NModalContainer.Instance is not { OpenModal: null } host) return;
        var screen = GD.Load<PackedScene>("res://Spire408/Scenes/StudySetup.tscn").Instantiate<StudySetup>();
        screen.Configure(Bank, Profile, draft ?? Preferences.Copy(), Essays);
        screen.BrowseRequested += settings =>
        {
            host.Clear();
            EssayLibraryFlow.Open(settings, () => ShowSetup(accepted, cancelled, settings));
        };
        screen.Confirmed += settings =>
        {
            try
            {
                bool essayOnly = settings.Essays && EssayEngine.Filter(Essays, settings, Profile).Any(q => !q.ReviewOnly);
                PendingRun = StudyEngine.NewRun(Bank, settings, Profile, Guid.NewGuid().ToString("N"), essayOnly);
                EssayEngine.Initialize(PendingRun, Essays, Profile);
                Store.Modify<StudySettings>("preferences", p =>
                {
                    p.Enabled = settings.Enabled; p.Subjects = [.. settings.Subjects]; p.ChapterKey = settings.ChapterKey;
                    p.WrongOnly = settings.WrongOnly; p.ExamFilter = settings.ExamFilter; p.EveryCard = settings.EveryCard; p.Essays = settings.Essays;
                });
                Store.Save("preferences");
                host.Clear();
                accepted();
            }
            catch (Exception ex) { screen.ShowError(ex.Message); GD.PushError(ex.ToString()); }
        };
        screen.Cancelled += () => { PendingRun = null; host.Clear(); cancelled?.Invoke(); };
        host.Add(screen);
    }

    public static async Task<bool> Quiz(Player player, int turn)
    {
        if (player.RunState is not RunState state || Active(state) is not { } run) return false;
        EnsureBattle(state, run);
        if (run.BankHash != Bank.Fingerprint)
            throw new InvalidOperationException("题库与本局存档版本不一致。请恢复创建本局时的题库。");
        string? id = StudyEngine.Issue(run, turn);
        if (id == null) return false;
        Persist(state, run);
        if (!Questions.TryGetValue(id, out var q)) throw new InvalidDataException("缺失题目 " + id);
        // Wait for an existing game modal instead of overwriting a tutorial or confirmation.
        var tree = (SceneTree)Engine.GetMainLoop();
        while (NModalContainer.Instance?.OpenModal != null)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        var host = NModalContainer.Instance ?? throw new InvalidOperationException("找不到游戏弹窗容器");
        var view = GD.Load<PackedScene>("res://Spire408/Scenes/QuizPanel.tscn").Instantiate<QuizPanel>();
        var completion = new TaskCompletionSource();
        bool earnedGain = false;
        bool wasPaused = CombatManager.Instance.IsPaused;
        if (!wasPaused) CombatManager.Instance.Pause();
        try
        {
            Profile.Attempts.TryGetValue(run.Battle.PendingKey, out var cached);
            view.Configure(q, run.Battle.Earned, run.Battle.Cap, cached, choice =>
            {
                // Keep the pending question retryable if writing the profile or run slot fails.
                var updated = JsonSerializer.Deserialize<StudyRun>(JsonSerializer.Serialize(run))!;
                var result = StudyEngine.Grade(updated, Profile, q, choice);
                Store.Save("learning");
                Persist(state, updated);
                earnedGain = result.Correct && updated.Battle.Earned > run.Battle.Earned;
                run = updated;
                return (result, run.Battle.Earned);
            });
            view.Closed += () => { host.Clear(); completion.TrySetResult(); };
            view.TreeExiting += () => completion.TrySetResult();
            host.Add(view);
            await completion.Task;
        }
        finally
        {
            if (!wasPaused) CombatManager.Instance.Unpause();
        }
        return earnedGain;
    }

    public static void Settle(RunState state)
    {
        if (Active(state) is not { } run) return;
        string id = BattleId(state);
        int amount = StudyEngine.Settle(run, id);
        if (amount > 0)
        {
            var player = state.Players[0];
            // A single synchronous setter permit; no async/global bypass window or gold multipliers.
            GoldGate.Grant(player, amount);
            var history = state.CurrentMapPointHistoryEntry?.GetEntry(player.NetId);
            if (history != null) history.GoldGained += amount;
            GD.Print($"[Spire408] Study gold +{amount}; battle {id}; cap {run.Battle.Cap}");
        }
        Persist(state, run); // Hook.AfterCombatVictory is awaited before the vanilla run snapshot.
    }
}

internal static class GoldGate
{
    private static Player? _recipient;
    private static int _expectedBalance;
    public static bool Consume(Player player, int balance)
    {
        if (!ReferenceEquals(_recipient, player) || balance != _expectedBalance) return false;
        _recipient = null;
        return true;
    }
    public static void Grant(Player player, int amount)
    {
        _recipient = player;
        _expectedBalance = checked(player.Gold + amount);
        try { player.Gold = _expectedBalance; }
        finally { _recipient = null; }
    }
}
