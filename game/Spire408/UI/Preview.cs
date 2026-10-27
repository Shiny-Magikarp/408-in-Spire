using Godot;
using Spire408.Core;
using Spire408.AI;

namespace Spire408.UI;

// Standalone editor preview: no game hooks, no game saves and no mod initializer.
public partial class Preview : Control
{
    private QuestionBank _bank = null!;
    private LearningProfile _profile = new();
    public override async void _Ready()
    {
        try
        {
            NativeAssets.MountForPreview();
            _bank = QuestionBank.Parse(Godot.FileAccess.GetFileAsString("res://Spire408/Data/questions.json"));
            string[] args = OS.GetCmdlineUserArgs();
            if (args.Contains("--essay")) ShowEssay(); else if (args.Contains("--quiz")) ShowQuiz(); else ShowSetup();
            string? screenshot = args.FirstOrDefault(x => x.StartsWith("--screenshot="))?.Split('=', 2)[1];
            if (screenshot != null)
            {
                await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
                GetViewport().GetTexture().GetImage().SavePng(screenshot);
                GD.Print("[Spire408 preview] screenshot saved " + screenshot);
                GetTree().Quit();
            }
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
    }
    private void ShowSetup()
    {
        var view = GD.Load<PackedScene>("res://Spire408/Scenes/StudySetup.tscn").Instantiate<StudySetup>();
        view.Configure(_bank, _profile, new StudySettings(), EssayBank.Parse(Godot.FileAccess.GetFileAsString("res://Spire408/Data/essays.json")));
        view.Cancelled += () => GetTree().Quit();
        view.Confirmed += _ => { view.QueueFree(); ShowQuiz(); };
        AddChild(view);
    }
    private void ShowQuiz()
    {
        var q = _bank.Questions.First(q => q.Stem.Contains('$') && q.Subject == "ds");
        var run = StudyEngine.NewRun(_bank, new StudySettings(), _profile, "preview");
        run.Deck = [q.Id]; StudyEngine.BeginBattle(run, "preview", StudyEngine.NormalCap); StudyEngine.Issue(run, 1);
        var view = GD.Load<PackedScene>("res://Spire408/Scenes/QuizPanel.tscn").Instantiate<QuizPanel>();
        view.Configure(q, 0, StudyEngine.NormalCap, null, choice => (StudyEngine.Grade(run, _profile, q, choice), run.Battle.Earned));
        view.Closed += () => { view.QueueFree(); ShowSetup(); };
        AddChild(view);
    }
    private void ShowEssay()
    {
        WakuuRuntime.Initialize(ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug"));
        WakuuJudge.ModelDirectory = ProjectSettings.GlobalizePath("res://../models/wakuu");
        var bank = EssayBank.Parse(Godot.FileAccess.GetFileAsString("res://Spire408/Data/essays.json"));
        var question = bank.Questions.Single(q => q.Id == "practice-co-cache");
        var view = GD.Load<PackedScene>("res://Spire408/Scenes/EssayPanel.tscn").Instantiate<EssayPanel>();
        view.Configure(question, [], null, "无", true, (answers, token) => Task.Run(
            () => EssayEngine.Grade(question, answers, WakuuJudge.Judge, WakuuJudge.Version, token), token));
        view.Closed += _ => GetTree().Quit();
        AddChild(view);
    }
}
