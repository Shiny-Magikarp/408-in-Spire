#if TOOLS
using Godot;
using Spire408.UI;

[Tool]
public partial class NativeUiPlugin : EditorPlugin
{
    public override void _EnterTree()
    {
        try { NativeAssets.MountForPreview(); }
        catch (Exception error) { GD.PushError(error.ToString()); }
    }
}
#endif
