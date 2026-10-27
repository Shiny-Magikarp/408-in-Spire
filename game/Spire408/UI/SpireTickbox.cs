using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes;

namespace Spire408.UI;

public partial class SpireTickbox : NTickbox
{
    [Export] public Label CaptionLabel { get; set; } = null!;
    [Export] public string Caption { get; set; } = "启用";
    [Export] public bool InitiallyTicked { get; set; } = true;
    public event Action? Pressed;
    public string Text { get => Caption; set { Caption = value; CaptionLabel.Text = value; } }
    public bool Disabled { get => !IsEnabled; set { if (value) Disable(); else Enable(); } }
    public bool ButtonPressed
    {
        get => IsTicked;
        set { if (IsTicked == value) return; IsTicked = value; EmitSignal(NTickbox.SignalName.Toggled, this); }
    }
    public override void _Ready()
    {
        ConnectSignals();
        IsTicked = InitiallyTicked;
        CaptionLabel.Text = Caption;
        Released += _ => { if (IsEnabled) Pressed?.Invoke(); };
    }
    public void SetPressedNoSignal(bool value) => IsTicked = value;
    protected override void OnRelease()
    {
        if (NGame.Instance != null) base.OnRelease();
        else { IsTicked = !IsTicked; EmitSignal(NTickbox.SignalName.Toggled, this); }
    }
    public void Activate() { if (IsEnabled) { OnPressHandler(); OnReleaseHandler(); } }
}
