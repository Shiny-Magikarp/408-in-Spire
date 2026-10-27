using System.Text.Json;
using Spire408.Core;

internal static class QuestionQualityChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
        var bank = QuestionBank.Parse(File.ReadAllText(Path.Combine(root, "game/Spire408/Data/questions.json")));
        var update = bank.QualityUpdate;
        var revision = update.Revisions.Single(r=>r.Id=="408-53059b12626384217e962029");
        var q = bank.Questions.Single(q => q.Id == revision.Id);
        Check(q.Answer == "D" && q.Explanation.Contains("24576") && !q.Explanation.Contains("缺页"),
            "reported encoding question uses corrected D and a matching derivation");
        Check(bank.Questions.Count == 1676 && !bank.Questions.Any(q => update.WithdrawnIds.Contains(q.Id)),
            "shipped bank excludes the unreviewed OCR batch");
        string withdrawn = update.WithdrawnIds.First(), keep = bank.Questions.First(q => q.Id != revision.Id).Id;
        var run = new StudyRun { Enabled = true, RunId = "legacy", BankHash = update.CompatibleFrom.First(),
            Deck = [withdrawn, keep, q.Id, withdrawn, keep], Cursor = 3, TotalGold = 30, PaidBattles = ["paid"],
            Battle = new BattleStudy { Id = "battle", Cap = 30, Earned = 20, Armed = true,
                Sequence = 7, PendingId = withdrawn, PendingKey = "legacy/battle/6", GradedKeys = ["old"] } };
        Check(QuestionQuality.UpgradeRun(bank, run) && run.Deck.SequenceEqual(new[] {keep, q.Id, keep}) && run.Cursor == 2,
            "known old bank migrates its deck without repeating answered questions");
        Check(run.Battle.PendingId == "" && run.Battle.PendingKey == "" && run.Battle.Sequence == 7 && !run.Battle.Armed,
            "withdrawn pending questions are cancelled without recycling first-answer keys");
        Check(run.Battle.Earned == 20 && run.TotalGold == 30 && run.PaidBattles.Contains("paid") && run.Battle.GradedKeys.Contains("old"),
            "quality upgrade preserves gold and reward idempotency");
        Check(!QuestionQuality.UpgradeRun(bank, run), "known bank migration is idempotent");
        var pending = new StudyRun { BankHash=update.CompatibleFrom.First(), Deck=[q.Id], Cursor=1,
            Battle=new BattleStudy { PendingId=q.Id, PendingKey="pending-first-choice", Sequence=4 } };
        QuestionQuality.UpgradeRun(bank, pending);
        Check(pending.Cursor == 1 && pending.Battle.PendingId == q.Id && pending.Battle.PendingKey == "pending-first-choice"
            && pending.Battle.Sequence == 4, "a corrected pending question keeps its first-answer identity during migration");
        var unknown = new StudyRun { BankHash = "unreviewed-bank", Deck = [q.Id] };
        try { QuestionQuality.UpgradeRun(bank, unknown); Check(false, "unknown bank must fail"); }
        catch (InvalidOperationException) { Check(unknown.BankHash == "unreviewed-bank", "unrecognized bank revisions remain blocked"); }
        var corrupt = new StudyRun { BankHash = update.CompatibleFrom.First(), Deck = ["not-in-manifest"] };
        try { QuestionQuality.UpgradeRun(bank, corrupt); Check(false, "unknown deck item must fail"); }
        catch (InvalidDataException) { Check(corrupt.Deck.Single() == "not-in-manifest", "unknown deck entries fail before modifying a run"); }

        var profile = new LearningProfile { AnsweredCount = 2, CorrectCount = 1,
            Wrong = new() { [q.Id] = revision.PreviousRevision, [withdrawn] = "old" },
            Attempts = new() {
                ["legacy/battle/1"] = new Attempt {QuestionId=q.Id, Revision=revision.PreviousRevision, Choice="D", Correct=false},
                ["legacy/battle/0"] = new Attempt {QuestionId=withdrawn, Revision="old", Choice="A", Correct=true} } };
        Check(QuestionQuality.ReconcileProfile(bank, profile) && profile.AnsweredCount == 1 && profile.CorrectCount == 1
            && profile.Wrong.Count == 0, "misgraded D is corrected and withdrawn questions leave the notebook and statistics");
        Check(profile.Attempts["legacy/battle/0"].Invalidated && profile.Attempts["legacy/battle/1"].Choice == "D"
            && profile.Attempts["legacy/battle/1"].Revision == q.Revision, "original choices remain auditable after correction");
        Check(!QuestionQuality.ReconcileProfile(bank, profile) && profile.AnsweredCount == 1 && profile.CorrectCount == 1,
            "reopening the profile cannot repeat statistics adjustments");
        run.Battle.PendingId = q.Id; run.Battle.PendingKey = "legacy/battle/1";
        var first = StudyEngine.Grade(run, profile, q, "B");
        Check(first.Correct && first.Choice == "D" && run.Battle.Earned == 30 && profile.AnsweredCount == 1,
            "corrected cached attempt retains the original choice and obeys the combat cap");
        var wrongB = new LearningProfile { AnsweredCount = 1, CorrectCount = 1, Attempts = new() {
            ["b"] = new Attempt { QuestionId=q.Id, Revision=revision.PreviousRevision, Choice="B", Correct=true } } };
        QuestionQuality.ReconcileProfile(bank, wrongB);
        Check(wrongB.CorrectCount == 0 && wrongB.Wrong[q.Id] == q.Revision, "old false-positive B becomes a wrong answer");
        var restored = JsonSerializer.Deserialize<LearningProfile>(JsonSerializer.Serialize(profile))!;
        Check(restored.Attempts["legacy/battle/0"].Invalidated && !QuestionQuality.ReconcileProfile(bank, restored),
            "quality reconciliation survives serialization");
        return checks;
    }
}
