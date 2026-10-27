using System.Text.Json;
using Spire408.Core;

int checks = 0;
void Check(bool ok, string description) { checks++; if (!ok) throw new Exception(description); Console.WriteLine("PASS " + description); }
Question Make(string id, string subject = "ds", string kind = "unknown") => new()
{ Id = id, Revision = "r1", Type = "single_choice", Subject = subject, Stem = "test", Answer = "A", Options = new() { ["A"]="1", ["B"]="2", ["C"]="3", ["D"]="4" }, Exam = new() { Kind = kind } };
var bank = new QuestionBank { SourceSha256 = "hash", Questions = Enumerable.Range(0, 50).Select(i => Make(i.ToString())).ToList() };
bank.Questions.Add(Make("exam", "os", "past_exam"));
bank.Questions.Add(Make("practice", "os", "practice"));
var profile = new LearningProfile();
var settings = new StudySettings();
settings.ExamFilter = "marked";
Check(StudyEngine.Filter(bank,settings,profile).Count == 51, "marked exam filter keeps unknown source explicit");
settings.ExamFilter = "strict";
Check(StudyEngine.Filter(bank,settings,profile).Single().Id == "practice", "strict filter excludes unknown sources");
settings.WrongOnly = true;
Check(StudyEngine.Filter(bank,settings,profile).Count == 0, "empty wrong-only never falls back");
profile.Wrong["practice"] = "r1";
Check(StudyEngine.Filter(bank,settings,profile).Count == 1, "wrong-only intersects source filter");
settings.Subjects = ["ds"];
Check(StudyEngine.Filter(bank,settings,profile).Count == 0, "book selection intersects wrong-only");
try { StudyEngine.NewRun(bank,settings,profile,"x"); Check(false,"empty pool must fail"); } catch(InvalidOperationException) { Check(true,"empty pool is rejected before embark"); }
settings = new StudySettings { EveryCard = true };
var run = StudyEngine.NewRun(bank, settings, profile, "run");
Check(run.Deck.SequenceEqual(StudyEngine.NewRun(bank, settings, profile, "run").Deck), "question sequence deterministic");
var parsedBankA = QuestionBank.Parse(JsonSerializer.Serialize(bank, QuestionBank.Json));
bank.Questions[0].Answer = "B";
var parsedBankB = QuestionBank.Parse(JsonSerializer.Serialize(bank, QuestionBank.Json));
Check(parsedBankA.Fingerprint != parsedBankB.Fingerprint, "bank content edits invalidate saved question version");
bank.Questions[0].Answer = "A";
settings.Subjects.Clear(); Check(run.Settings.Subjects.Count == 4, "run settings are frozen copies");
StudyEngine.BeginBattle(run,"battle1",StudyEngine.NormalCap);
Check(run.Battle.MaximumGains == 3 && run.Battle.RemainingGains == 3, "ordinary battle starts with three gains");
var preBattle = JsonSerializer.Serialize(run);
var id = StudyEngine.Issue(run,1)!;
var q = bank.Questions.Single(x => x.Id == id);
var attempt = StudyEngine.Grade(run,profile,q,"B");
Check(!attempt.Correct && run.Battle.Earned==0 && profile.Wrong.ContainsKey(id), "wrong answer earns zero and enters persistent notebook");
Check(run.Battle.RemainingGains == 3, "wrong answer preserves all gain opportunities");
try { StudyEngine.Grade(run,profile,q,"A"); Check(false,"duplicate submission must fail"); }
catch(InvalidOperationException) { Check(run.Battle.Earned==0,"duplicate submission cannot award gold"); }
var reloaded = JsonSerializer.Deserialize<StudyRun>(preBattle)!;
StudyEngine.Issue(reloaded,1);
var cached = StudyEngine.Grade(reloaded,profile,q,"A");
Check(!cached.Correct && reloaded.Battle.Earned==0, "reload cannot change an already revealed answer for rewards");
Check(profile.AnsweredCount==1, "reload does not duplicate learning statistics");
for(int i=0;i<3;i++) {
    id=StudyEngine.Issue(run,1)!;
    StudyEngine.Grade(run,profile,bank.Questions.Single(q=>q.Id==id),"A");
    Check(run.Battle.Earned == (i+1)*10 && run.Battle.RemainingGains == 2-i, "correct answer grants ten gold and uses one gain " + i);
}
int exhaustedCursor = run.Cursor;
Check(run.Battle.Earned==30 && StudyEngine.Issue(run,2)==null && run.Cursor == exhaustedCursor, "per-combat cap stops questions without consuming the deck");
Check(StudyEngine.Settle(run,"other")==0, "wrong battle cannot claim payout");
Check(StudyEngine.Settle(run,"battle1")==30 && StudyEngine.Settle(run,"battle1")==0, "victory payout idempotent");
var saved = JsonSerializer.Deserialize<StudyRun>(JsonSerializer.Serialize(run))!;
Check(StudyEngine.Settle(saved,"battle1")==0, "claimed reward survives run serialization");
Check(saved.Battle.RemainingGains == 0, "remaining gains survive run serialization");
StudyEngine.BeginBattle(run,"battle2",StudyEngine.EliteCap);
Check(run.Battle.Earned==0 && run.Cursor==4 && run.Battle.RemainingGains==6, "elite battle resets six gains but not deck cursor");
run.Settings.EveryCard=false;
id=StudyEngine.Issue(run,1)!; StudyEngine.Grade(run,profile,bank.Questions.Single(q=>q.Id==id),"A");
Check(StudyEngine.Issue(run,1)!=null, "manual activations have no once-per-turn restriction from legacy preferences");
foreach (int cap in new[] { StudyEngine.EliteCap, StudyEngine.BossCap }) {
    var quotaRun = StudyEngine.NewRun(bank, new StudySettings { EveryCard = true }, new LearningProfile(), "quota"+cap);
    var quotaProfile = new LearningProfile();
    StudyEngine.BeginBattle(quotaRun,"quota",cap);
    int quota = cap / StudyEngine.GoldPerCorrect;
    for (int i=0; i<quota; i++) {
        var issued = StudyEngine.Issue(quotaRun,1)!;
        StudyEngine.Grade(quotaRun,quotaProfile,bank.Questions.Single(q=>q.Id==issued),"A");
    }
    Check(quotaRun.Battle.Earned==cap && quotaProfile.CorrectCount==quota && quotaRun.Battle.RemainingGains==0
        && StudyEngine.Issue(quotaRun,2)==null, "elite/boss cap grants exactly six/nine successful gains " + cap);
}
foreach (var (oldCap,newCap,oldEarned) in new[] { (25,30,5), (40,60,35), (60,90,60) }) {
    var legacy = new StudyRun { Battle = new BattleStudy { Id="legacy", Cap=oldCap, Earned=oldEarned, PendingId="pending", PendingKey="stable", GradedKeys=["graded"] } };
    StudyEngine.BeginBattle(legacy,"legacy",newCap);
    int migrated = Math.Min(newCap,oldEarned*2);
    StudyEngine.BeginBattle(legacy,"legacy",newCap);
    Check(legacy.Battle.Cap == newCap && legacy.Battle.Earned == migrated && legacy.Battle.PendingKey == "stable"
        && legacy.Battle.GradedKeys.Contains("graded"), "legacy unpaid battle upgrades once and preserves first answers " + oldCap);
}
var paidLegacy = new StudyRun { Battle = new BattleStudy { Id="paid", Cap=25, Earned=25 }, PaidBattles=["paid"] };
StudyEngine.BeginBattle(paidLegacy,"paid",30);
Check(paidLegacy.Battle.Earned == 25 && StudyEngine.Settle(paidLegacy,"paid") == 0, "legacy settled battle cannot receive a retroactive second payout");
var manual = StudyEngine.NewRun(bank, new StudySettings(), new LearningProfile(), "manual");
var manualProfile = new LearningProfile();
StudyEngine.BeginBattle(manual,"manual-battle",30);
Check(!manual.Battle.Armed && StudyEngine.CanActivate(manual) && !StudyEngine.TakeActivation(manual), "new combat defaults to idle and unarmed plays cannot reserve a question");
Check(StudyEngine.ToggleActivation(manual) && manual.Battle.Armed && manual.Cursor == 0 && manual.Battle.Earned == 0,
    "arming alone does not issue questions or consume gold quota");
