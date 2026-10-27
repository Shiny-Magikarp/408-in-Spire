using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using Spire408.Core;

namespace Spire408.UI;

public enum EssayCloseReason { Back, Abandon, Claim }

public partial class EssayPanel : Control, IScreenContext
{
    [Export] public Label Heading { get; set; } = null!;
    [Export] public Label Rules { get; set; } = null!;
    [Export] public RichTextLabel Stem { get; set; } = null!;
    [Export] public VBoxContainer Part1 { get; set; } = null!;
    [Export] public VBoxContainer Part2 { get; set; } = null!;
    [Export] public VBoxContainer Part3 { get; set; } = null!;
    [Export] public VBoxContainer Part4 { get; set; } = null!;
    [Export] public Label Prompt1 { get; set; } = null!;
    [Export] public Label Prompt2 { get; set; } = null!;
    [Export] public Label Prompt3 { get; set; } = null!;
    [Export] public Label Prompt4 { get; set; } = null!;
    [Export] public TextEdit Answer1 { get; set; } = null!;
    [Export] public TextEdit Answer2 { get; set; } = null!;
    [Export] public TextEdit Answer3 { get; set; } = null!;
    [Export] public TextEdit Answer4 { get; set; } = null!;
    [Export] public RichTextLabel Feedback { get; set; } = null!;
    [Export] public ScrollContainer Scroll { get; set; } = null!;
    [Export] public SpireButton Submit { get; set; } = null!;
    [Export] public SpireButton Back { get; set; } = null!;
    [Export] public SpireButton Abandon { get; set; } = null!;
    [Export] public SpireButton Previous { get; set; } = null!;
    [Export] public SpireButton Next { get; set; } = null!;
    [Export] public Label Page { get; set; } = null!;
    [Export] public SpireTickbox WrongToggle { get; set; } = null!;
    [Export] public Godot.Timer DraftTimer { get; set; } = null!;
    public Control? DefaultFocusedControl => Answer1;
    public event Action<EssayCloseReason>? Closed;
    public event Action<List<string>>? DraftChanged;
    public event Action<bool>? WrongChanged;
    public List<string> Answers { get { CapturePage(); return [.. _draft]; } }
    private EssayQuestion _question = null!;
    private List<string> _draft = [];
    private EssayAttempt? _result;
    private Func<List<string>, CancellationToken, Task<EssayAttempt>> _grade = null!;
    private CancellationTokenSource? _grading;
    private string _cost = "";
    private bool _bonusAvailable, _practice, _initiallyWrong, _binding, _manualWrong, _syncWrong;
    private int _page;
    public bool Busy { get; private set; }
    public int PageIndex => _page;

