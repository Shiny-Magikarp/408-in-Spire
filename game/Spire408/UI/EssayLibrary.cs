using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using Spire408.Core;

namespace Spire408.UI;

public partial class EssayLibrary : Control, IScreenContext
{
    [Export] public Label Summary { get; set; } = null!;
    [Export] public Label Description { get; set; } = null!;
    [Export] public LineEdit Search { get; set; } = null!;
    [Export] public SpireDropdown QuestionSelect { get; set; } = null!;
    [Export] public SpireButton Open { get; set; } = null!;
    [Export] public SpireButton Back { get; set; } = null!;
    public Control? DefaultFocusedControl => Search;
    public event Action<EssayQuestion>? PracticeRequested;
    public event Action? Closed;
    private List<EssayQuestion> _questions = [], _matching = [];
    private string _selectedId = "", _search = "";
    public EssayQuestion? Selected => QuestionSelect.Selected >= 0 && QuestionSelect.Selected < _matching.Count ? _matching[QuestionSelect.Selected] : null;
    public int MatchingCount => _matching.Count;
    public void Configure(List<EssayQuestion> questions, string selectedId, string search)
    { _questions = questions; _selectedId = selectedId; _search = search; }
    public override void _Ready()
    {
        Search.Text = _search;
        Search.TextChanged += _ => Refresh();
        QuestionSelect.ItemSelected += _ => Describe();
        Open.Pressed += () => { if (Selected is { } q) PracticeRequested?.Invoke(q); };
        Back.Pressed += () => Closed?.Invoke();
        Refresh();
    }
    private void Refresh()
    {
        string term = Search.Text.Trim(); string previous = Selected?.Id ?? _selectedId;
        _matching = _questions.Where(q => term.Length == 0 ||
            (q.Title + " " + q.SubjectName + " " + q.Topic + " " + q.Id + " " + q.Stem).Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        QuestionSelect.Clear();
        foreach (var q in _matching) QuestionSelect.AddItem((q.ReviewOnly ? "[自查] " : q.Experimental ? "[实验核分] " : "[可核分] ") + q.SubjectName + " · " + q.Title);
        if (_matching.Count > 0) QuestionSelect.Selected = Math.Max(0, _matching.FindIndex(q => q.Id == previous));
        Summary.Text = $"当前筛选 {_questions.Count} 道 · 搜索结果 {_matching.Count} 道 · 可核分 {_matching.Count(q => !q.ReviewOnly)} 道 · 其中实验 {_matching.Count(q => q.Experimental)} 道\n沿用复习计划中的科目、章节、真题及错题筛选。题库练习不计奖。";
        Open.Disabled = _matching.Count == 0; Describe();
    }
    private void Describe()
    {
        Description.Text = Selected is { } q ? $"{q.Title}\n{q.GradingScope}\n" +
            (q.Source.Repository.Length > 0 ? $"原卷分值：{q.OriginalPoints} · " : "项目自编练习 · ") +
            $"题目 ID：{q.Id}\n\n{q.Stem[..Math.Min(q.Stem.Length, 350)]}" : "没有匹配题目。可修改搜索词，或返回调整复习筛选。";
    }
    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); Closed?.Invoke(); }
    }
}
