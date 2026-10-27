using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace Spire408.UI;

public partial class StudyKnowledgeButton : NButton
{
    [Export] public Control HoverMotion { get; set; } = null!;
    [Export] public Control AnimatedMotion { get; set; } = null!;
    [Export] public TextureRect Glow { get; set; } = null!;
    [Export] public AnimationPlayer Animations { get; set; } = null!;
    public event Action? Pressed;
    public bool Armed { get; private set; }
    public bool Disabled => !IsEnabled;
    private bool _hovered;
    private bool _displayed;
    private Tween? _hoverTween;
    protected override string? ClickedSfx => NGame.Instance == null ? null : base.ClickedSfx;
    protected override string? HoveredSfx => NGame.Instance == null ? null : base.HoveredSfx;

    public override void _Ready()
    {
        ConnectSignals();
        Released += _ => { if (IsEnabled) Pressed?.Invoke(); };
    }

    public void Display(bool armed, bool enabled)
    {
        if (_displayed && Armed == armed && IsEnabled == enabled) return;
        _displayed = true;
        Armed = armed;
        if (enabled)
        {
            Enable();
            Modulate = Colors.White;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            Animations.Play(armed ? "active" : "idle", 0.12);
        }
        else
        {
            Disable();
            MouseDefaultCursorShape = CursorShape.Arrow;
            Modulate = new Color(0.6f, 0.65f, 0.65f);
            Animations.Stop();
            AnimatedMotion.Scale = Vector2.One;
            Glow.Modulate = Colors.Transparent;
            _hoverTween?.Kill();
            HoverMotion.Scale = Vector2.One;
        }
    }

    protected override void OnFocus()
    {
        base.OnFocus();
        _hovered = true;
        TweenHover(new Vector2(1.07f, 1.07f));
    }

    protected override void OnUnfocus()
    {
        base.OnUnfocus();
        _hovered = false;
        TweenHover(Vector2.One);
    }

    protected override void OnPress()
    {
        base.OnPress();
        TweenHover(new Vector2(0.95f, 0.95f), 0.06);
    }

    protected override void OnRelease()
    {
        base.OnRelease();
        TweenHover(_hovered ? new Vector2(1.07f, 1.07f) : Vector2.One);
    }

    private void TweenHover(Vector2 scale, double duration = 0.14)
    {
        _hoverTween?.Kill();
        _hoverTween = CreateTween();
        _hoverTween.TweenProperty(HoverMotion, "scale", scale, duration).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    public override void _ExitTree()
    {
        _hoverTween?.Kill();
        base._ExitTree();
    }

    public void Activate() { if (IsEnabled) { OnPressHandler(); OnReleaseHandler(); } }
}
