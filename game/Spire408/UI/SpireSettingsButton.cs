using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace Spire408.UI;

// Original settings button behavior and editor-authored native image/material.
public partial class SpireSettingsButton : NSettingsButton
{
    [Export] public Label CaptionLabel { get; set; } = null!;
    [Export] public string Caption { get; set; } = "打开题库";
    protected override string[] Hotkeys => [];
    protected override string? ClickedSfx => NGame.Instance == null ? null : base.ClickedSfx;
    protected override string? HoveredSfx => NGame.Instance == null ? null : base.HoveredSfx;
    public event Action? Pressed;
    public bool Disabled { get => !IsEnabled; set { if (value) Disable(); else Enable(); } }
    public override void _Ready()
    {
        ConnectSignals();
        CaptionLabel.Text = Caption;
        Released += _ => { if (IsEnabled) Pressed?.Invoke(); };
    }
    protected override void OnFocus() { if (NGame.Instance != null) base.OnFocus(); }
    protected override void OnDisable() { base.OnDisable(); Modulate = new Color(0.5f, 0.5f, 0.5f); }
    protected override void OnEnable() { base.OnEnable(); Modulate = Colors.White; }
    public void Activate() { if (IsEnabled) { OnPressHandler(); OnReleaseHandler(); } }
}