Check(StudyEngine.ToggleActivation(manual) && !manual.Battle.Armed && !StudyEngine.TakeActivation(manual), "second badge click cancels without reserving a card");
StudyEngine.ToggleActivation(manual);
Check(StudyEngine.TakeActivation(manual) && !StudyEngine.TakeActivation(manual) && !manual.Battle.Armed,
    "activation reserves exactly one manual action and immediately returns to idle");
id = StudyEngine.Issue(manual,1)!;
StudyEngine.Grade(manual,manualProfile,bank.Questions.Single(q=>q.Id==id),"B");
Check(!manual.Battle.Armed && manual.Battle.RemainingGains == 3, "wrong reserved answer preserves gains without auto-arming another card");
StudyEngine.ToggleActivation(manual); StudyEngine.TakeActivation(manual);
id = StudyEngine.Issue(manual,1)!;
StudyEngine.Grade(manual,manualProfile,bank.Questions.Single(q=>q.Id==id),"A");
Check(manual.Battle.Earned == 10 && manual.Battle.RemainingGains == 2 && !manual.Battle.Armed, "rearming during the same turn grants one gain for the chosen card");
StudyEngine.ToggleActivation(manual); StudyEngine.TakeActivation(manual);
id = StudyEngine.Issue(manual,1)!;
StudyEngine.Grade(manual,manualProfile,bank.Questions.Single(q=>q.Id==id),"");
Check(!manual.Battle.Armed && manual.Battle.RemainingGains == 2, "skipping returns to idle without spending a gain");
StudyEngine.ToggleActivation(manual);
var manualRestored = JsonSerializer.Deserialize<StudyRun>(JsonSerializer.Serialize(manual))!;
Check(manualRestored.Battle.Armed && manualRestored.Battle.Earned == 10, "explicit arming and quota survive serialization");
StudyEngine.BeginBattle(manualRestored,"next-manual-battle",60);
Check(!manualRestored.Battle.Armed && manualRestored.Battle.RemainingGains == 6, "next battle resets activation and restores its own quota");
manual.Battle.Earned = 30;
Check(!StudyEngine.TakeActivation(manual) && !manual.Battle.Armed && !StudyEngine.ToggleActivation(manual), "late cap exhaustion cancels outstanding arming and prevents reactivation");
manual.Battle.Earned = 0; manual.Cursor = manual.Deck.Count;
Check(!StudyEngine.ToggleActivation(manual), "exhausted question deck prevents activation");
manual.Cursor = 0; manual.PaidBattles.Add(manual.Battle.Id);
Check(!StudyEngine.ToggleActivation(manual), "settled battle cannot be reactivated");
manual.PaidBattles.Clear(); manual.Enabled = false;
Check(!StudyEngine.ToggleActivation(manual), "disabled study runs cannot be activated");
var realPath=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../data/questions.json"));
if(File.Exists(realPath)) { var real = QuestionBank.Parse(File.ReadAllText(realPath)); Check(real.Questions.Count==1676,"quality-filtered imported bank parses in C#"); }
var projectRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../.."));
checks+=QuestionQualityChecks.Run(projectRoot);
checks+=EssayChecks.Run(projectRoot);
checks+=AiChecks.Run(projectRoot);
Console.WriteLine($"{checks} checks passed.");
