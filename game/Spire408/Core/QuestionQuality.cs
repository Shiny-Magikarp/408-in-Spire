namespace Spire408.Core;

public sealed class QuestionQualityUpdate
{
    public List<string> CompatibleFrom { get; set; } = [];
    public HashSet<string> WithdrawnIds { get; set; } = [];
    public List<QuestionRevisionUpdate> Revisions { get; set; } = [];
}

public sealed class QuestionRevisionUpdate
{
    public string Id { get; set; } = "";
    public string PreviousRevision { get; set; } = "";
    public string Revision { get; set; } = "";
}

public static class QuestionQuality
{
    // Preserve historical choices as evidence, but stop counting withdrawn material as practice.
    // This never changes gold already earned, paid battles or the first-choice lock.
    public static bool ReconcileProfile(QuestionBank bank, LearningProfile profile)
    {
        bool changed = false;
        var update = bank.QualityUpdate;
        foreach (string id in update.WithdrawnIds)
            changed |= profile.Wrong.Remove(id);
        foreach (var attempt in profile.Attempts.Values)
        {
            if (update.WithdrawnIds.Contains(attempt.QuestionId) && !attempt.Invalidated)
            {
                attempt.Invalidated = true;
                profile.AnsweredCount = Math.Max(0, profile.AnsweredCount - 1);
                if (attempt.Correct) profile.CorrectCount = Math.Max(0, profile.CorrectCount - 1);
                changed = true;
            }
        }
        foreach (var revision in update.Revisions)
        {
            var question = bank.Questions.Single(q => q.Id == revision.Id && q.Revision == revision.Revision);
            bool revised = false;
            foreach (var attempt in profile.Attempts.Values.Where(a => a.QuestionId == revision.Id
                         && a.Revision == revision.PreviousRevision && !a.Invalidated))
            {
                bool correct = attempt.Choice == question.Answer;
                profile.CorrectCount = Math.Max(0, profile.CorrectCount + (correct ? 1 : 0) - (attempt.Correct ? 1 : 0));
                attempt.Correct = correct;
                attempt.Revision = revision.Revision;
                revised = true;
                changed = true;
            }
            if (revised || (profile.Wrong.TryGetValue(revision.Id, out string? wrongRevision) && wrongRevision == revision.PreviousRevision))
            {
                var latest = profile.Attempts.Values.LastOrDefault(a => a.QuestionId == revision.Id && !a.Invalidated);
                if (latest is { Correct: false }) profile.Wrong[revision.Id] = revision.Revision;
                else profile.Wrong.Remove(revision.Id);
                changed = true;
            }
        }
        return changed;
    }

    public static bool UpgradeRun(QuestionBank bank, StudyRun run)
    {
        if (run.BankHash == bank.Fingerprint) return false;
        if (!bank.QualityUpdate.CompatibleFrom.Contains(run.BankHash))
            throw new InvalidOperationException("题库与本局存档版本不一致，且没有已核验的迁移记录。");
        var valid = bank.Questions.Select(q => q.Id).ToHashSet();
        var withdrawn = bank.QualityUpdate.WithdrawnIds;
        if (run.Cursor < 0 || run.Cursor > run.Deck.Count || run.Deck.Any(id => !valid.Contains(id) && !withdrawn.Contains(id))
            || (run.Battle.PendingId.Length > 0 && !valid.Contains(run.Battle.PendingId) && !withdrawn.Contains(run.Battle.PendingId)))
            throw new InvalidDataException("旧局包含迁移清单之外的题目，已停止迁移。");
        int cursor = run.Deck.Take(run.Cursor).Count(valid.Contains);
        run.Deck = run.Deck.Where(valid.Contains).ToList();
        run.Cursor = cursor;
        if (withdrawn.Contains(run.Battle.PendingId))
        {
            run.Battle.PendingId = "";
            run.Battle.PendingKey = "";
        }
        run.Battle.Armed = false;
        run.BankHash = bank.Fingerprint;
        return true;
    }
}