    public void Configure(EssayQuestion question, List<string> draft, EssayAttempt? result, string cost, bool bonusAvailable,
        Func<List<string>, CancellationToken, Task<EssayAttempt>> grade, bool practice = false, bool initiallyWrong = false, bool manualWrong = false)
    {
        _question = question; _draft = Enumerable.Range(0, question.Parts.Count).Select(i => i < draft.Count ? draft[i] : "").ToList();
        _result = result; _cost = cost; _bonusAvailable = bonusAvailable; _grade = grade;
        _practice = practice; _initiallyWrong = initiallyWrong; _manualWrong = manualWrong;
    }
    private TextEdit[] Inputs() => [Answer1, Answer2, Answer3, Answer4];
    public override void _Ready()
    {
        Heading.Text = $"瓦库 · {_question.SubjectName} · {_question.Title}";
        string provenance = _question.Source.Repository.Length == 0 ? "项目自编练习" :
            $"来源：{_question.Source.Name} · 原卷 {_question.OriginalPoints} 分" + (_question.Source.Adapted ? " · 真题改编" : "");
        Rules.Text = provenance + "\n" + (_question.GradingScope.Length > 0 ? _question.GradingScope + "\n" : "");
        Rules.Text += _practice ? "题库练习不产生金币或物品，可暂存后继续。自查题不显示自动分数。" :
            "每个事件只提交一次。40～59 分：15 金币；60～74：25；75～89：35+稀有卡三选一；90～100：40+遗物。\n"
            + (_bonusAvailable ? "额外卡牌/遗物每幕最多一次。" : "本幕额外奖励已领取。") + " 原选项代价：" + _cost;
        AssetText.Fill(Stem, AssetText.Essay(_question.Id, "stem", _question.Stem));
        foreach (var input in Inputs()) input.TextChanged += () => { if (!_binding) DraftTimer.Start(); };
        Previous.Pressed += () => ChangePage(-1); Next.Pressed += () => ChangePage(1);
        WrongToggle.ButtonPressed = _initiallyWrong;
        WrongToggle.Toggled += _ =>
        {
            if (_syncWrong) return;
            _manualWrong = WrongToggle.ButtonPressed;
            WrongChanged?.Invoke(_manualWrong);
        };
        Abandon.Visible = !_practice;
        Back.Text = _practice ? "返回题库" : "暂存返回";
        Submit.Text = _question.ReviewOnly ? "对照解析" : "提交评分";
        Submit.Pressed += async () =>
        {
            if (_result != null) { SaveDraft(); Closed?.Invoke(EssayCloseReason.Claim); }
            else await Grade();
        };
        Back.Pressed += () => Close(EssayCloseReason.Back);
        Abandon.Pressed += () => Close(EssayCloseReason.Abandon);
        DraftTimer.Timeout += SaveDraft;
        TreeExiting += () => _grading?.Cancel();
        BindPage();
        if (_result != null) ShowResult();
    }
    private void CapturePage()
    {
        if (_binding || !IsNodeReady()) return;
        var inputs = Inputs();
        for (int i = 0; i < 4 && _page * 4 + i < _draft.Count; i++) _draft[_page * 4 + i] = inputs[i].Text;
    }
    private void BindPage()
    {
        _binding = true;
        var rows = new[] { Part1, Part2, Part3, Part4 };
        var prompts = new[] { Prompt1, Prompt2, Prompt3, Prompt4 };
        var inputs = Inputs();
        for (int slot = 0; slot < 4; slot++)
        {
            int index = _page * 4 + slot; rows[slot].Visible = _result == null && index < _question.Parts.Count;
            if (!rows[slot].Visible) continue;
            var part = _question.Parts[index];
            prompts[slot].Text = $"{index + 1}. {part.Prompt}" + (_question.ReviewOnly ? "" : $"  [{part.Points} 练习分]");
            inputs[slot].Text = _draft[index]; inputs[slot].Editable = _result == null;
            inputs[slot].CustomMinimumSize = new Vector2(0, _question.ReviewOnly || part.Kind == "rubric" ? 260 : 100);
            inputs[slot].PlaceholderText = part.Kind is "review" or "concept" or "rubric" ? "写下你的过程、伪代码或结论…" : "只填写本项最终结果";
        }
        int pages = (_question.Parts.Count + 3) / 4;
        Previous.Visible = Next.Visible = Page.Visible = pages > 1 && _result == null;
        Previous.Disabled = _page == 0; Next.Disabled = _page >= pages - 1;
        Page.Text = $"评分点 {_page + 1} / {pages}";
        _binding = false;
    }
    private void ChangePage(int delta)
    {
        CapturePage(); _page = Math.Clamp(_page + delta, 0, (_question.Parts.Count - 1) / 4);
        BindPage(); SaveDraft();
    }
    private void SaveDraft() { if (_result == null) DraftChanged?.Invoke(Answers); }
    private void Close(EssayCloseReason reason) { _grading?.Cancel(); SaveDraft(); Closed?.Invoke(reason); }
    private async Task Grade()
    {
        if (Busy) return;
        SaveDraft(); Busy = true; Submit.Disabled = true;
        Feedback.Visible = true; Feedback.Text = _question.ReviewOnly ? "正在打开参考解析…" : "瓦库正在离线核对评分点…";
        _grading = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var result = await _grade(Answers, _grading.Token);
            if (!IsInstanceValid(this) || !IsInsideTree() || _grading.IsCancellationRequested) return;
            _result = result; ShowResult();
        }
        catch (Exception error)
        {
            GD.PushError("[Spire408] 离线评分未完成：" + error);
            if (IsInstanceValid(this) && IsInsideTree())
                Feedback.Text = "本次批改未完成，草稿已保留，没有记为答错。可重试或暂存返回。\n" + error.Message;
        }
        finally
        {
            if (IsInstanceValid(this) && IsInsideTree()) { Busy = false; Submit.Disabled = false; }
        }
    }
    private void ShowResult()
    {
        BindPage();
        var result = _result!;
        if (_practice) Back.Hide();
        var text = new System.Text.StringBuilder(_question.ReviewOnly ? "[b]自查解析 · 本题未自动评分[/b]\n" : $"[b]瓦库评分：{result.Score} / 100（练习分）[/b]\n");
        if (_question.Experimental) text.Append("实验性语义评分，可能误判；尚未验证代码执行或证明过程，请对照完整解析复核。\n");
        for (int i = 0; i < result.Grades.Count; i++)
        {
            var g = result.Grades[i];
            if (!_question.ReviewOnly) text.Append($"\n{i + 1}. {g.Earned}/{g.Maximum} 分 · {g.Feedback}\n");
            text.Append("你的作答：" + AssetText.Escape(result.Answers[i]) + "\n");
            if (!_question.ReviewOnly) text.Append("核对结果：" + AssetText.Escape(_question.Parts[i].Reference) + "\n");
            foreach (var criterion in g.Criteria)
                text.Append((criterion.Status == "awarded" ? "✓ " : criterion.Status == "review" ? "待复核 · " : "未命中 · ")
                    + AssetText.Escape(criterion.Reference) + "\n");
        }
        if (_question.ReferenceAnswer.Length > 0)
        {
            text.Append("\n[b]完整参考答案[/b]\n" + AssetText.Essay(_question.Id,"reference_answer",_question.ReferenceAnswer));
            text.Append("\n\n[b]解析[/b]\n" + AssetText.Essay(_question.Id,"explanation",_question.Explanation));
        }
        if (_practice) text.Append("\n\n题库练习不产生金币或物品。可用上方勾选框更新大题错题本。");
        else
        {
            var reward = EssayEngine.RewardFor(result.Score);
            text.Append($"\n本题可领取 {reward.Gold} 金币");
            if (_bonusAvailable && !result.NeedsReview && reward.Bonus != "none") text.Append(reward.Bonus == "card" ? "，以及稀有卡三选一" : "，以及一件遗物");
            if (result.NeedsReview) text.Append("。含待复核项目，不发额外奖励");
        }
        AssetText.Fill(Feedback, text.ToString()); Feedback.Visible = true;
        if (!result.NeedsReview && !_manualWrong)
        {
            _syncWrong = true; WrongToggle.ButtonPressed = result.Score != 100; _syncWrong = false;
        }
        Submit.Text = _practice ? "返回题库" : "领取奖励";
        ScrollToResult();
    }
    private async void ScrollToResult()
    {
        var tree = GetTree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (IsInstanceValid(this) && IsInsideTree()) Scroll.ScrollVertical = (int)Feedback.Position.Y;
    }
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); Close(EssayCloseReason.Back); }
    }
}
