using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace Spire408.UI;

public static class NativeAssets
{
    public static void MountForPreview()
    {
        if (!ResourceLoader.Exists("res://scenes/ui/abandon_run_yes_button.tscn"))
        {
            string pack = ProjectSettings.GlobalizePath("res://../../SlayTheSpire2.pck");
            if (!ProjectSettings.LoadResourcePack(pack, replaceFiles: false))
                throw new InvalidOperationException("Run Preview.ps1 to prepare local native UI resources.");
        }
        var bridge = typeof(GodotObject).Assembly.GetType("Godot.Bridge.ScriptManagerBridge")!;
        bridge.GetMethod("LookupScriptsInAssembly", BindingFlags.Public | BindingFlags.Static)!
            .CreateDelegate<Action<Assembly>>()(typeof(NButton).Assembly);
    }
}
