using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spire408.Core;

public sealed class QuestionBank
{
    public string SourceSha256 { get; set; } = "";
    [JsonIgnore] public string ContentHash { get; private set; } = "";
    [JsonIgnore] public string Fingerprint => ContentHash.Length > 0 ? ContentHash : SourceSha256;
    public List<Question> Questions { get; set; } = [];
    public QuestionQualityUpdate QualityUpdate { get; set; } = new();
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    public static QuestionBank Parse(string json)
    {
        var bank = JsonSerializer.Deserialize<QuestionBank>(json, Json) ?? throw new InvalidDataException("题库为空");
        if (bank.Questions.Count == 0 || bank.Questions.Select(q => q.Id).Distinct().Count() != bank.Questions.Count)
            throw new InvalidDataException("题库为空或有重复 ID");
        foreach (var q in bank.Questions)
            if (q.Type != "single_choice" || !q.Options.ContainsKey(q.Answer) || q.Options.Count != 4)
                throw new InvalidDataException($"无效选择题: {q.Id}");
        bank.ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return bank;
    }
}

public sealed class Question
{
    public string Id { get; set; } = "";
    public string Revision { get; set; } = "";
    public string Subject { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string Type { get; set; } = "";
    public string Chapter { get; set; } = "";
    public string ChapterTitle { get; set; } = "";
    public string Stem { get; set; } = "";
    public Dictionary<string, string> Options { get; set; } = [];
    public string Answer { get; set; } = "";
    public string Explanation { get; set; } = "";
    public ExamInfo Exam { get; set; } = new();
    [JsonIgnore] public string ChapterKey => Subject + ":" + Chapter;
}
public sealed class ExamInfo { public string Kind { get; set; } = "unknown"; }

public sealed class StudySettings
{
    public bool Enabled { get; set; } = true;
    public List<string> Subjects { get; set; } = ["ds", "co", "os", "cn"];
    public string ChapterKey { get; set; } = "";
    public bool WrongOnly { get; set; }
    // all / marked / strict. Unknown provenance is never silently promoted to practice.
    public string ExamFilter { get; set; } = "all";
    // Retained for old preferences; questions now require a manual badge activation.
    public bool EveryCard { get; set; }
    public bool Essays { get; set; } = true;
    public StudySettings Copy() => new()
    {
        Enabled = Enabled, Subjects = [.. Subjects], ChapterKey = ChapterKey,
        WrongOnly = WrongOnly, ExamFilter = ExamFilter, EveryCard = EveryCard, Essays = Essays
    };
}

public sealed class LearningProfile
{
    public Dictionary<string, string> Wrong { get; set; } = [];
    public Dictionary<string, Attempt> Attempts { get; set; } = [];
    public int CorrectCount { get; set; }
    public int AnsweredCount { get; set; }
    public Dictionary<string, string> EssayWrong { get; set; } = [];
    public Dictionary<string, EssayAttempt> EssayAttempts { get; set; } = [];
    public Dictionary<string, List<string>> EssayDrafts { get; set; } = [];
    public Dictionary<string, EssayAttempt> EssayPractice { get; set; } = [];
    public HashSet<string> EssayManualWrong { get; set; } = [];
}
public sealed class Attempt
{
    public string QuestionId { get; set; } = "";
    public string Revision { get; set; } = "";
    public string Choice { get; set; } = "";
    public bool Correct { get; set; }
    public bool Invalidated { get; set; }
}
public sealed class StudyRun
{
    public bool Enabled { get; set; }
    public string RunId { get; set; } = "";
    public string BankHash { get; set; } = "";
    public StudySettings Settings { get; set; } = new();
    public List<string> Deck { get; set; } = [];
    public int Cursor { get; set; }
    public BattleStudy Battle { get; set; } = new();
    public HashSet<string> PaidBattles { get; set; } = [];
    public int TotalGold { get; set; }
    public HashSet<string> IntrinsicGoldRewardKeys { get; set; } = [];
    public string EssayBankHash { get; set; } = "";
    public List<string> EssayDeck { get; set; } = [];
    public int EssayCursor { get; set; }
    public Dictionary<string, EssayChallenge> EssayEvents { get; set; } = [];
    public HashSet<int> EssayBonusActs { get; set; } = [];
}
public sealed class BattleStudy
{
    public string Id { get; set; } = "";
    public int Cap { get; set; }
    public int Earned { get; set; }
    public bool Armed { get; set; }
    public int LastTurn { get; set; } = -1;
    public int Sequence { get; set; }
    public string PendingId { get; set; } = "";
    public string PendingKey { get; set; } = "";
    public HashSet<string> GradedKeys { get; set; } = [];
    [JsonIgnore] public int RemainingGains => Math.Max(0, (Cap - Earned + StudyEngine.GoldPerCorrect - 1) / StudyEngine.GoldPerCorrect);
    [JsonIgnore] public int MaximumGains => (Cap + StudyEngine.GoldPerCorrect - 1) / StudyEngine.GoldPerCorrect;
}

public static class StudyEngine
{
    public const int GoldPerCorrect = 10;
    public const int NormalCap = 30;
    public const int EliteCap = 60;
    public const int BossCap = 90;
    public static List<Question> Filter(QuestionBank bank, StudySettings settings, LearningProfile profile) =>
        bank.Questions.Where(q => settings.Subjects.Contains(q.Subject)
            && (settings.ChapterKey.Length == 0 || settings.ChapterKey == q.ChapterKey)
            && (settings.ExamFilter == "all" || (settings.ExamFilter == "strict"
                ? q.Exam.Kind == "practice" : q.Exam.Kind is not ("past_exam" or "suspected_exam")))
            && (!settings.WrongOnly || profile.Wrong.ContainsKey(q.Id))).ToList();

