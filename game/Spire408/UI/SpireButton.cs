using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes;

namespace Spire408.UI;

// Uses the original popup button scene, animation, focus behavior and sound.
public partial class SpireButton : NPopupYesNoButton
{
    [Export] public string Caption { get; set; } = "继续";
    protected override string[] Hotkeys => [];
    protected override string? ClickedSfx => NGame.Instance == null ? null : base.ClickedSfx;
    protected override string? HoveredSfx => NGame.Instance == null ? null : base.HoveredSfx;
    public event Action? Pressed;
    public string Text { get => Caption; set { Caption = value; if (IsNodeReady()) SetText(value); } }
    public bool Disabled { get => !IsEnabled; set { if (value) Disable(); else Enable(); } }
    public override void _Ready()
    {
        base._Ready();
        SetText(Caption);
        Released += _ => { if (IsEnabled) Pressed?.Invoke(); };
    }
    protected override void OnDisable() { base.OnDisable(); Modulate = new Color(0.5f, 0.5f, 0.5f); }
    protected override void OnEnable() { base.OnEnable(); Modulate = Colors.White; }
    public void Activate() { if (IsEnabled) { OnPressHandler(); OnReleaseHandler(); } }
}
