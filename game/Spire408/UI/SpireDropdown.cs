using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace Spire408.UI;

public partial class SpireDropdown : NSettingsDropdown
{
    [Export] public PackedScene ItemScene { get; set; } = null!;
    private readonly List<string> _items = [];
    private int _selected = -1;
    private bool _layoutPending;
    public event Action<long>? ItemSelected;
    public int Selected
    {
        get => _selected;
        set
        {
            _selected = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            _currentOptionLabel.SetTextAutoSize(_selected < 0 ? "" : _items[_selected]);
        }
    }
    public override void _Ready() => ConnectSignals();
    private void CloseChoices()
    {
        if (NControllerManager.Instance != null) CloseDropdown();
        else { GetNode<Control>("%DropdownContainer").Hide(); GetNode<Control>("%Dismisser").Hide(); }
    }
    protected override void OnFocus()
    {
        if (NControllerManager.Instance != null) base.OnFocus();
        else _currentOptionHighlight.Modulate = new Color("3C5B6B");
    }
    protected override void OnRelease()
    {
        if (NControllerManager.Instance != null) base.OnRelease();
        else
        {
            var list = GetNode<Control>("%DropdownContainer");
            list.Visible = !list.Visible;
        }
    }
    public void Clear() { CloseChoices(); ClearDropdownItems(); _items.Clear(); Selected = -1; }
    public void AddItem(string text)
    {
        int index = _items.Count;
        _items.Add(text);
        // Only the data-dependent choices are instantiated; the dropdown and its
        // scrollbar, frame and focus controls are bound in the editable scene.
        var item = ItemScene.Instantiate<NDropdownItem>();
        _dropdownItems.AddChild(item);
        item.CustomMinimumSize = new Vector2(CustomMinimumSize.X, 44);
        item.Text = text;
        item.Selected += _ => { Selected = index; CloseChoices(); ItemSelected?.Invoke(index); };
        if (_selected < 0) Selected = 0;
        RefreshChoicesLayout();
    }
    private async void RefreshChoicesLayout()
    {
        if (_layoutPending) return;
        _layoutPending = true;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(this) || !IsInsideTree()) return;
        GetNode<NDropdownContainer>("%DropdownContainer").RefreshLayout();
        _layoutPending = false;
    }
}