    public static StudyRun NewRun(QuestionBank bank, StudySettings settings, LearningProfile profile, string runId, bool allowEssayOnly = false)
    {
        var pool = Filter(bank, settings, profile);
        if (settings.Enabled && pool.Count == 0 && !allowEssayOnly) throw new InvalidOperationException("当前筛选没有可用题目，请调整复习计划。");
        // Hash ordering is reproducible across runtimes and independent of upstream file order.
        var deck = pool.OrderBy(q => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runId + q.Id))), StringComparer.Ordinal)
            .Select(q => q.Id).ToList();
        return new StudyRun { Enabled = settings.Enabled, RunId = runId, BankHash = bank.Fingerprint, Settings = settings.Copy(), Deck = deck };
    }

    public static void BeginBattle(StudyRun run, string battleId, int cap)
    {
        if (run.Battle.Id == battleId)
        {
            // Upgrade an unpaid 0.4 battle from five-gold answers to the new rules.
            // Retain its correct-answer count, pending question and first-answer ledger.
            var b = run.Battle;
            if (!run.PaidBattles.Contains(battleId) && (b.Cap, cap) is (25, NormalCap) or (40, EliteCap) or (60, BossCap))
            {
                b.Earned = Math.Min(cap, b.Earned * 2);
                b.Cap = cap;
            }
            return;
        }
        run.Battle = new BattleStudy { Id = battleId, Cap = cap };
    }

    public static string? Issue(StudyRun run, int turn)
    {
        var b = run.Battle;
        if (!run.Enabled || b.Id.Length == 0 || run.PaidBattles.Contains(b.Id) || b.Earned >= b.Cap) return null;
        if (b.PendingId.Length > 0) return b.PendingId;
        if (run.Cursor >= run.Deck.Count) return null;
        b.LastTurn = turn;
        b.PendingKey = $"{run.RunId}/{b.Id}/{b.Sequence++}";
        b.PendingId = run.Deck[run.Cursor++];
        return b.PendingId;
    }

    public static bool CanActivate(StudyRun run) => run.Enabled && run.Battle.Id.Length > 0
        && !run.PaidBattles.Contains(run.Battle.Id) && run.Battle.RemainingGains > 0
        && (run.Battle.PendingId.Length > 0 || run.Cursor < run.Deck.Count);

    public static bool ToggleActivation(StudyRun run)
    {
        if (!run.Battle.Armed && !CanActivate(run)) return false;
        run.Battle.Armed = !run.Battle.Armed;
        return true;
    }

    // Reserve one activation when a specific manual card action is created.
    // Correct answers, rather than clicks or reservations, consume the gold quota.
    public static bool TakeActivation(StudyRun run)
    {
        if (!run.Battle.Armed) return false;
        run.Battle.Armed = false;
        return CanActivate(run);
    }

    public static Attempt Grade(StudyRun run, LearningProfile profile, Question q, string choice)
    {
        var b = run.Battle;
        if (b.PendingId != q.Id || b.PendingKey.Length == 0) throw new InvalidOperationException("题目已经提交或不属于当前作答。");
        if (choice.Length > 0 && !q.Options.ContainsKey(choice)) throw new ArgumentException("无效选项");
        if (!profile.Attempts.TryGetValue(b.PendingKey, out var attempt))
        {
            attempt = new Attempt { QuestionId = q.Id, Revision = q.Revision, Choice = choice, Correct = choice == q.Answer };
            profile.Attempts[b.PendingKey] = attempt;
            profile.AnsweredCount++;
            if (attempt.Correct) { profile.Wrong.Remove(q.Id); profile.CorrectCount++; }
            else profile.Wrong[q.Id] = q.Revision;
        }
        else if (attempt.Invalidated || attempt.QuestionId != q.Id || attempt.Revision != q.Revision)
            throw new InvalidOperationException("读档后的题库版本或题序不一致，已停止计奖。");
        if (b.GradedKeys.Add(b.PendingKey) && attempt.Correct)
            b.Earned = Math.Min(b.Cap, b.Earned + GoldPerCorrect);
        b.PendingId = "";
        b.PendingKey = "";
        return attempt;
    }

    public static int Settle(StudyRun run, string battleId)
    {
        if (run.Battle.Id != battleId || !run.PaidBattles.Add(battleId)) return 0;
        run.Battle.Armed = false;
        int amount = run.Battle.Earned;
        run.TotalGold += amount;
        return amount;
    }
}
