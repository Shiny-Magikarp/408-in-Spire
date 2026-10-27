using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Spire408.Core;

public sealed class EssayBank
{
    public List<EssayQuestion> Questions { get; set; } = [];
    public List<string> CompatibleFrom { get; set; } = [];
    public Dictionary<string,string> LegacyGradingRevisions { get; set; } = [];
    public Dictionary<string, Dictionary<string,string>> CompatibleRubricsByBank { get; set; } = [];
    [JsonIgnore] public string Hash { get; private set; } = "";
    public static EssayBank Parse(string json)
    {
        var bank = JsonSerializer.Deserialize<EssayBank>(json, QuestionBank.Json) ?? throw new InvalidDataException("大题库为空");
        if (bank.Questions.Count == 0 || bank.Questions.Select(q => q.Id).Distinct().Count() != bank.Questions.Count)
            throw new InvalidDataException("大题 ID 无效");
        foreach (var q in bank.Questions)
        {
            if (q.Parts.Count is < 1 or > 32 || q.Parts.Select(p => p.Id).Distinct().Count() != q.Parts.Count
                || q.Parts.Sum(p => p.Points) != 100 || q.Parts.Any(p => p.Points <= 0 || p.Reference.Length == 0
                    || p.Kind is not ("number" or "exact" or "concept" or "review" or "rubric")
                    || p.Kind == "rubric" && (p.Checks.Count == 0 || p.Checks.Count > 128 || p.Checks.Any(c => c.Length is < 1 or > 80))))
                throw new InvalidDataException("无效大题评分细则 " + q.Id);
        }
        bank.Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return bank;
    }
}
public sealed class EssayQuestion
{
    public string Id { get; set; } = "";
    public string Revision { get; set; } = "";
    public string Subject { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string Chapter { get; set; } = "";
    public string Title { get; set; } = "";
    public string Stem { get; set; } = "";
    public string Topic { get; set; } = "";
    public string ReferenceAnswer { get; set; } = "";
    public string Explanation { get; set; } = "";
    public int OriginalPoints { get; set; } = 100;
    public string GradingScope { get; set; } = "";
    public EssaySource Source { get; set; } = new();
    public ExamInfo Exam { get; set; } = new();
    public List<EssayPart> Parts { get; set; } = [];
    public bool Experimental { get; set; }
    public bool RequiresProcessReview { get; set; }
    [JsonIgnore] public string ChapterKey => Subject + ":" + Chapter;
    [JsonIgnore] public bool ReviewOnly => Parts.All(p => p.Kind == "review");
}
public sealed class EssaySource
{
    public string Repository { get; set; } = "";
    public string Commit { get; set; } = "";
    public string File { get; set; } = "";
    public string UpstreamId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Year { get; set; }
    public int Number { get; set; }
    public bool Adapted { get; set; }
}
public sealed class EssayPart
{
    public string Id { get; set; } = "";
    public string Prompt { get; set; } = "";
    public int Points { get; set; }
    public string Kind { get; set; } = "";
    public string Reference { get; set; } = "";
    public List<string> Accepted { get; set; } = [];
    public List<string> Checks { get; set; } = [];
    public double Expected { get; set; }
    public double Tolerance { get; set; }
    public string Unit { get; set; } = "";
}
public sealed class EssayPointGrade
{
    public string PartId { get; set; } = "";
    public int Earned { get; set; }
    public int Maximum { get; set; }
    public string Status { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Feedback { get; set; } = "";
    public List<EssayCriterionGrade> Criteria { get; set; } = [];
}
public sealed record EssayCriterionGrade(string Reference, string Status);
public sealed class EssayAttempt
{
    public string QuestionId { get; set; } = "";
    public string Revision { get; set; } = "";
    public List<string> Answers { get; set; } = [];
    public List<EssayPointGrade> Grades { get; set; } = [];
    public int Score { get; set; }
    public bool NeedsReview { get; set; }
    public string ModelVersion { get; set; } = "";
}
public sealed class EssayChallenge
{
    public string Key { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public int Act { get; set; }
    public EssayAttempt? Attempt { get; set; }
    public bool Claimed { get; set; }
    public int Gold { get; set; }
    public string Bonus { get; set; } = "none";
    public List<string> CardIds { get; set; } = [];
    public string RelicId { get; set; } = "";
    public string SelectedCardId { get; set; } = "";
    public bool BonusClaimed { get; set; }
    public bool OriginalFinished { get; set; }
    public string Cost { get; set; } = "";
}
public readonly record struct SemanticEvidence(double Contradiction, double Neutral, double Entailment);

public static class EssayEngine
{
    public static bool UpgradeRun(StudyRun run, EssayBank bank)
    {
        if (run.EssayBankHash == bank.Hash || !bank.CompatibleFrom.Contains(run.EssayBankHash)) return false;
        var byId = bank.Questions.ToDictionary(q => q.Id);
        var revisions = bank.CompatibleRubricsByBank.GetValueOrDefault(run.EssayBankHash) ?? bank.LegacyGradingRevisions;
        if (run.EssayCursor < 0 || run.EssayCursor > run.EssayDeck.Count
            || run.EssayDeck.Concat(run.EssayEvents.Values.Select(e => e.QuestionId)).Any(id =>
                !byId.TryGetValue(id, out var q) || !revisions.TryGetValue(id, out string? revision) || q.Revision != revision))
            throw new InvalidDataException("旧局的大题评分细则不在已验证的兼容范围内。");
        run.EssayBankHash = bank.Hash;
        return true;
    }
    public static List<EssayQuestion> Filter(EssayBank bank, StudySettings settings, LearningProfile profile) =>
        bank.Questions.Where(q => settings.Subjects.Contains(q.Subject)
            && (settings.ChapterKey.Length == 0 || settings.ChapterKey == q.ChapterKey)
            && (settings.ExamFilter == "all" || (settings.ExamFilter == "strict"
                ? q.Exam.Kind == "practice" : q.Exam.Kind is not ("past_exam" or "suspected_exam")))
            && (!settings.WrongOnly || profile.EssayWrong.ContainsKey(q.Id))).ToList();

