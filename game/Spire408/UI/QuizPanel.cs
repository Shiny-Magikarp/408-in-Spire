using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using Spire408.Core;

namespace Spire408.UI;

public partial class QuizPanel : Control, IScreenContext
{
    [Export] public Label Heading { get; set; } = null!;
    [Export] public Label Progress { get; set; } = null!;
    [Export] public RichTextLabel Stem { get; set; } = null!;
    [Export] public RichTextLabel OptionA { get; set; } = null!;
    [Export] public RichTextLabel OptionB { get; set; } = null!;
    [Export] public RichTextLabel OptionC { get; set; } = null!;
    [Export] public RichTextLabel OptionD { get; set; } = null!;
    [Export] public SpireTickbox PickA { get; set; } = null!;
    [Export] public SpireTickbox PickB { get; set; } = null!;
    [Export] public SpireTickbox PickC { get; set; } = null!;
    [Export] public SpireTickbox PickD { get; set; } = null!;
    [Export] public Label Feedback { get; set; } = null!;
    [Export] public RichTextLabel Explanation { get; set; } = null!;
    [Export] public ScrollContainer Scroll { get; set; } = null!;
    [Export] public SpireButton Submit { get; set; } = null!;
    [Export] public SpireButton Skip { get; set; } = null!;
    public Control? DefaultFocusedControl => PickA;
    public event Action? Closed;
    private Question _q = null!;
    private int _earned, _cap;
    private Attempt? _cached;
    private bool _graded;
    private string _choice = "";
    private Func<string, (Attempt, int)> _grade = null!;
    public void Configure(Question question, int earned, int cap, Attempt? cached, Func<string, (Attempt, int)> grade)
    { _q = question; _earned = earned; _cap = cap; _cached = cached; _grade = grade; }
    public override void _Ready()
    {
        Heading.Text = $"{_q.SubjectName}  /  {(_q.ChapterTitle.Length > 0 ? _q.ChapterTitle : "第 " + _q.Chapter + " 章")}";
        UpdateProgress();
        var labels = new[] { OptionA, OptionB, OptionC, OptionD };
        var picks = Picks();
        for (int i = 0; i < 4; i++)
        {
            string letter = "ABCD"[i].ToString();
            picks[i].Pressed += () => Select(letter);
        }
        Submit.Pressed += () => { if (_graded) Closed?.Invoke(); else Grade(_choice); };
        Skip.Pressed += () => Grade("");
        Submit.Disabled = true;
        PickA.GrabFocus();
        // Layout first, then size diagrams to the actual content width.
        Callable.From(() =>
        {
            if (!IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion()) return;
            AssetText.Fill(Stem, AssetText.Get(_q.Id, "stem", _q.Stem));
            for (int i = 0; i < 4; i++) AssetText.Fill(labels[i], AssetText.Get(_q.Id, "ABCD"[i].ToString(), _q.Options["ABCD"[i].ToString()]));
            if (_cached != null) Grade(_cached.Choice);
        }).CallDeferred();
    }
    private SpireTickbox[] Picks() => [PickA, PickB, PickC, PickD];
    private void Select(string letter)
    {
        if (_graded) return;
        _choice = letter;
        for (int i = 0; i < 4; i++) Picks()[i].SetPressedNoSignal("ABCD"[i].ToString() == letter);
        Submit.Disabled = false;
    }
    private void Grade(string choice)
    {
        if (_graded) return;
        try
        {
            (var attempt, _earned) = _grade(choice);
            _graded = true;
            foreach (var pick in Picks()) pick.Disabled = true;
            Skip.Visible = false;
            Submit.Disabled = false; Submit.Text = "继续出牌";
            string previous = _cached != null ? "已恢复上次作答 · " : "";
            Feedback.Text = previous + (attempt.Correct ? $"回答正确  +{StudyEngine.GoldPerCorrect} 金币 · 本次卡牌伤害 / 格挡 ×2" : attempt.Choice.Length == 0 ? $"已跳过 · 正确答案 {_q.Answer}" : $"回答错误 · 你选 {attempt.Choice}，正确答案 {_q.Answer}");
            Feedback.Modulate = attempt.Correct ? new Color("8eddb6") : new Color("f1b296");
            Feedback.Visible = true; Explanation.Visible = true;
            AssetText.Fill(Explanation, AssetText.Get(_q.Id, "explanation", _q.Explanation));
            UpdateProgress();
            Submit.GrabFocus();
            ScrollToFeedback();
        }
        catch (Exception ex)
        {
            Feedback.Visible = true; Feedback.Text = "作答保存失败，请重试：" + ex.Message;
            GD.PushError(ex.ToString());
        }
    }
    private async void ScrollToFeedback()
    {
        // Rich text height and the VBox layout settle over consecutive frames.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (IsInstanceValid(this) && IsInsideTree()) Scroll.EnsureControlVisible(Feedback);
    }
    private void UpdateProgress() => Progress.Text = $"本场答题金币 {_earned} / {_cap} · 剩余 {Math.Max(0, (_cap - _earned + StudyEngine.GoldPerCorrect - 1) / StudyEngine.GoldPerCorrect)} 次增益 · 答题金币胜利时领取";
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (!_graded && key.Keycode >= Key.A && key.Keycode <= Key.D) Select(key.Keycode.ToString());
        else if (key.Keycode == Key.Enter || key.Keycode == Key.KpEnter)
        {
            if (_graded) Closed?.Invoke();
            else if (_choice.Length > 0) Grade(_choice);
        }
        // Keep game shortcuts from ending a turn or opening other overlays during the quiz.
        GetViewport().SetInputAsHandled();
    }
}

