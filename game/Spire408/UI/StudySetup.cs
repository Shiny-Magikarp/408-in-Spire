using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using Spire408.Core;

namespace Spire408.UI;

public partial class StudySetup : Control, IScreenContext
{
    [Export] public SpireTickbox EnabledToggle { get; set; } = null!;
    [Export] public SpireTickbox DataStructures { get; set; } = null!;
    [Export] public SpireTickbox Organization { get; set; } = null!;
    [Export] public SpireTickbox OperatingSystems { get; set; } = null!;
    [Export] public SpireTickbox Networks { get; set; } = null!;
    [Export] public SpireTickbox WrongToggle { get; set; } = null!;
    [Export] public SpireTickbox EssayToggle { get; set; } = null!;
    [Export] public SpireDropdown SourceFilter { get; set; } = null!;
    [Export] public SpireDropdown ChapterFilter { get; set; } = null!;
    [Export] public Label CountLabel { get; set; } = null!;
    [Export] public Label NoticeLabel { get; set; } = null!;
    [Export] public SpireButton StartButton { get; set; } = null!;
    [Export] public SpireButton CancelButton { get; set; } = null!;
    [Export] public SpireSettingsButton LibraryButton { get; set; } = null!;
    public Control? DefaultFocusedControl => StartButton;
    public event Action<StudySettings>? Confirmed;
    public event Action? Cancelled;
    public event Action<StudySettings>? BrowseRequested;
    private QuestionBank _bank = null!;
    private EssayBank? _essays;
    private LearningProfile _profile = null!;
    private StudySettings _settings = new();
    private List<string> _chapters = [""];
    private readonly string[] _sources = ["all", "marked", "strict"];

    public void Configure(QuestionBank bank, LearningProfile profile, StudySettings settings, EssayBank? essays = null)
    { _bank = bank; _profile = profile; _settings = settings; _essays = essays; }

    public override void _Ready()
    {
        EnabledToggle.ButtonPressed = _settings.Enabled;
        var books = Books();
        foreach (var (code, box) in books)
        {
            int count = _bank.Questions.Count(q => q.Subject == code);
            box.Text += $"   {count} 题";
            box.ButtonPressed = _settings.Subjects.Contains(code);
            box.Toggled += _ => { RebuildChapters(); Refresh(); };
        }
        WrongToggle.ButtonPressed = _settings.WrongOnly;
        EssayToggle.ButtonPressed = _settings.Essays;
        EssayToggle.Toggled += _ => Refresh();
        SourceFilter.AddItem("全部题目");
        SourceFilter.AddItem("屏蔽已标注真题");
        SourceFilter.AddItem("严格非真题（只用已核实来源）");
        SourceFilter.Selected = Math.Max(0, Array.IndexOf(_sources, _settings.ExamFilter));
        RebuildChapters();
        int initialChapter = _chapters.IndexOf(_settings.ChapterKey);
        ChapterFilter.Selected = Math.Max(0, initialChapter);
        EnabledToggle.Toggled += _ => Refresh();
        WrongToggle.Toggled += _ => Refresh();
        SourceFilter.ItemSelected += _ => Refresh();
        ChapterFilter.ItemSelected += _ => Refresh();
        StartButton.Pressed += () => Confirmed?.Invoke(ReadSettings());
        CancelButton.Pressed += () => Cancelled?.Invoke();
        LibraryButton.Pressed += () => BrowseRequested?.Invoke(ReadSettings());
        Refresh();
        StartButton.GrabFocus();
    }
    private (string code, SpireTickbox box)[] Books() => [("ds", DataStructures), ("co", Organization), ("os", OperatingSystems), ("cn", Networks)];
    private void RebuildChapters()
    {
        string previous = ChapterFilter.Selected >= 0 && ChapterFilter.Selected < _chapters.Count ? _chapters[ChapterFilter.Selected] : "";
        var selected = Books().Where(b => b.box.ButtonPressed).Select(b => b.code).ToHashSet();
        ChapterFilter.Clear(); _chapters = [""]; ChapterFilter.AddItem("全部章节");
        foreach (var group in _bank.Questions.Where(q => selected.Contains(q.Subject)).GroupBy(q => q.ChapterKey).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var q = group.First(); _chapters.Add(group.Key);
            ChapterFilter.AddItem($"{q.SubjectName} · {(q.ChapterTitle.Length > 0 ? q.ChapterTitle : "第 " + q.Chapter + " 章")}");
        }
        if (_essays != null)
            foreach (var q in _essays.Questions.Where(q => selected.Contains(q.Subject)).DistinctBy(q => q.ChapterKey).OrderBy(q => q.ChapterKey))
                if (!_chapters.Contains(q.ChapterKey)) { _chapters.Add(q.ChapterKey); ChapterFilter.AddItem($"{q.SubjectName} · 第 {q.Chapter} 章（大题）"); }
        ChapterFilter.Selected = Math.Max(0, _chapters.IndexOf(previous));
    }
    private StudySettings ReadSettings() => new()
    {
        Enabled = EnabledToggle.ButtonPressed, Subjects = Books().Where(b => b.box.ButtonPressed).Select(b => b.code).ToList(),
        WrongOnly = WrongToggle.ButtonPressed, Essays = EssayToggle.ButtonPressed,
        ExamFilter = _sources[Math.Max(0, SourceFilter.Selected)], ChapterKey = _chapters[Math.Max(0, ChapterFilter.Selected)]
    };
    private void Refresh()
    {
        var settings = ReadSettings();
        int count = StudyEngine.Filter(_bank, settings, _profile).Count;
        var essays = _essays != null ? EssayEngine.Filter(_essays, settings, _profile) : [];
        int essayCount = settings.Essays ? essays.Count : 0;
        int rewardCount = settings.Essays ? essays.Count(q => !q.ReviewOnly) : 0;
        CountLabel.Text = settings.Enabled ? $"本局选择题 {count:N0} 道 · 选择题错题 {_profile.Wrong.Count:N0} 道" : "本局关闭复习模式，使用原版金币规则。";
        if (settings.Enabled && settings.Essays && _essays != null)
            CountLabel.Text += $" · 大题 {essayCount} 道（可核分 {rewardCount} / 实验 {essays.Count(q => q.Experimental)}）";
        StartButton.Disabled = settings.Enabled && count == 0 && rewardCount == 0;
        LibraryButton.Disabled = essays.Count == 0;
        StartButton.Text = settings.Enabled ? "开始游戏" : "开始普通游戏";
        NoticeLabel.Text = settings.ExamFilter switch
        {
            "marked" => "未标注来源的题目仍可能包含真题；此选项只排除已识别的真题。",
            "strict" => "严格模式仅保留明确标注为练习或模拟的来源；来源不明的题不参与。",
            _ => "答对每题 +10 金币；普通 / 精英 / Boss 战上限 30 / 60 / 90，对应 3 / 6 / 9 次增益。金币胜利时结算。"
        };
        if (settings.WrongOnly && count == 0 && essayCount == 0) NoticeLabel.Text = "当前筛选下没有错题。请取消“仅做错题”或调整科目和章节。";
        else if (count == 0 && rewardCount > 0) NoticeLabel.Text = "当前仅有计奖大题：本局不强制选择题，通过问号事件复习。全部大题也可直接进入题库练习。";
        else if (count == 0 && essayCount > 0) NoticeLabel.Text = "当前仅有需自查的大题，请点击“大题题库”练习；自查题不参与事件计奖。";
    }
    public void ShowError(string text) => NoticeLabel.Text = text;
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); Cancelled?.Invoke(); }
    }
}

