using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using Spire408.Core;
using Spire408.Integration;

namespace Spire408.UI;

public partial class StudyCombatHud : Control
{
    [Export] public TextureRect Emblem { get; set; } = null!;
    [Export] public Label RemainingLabel { get; set; } = null!;
    [Export] public Label GoldLabel { get; set; } = null!;
    [Export] public StudyKnowledgeButton ActivationButton { get; set; } = null!;
    private RunState? _state;
    private string _displayKey = "";
    public void Configure(RunState state) => _state = state;
    public override void _Ready()
    {
        ActivationButton.Pressed += Toggle;
        Refresh();
    }
    public override void _Process(double delta) => Refresh();
    private bool CanInteract(StudyRun run) => StudyEngine.CanActivate(run)
        && _state?.Players[0].PlayerCombatState?.Phase == PlayerTurnPhase.Play
        && !CombatManager.Instance.PlayerActionsDisabled && !CombatManager.Instance.IsPaused
        && !CombatManager.Instance.IsOverOrEnding && NModalContainer.Instance?.OpenModal == null;

    private void Toggle()
    {
        if (_state == null || ModEntry.Active(_state) is not { } run || !CanInteract(run)) return;
        if (!StudyEngine.ToggleActivation(run)) return;
        ModEntry.Persist(_state, run);
        Refresh();
    }
    private void Refresh()
    {
        if (_state == null || ModEntry.Active(_state) is not { } run) { Visible = false; return; }
        Visible = CombatManager.Instance.IsInProgress;
        if (!Visible) return;
        var b = run.Battle;
        bool empty = run.Cursor >= run.Deck.Count && b.PendingId.Length == 0;
        bool interactive = CanInteract(run);
        string key = $"{b.Id}/{b.Earned}/{b.Cap}/{empty}/{b.Armed}/{interactive}";
        if (key == _displayKey) return;
        _displayKey = key;
        RemainingLabel.Text = $"{b.RemainingGains}";
        GoldLabel.Text = $"答题金币 {b.Earned}/{b.Cap}";
        ActivationButton.Display(b.Armed, interactive);
        TooltipText = $"剩余 {b.RemainingGains}/{b.MaximumGains} 次答题增益机会\n点击激活，再打出想要增益的牌；再次点击可取消。\n答对：+10 金币，该次卡牌伤害和格挡 ×2。\n答错或跳过不消耗机会，金币胜利时领取。\n触发后自动回到静息；自动出牌不使用激活。\n" +
            "卡牌和遗物正常产金，不占答题金币上限。\n" +
            (b.RemainingGains == 0 ? "答题金币已达上限，本场不可激活。" : empty ? "本局所选题目已做完。" : b.Armed ? "已为下一张主动打出的牌启用答题。" : "当前静息：普通出牌不触发答题。");
        ActivationButton.TooltipText = TooltipText;
    }
}