    public static void Initialize(StudyRun run, EssayBank bank, LearningProfile profile)
    {
        run.EssayBankHash = bank.Hash;
        run.EssayDeck = Filter(bank, run.Settings, profile).Where(q => !q.ReviewOnly).OrderBy(q => Hash(run.RunId + "/essay/" + q.Id), StringComparer.Ordinal)
            .Select(q => q.Id).ToList();
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static EssayChallenge? Issue(StudyRun run, string eventKey, int act)
    {
        if (!run.Enabled || !run.Settings.Essays) return null;
        if (run.EssayEvents.TryGetValue(eventKey, out var prior)) return prior;
        if (run.EssayCursor >= run.EssayDeck.Count) return null;
        var challenge = new EssayChallenge { Key = eventKey, Act = act, QuestionId = run.EssayDeck[run.EssayCursor++] };
        run.EssayEvents[eventKey] = challenge;
        return challenge;
    }
    public static EssayAttempt Grade(EssayQuestion question, IReadOnlyList<string> answers,
        Func<string, string, SemanticEvidence> judge, string modelVersion, CancellationToken token = default)
    {
        int limit = question.Parts.Any(p => p.Kind == "rubric") ? 8000 : question.ReviewOnly ? 20000 : 1000;
        if (answers.Count != question.Parts.Count || answers.Any(a => a.Length > limit))
            throw new ArgumentException($"小问数量不匹配或答案超过 {limit} 字");
        var result = new EssayAttempt { QuestionId = question.Id, Revision = question.Revision, Answers = answers.ToList(), ModelVersion = modelVersion,
            NeedsReview = question.RequiresProcessReview };
        for (int i = 0; i < question.Parts.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var part = question.Parts[i]; string answer = answers[i].Trim();
            var grade = new EssayPointGrade { PartId = part.Id, Maximum = part.Points, Evidence = answer };
            if (part.Kind == "review")
            {
                grade.Status = "review"; grade.Feedback = "请对照参考答案自查，本项未自动评分";
                result.NeedsReview = true;
            }
            else if (answer.Length == 0) { grade.Status = "missing"; grade.Feedback = "未作答"; }
            else if (part.Kind == "rubric")
            {
                int awarded = 0, uncertain = 0;
                foreach (string reference in part.Checks)
                {
                    token.ThrowIfCancellationRequested();
                    var evidence = judge(answer, reference);
                    if (!double.IsFinite(evidence.Entailment) || !double.IsFinite(evidence.Contradiction) || !double.IsFinite(evidence.Neutral))
                        throw new InvalidDataException("模型返回非有限分数");
                    string status = evidence.Entailment >= .85 && evidence.Contradiction < .1 ? "awarded"
                        : evidence.Contradiction >= .6 ? "contradiction" : evidence.Neutral >= .65 ? "missing" : "review";
                    if (status == "awarded") awarded++;
                    if (status == "review") uncertain++;
                    grade.Criteria.Add(new(reference, status));
                }
                grade.Earned = awarded * part.Points / part.Checks.Count;
                grade.Status = uncertain > 0 ? "review" : awarded == part.Checks.Count ? "awarded" : "partial";
                grade.Feedback = $"实验性核分：命中 {awarded}/{part.Checks.Count} 个参考评分点" + (uncertain > 0 ? $"，{uncertain} 项待复核" : "")
                    + (question.RequiresProcessReview ? "；未验证代码执行、证明或绘图过程" : "");
                result.NeedsReview |= uncertain > 0;
            }
            else if (part.Kind != "concept")
            {
                bool ok = part.Kind == "number" ? NumberMatches(part, answer) : part.Accepted.Any(x => Normalize(x) == Normalize(answer));
                grade.Status = ok ? "awarded" : "incorrect";
                grade.Earned = ok ? part.Points : 0;
                grade.Feedback = ok ? "结果与评分细则一致" : "最终结果或单位不符";
            }
            else
            {
                var evidence = judge(answer, part.Reference);
                if (!double.IsFinite(evidence.Entailment) || !double.IsFinite(evidence.Contradiction)
                    || !double.IsFinite(evidence.Neutral)) throw new InvalidDataException("模型返回非有限分数");
                bool accepted = evidence.Entailment >= 0.85 && evidence.Contradiction < 0.1;
                grade.Earned = accepted ? part.Points : 0;
                grade.Status = accepted ? "awarded" : evidence.Contradiction >= 0.6 ? "contradiction" : evidence.Neutral >= 0.65 ? "missing" : "review";
                grade.Feedback = grade.Status switch { "awarded" => "瓦库识别到评分点", "contradiction" => "答案与评分点矛盾", "missing" => "尚未识别到完整评分点", _ => "瓦库无法确认，保留供复核，本项暂不计分" };
                result.NeedsReview |= grade.Status == "review";
            }
            result.Grades.Add(grade);
        }
        result.Score = result.Grades.Sum(g => g.Earned);
        return result;
    }
    public static void Record(StudyRun run, LearningProfile profile, EssayChallenge challenge, EssayQuestion q, EssayAttempt result)
    {
        if (!run.EssayEvents.TryGetValue(challenge.Key, out var pending) || pending.QuestionId != q.Id
            || result.QuestionId != q.Id || result.Revision != q.Revision) throw new InvalidOperationException("大题评分与当前挑战不符");
        if (profile.EssayAttempts.TryGetValue(challenge.Key, out var prior)) result = prior;
        else profile.EssayAttempts[challenge.Key] = result;
        if (result.QuestionId != q.Id || result.Revision != q.Revision) throw new InvalidOperationException("已提交大题的版本不一致");
        challenge.Attempt = result;
        UpdateNotebook(profile, q, result);
        profile.EssayDrafts.Remove(challenge.Key);
    }
    public static void UpdateNotebook(LearningProfile profile, EssayQuestion q, EssayAttempt result)
    {
        if (!result.NeedsReview)
        {
            if (result.Score == 100)
            {
                if (!profile.EssayManualWrong.Contains(q.Id)) profile.EssayWrong.Remove(q.Id);
            }
            else profile.EssayWrong[q.Id] = q.Revision;
        }
    }
    public static (int Gold, string Bonus) RewardFor(int score) => score switch
    { < 40 => (0, "none"), < 60 => (15, "none"), < 75 => (25, "none"), < 90 => (35, "card"), _ => (40, "relic") };

    public static int Claim(StudyRun run, EssayChallenge challenge)
    {
        if (challenge.Claimed) return 0;
        if (challenge.Attempt == null) throw new InvalidOperationException("请先提交大题");
        var reward = RewardFor(challenge.Attempt.Score);
        challenge.Gold = reward.Gold;
        challenge.Bonus = reward.Bonus;
        if (challenge.Attempt.NeedsReview) challenge.Bonus = "none";
        if (challenge.Bonus != "none" && !run.EssayBonusActs.Add(challenge.Act)) challenge.Bonus = "none";
        challenge.Claimed = true;
        run.TotalGold += challenge.Gold;
        return challenge.Gold;
    }
    private static string Normalize(string text) => Regex.Replace(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"\s+", "").TrimEnd('。', '.');
    public static bool NumberMatches(EssayPart part, string input)
    {
        string text = input.Normalize(NormalizationForm.FormKC).Trim().TrimEnd('。');
        var match = Regex.Match(text, @"^(?:答案\s*[:：=]\s*)?(?<n>[+-]?(?:\d+(?:\.\d*)?|\.\d+))(?:\s*/\s*(?<d>[+-]?(?:\d+(?:\.\d*)?|\.\d+)))?\s*(?<u>[A-Za-z%位个条字节页帧]*)$", RegexOptions.CultureInvariant);
        if (!match.Success || !double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value)) return false;
        if (match.Groups["d"].Success)
        {
            if (!double.TryParse(match.Groups["d"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double denominator)
                || denominator == 0 || !double.IsFinite(denominator)) return false;
            value /= denominator;
        }
        string unit = match.Groups["u"].Value;
        if (unit.Length > 0)
        {
            if (part.Unit.Length == 0 && unit is not ("位" or "个" or "条" or "页" or "帧")) return false;
            if (part.Unit == "B")
            {
                value *= unit switch { "B" or "字节" => 1, "KiB" or "KB" => 1024, "MiB" or "MB" => 1048576, _ => double.NaN };
            }
            else if (part.Unit is "ns" or "ms")
            {
                double seconds = unit switch { "ns" => 1e-9, "us" => 1e-6, "ms" => 1e-3, "s" => 1, _ => double.NaN };
                value = value * seconds / (part.Unit == "ns" ? 1e-9 : 1e-3);
            }
            else if (part.Unit.Length > 0 && unit != part.Unit) return false;
        }
        return double.IsFinite(value) && Math.Abs(value - part.Expected) <= Math.Max(part.Tolerance, 1e-8);
    }
}
