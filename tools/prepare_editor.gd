extends SceneTree

# Development cache only: read native assets from the user's installed game.
# These files are ignored by Git and never included in the mod PCK.
var target: String
var seen = {}

func _initialize():
    target = OS.get_environment("SPIRE408_EDITOR_PROJECT")
    if target.is_empty() or not FileAccess.file_exists(target.path_join("project.godot")):
        push_error("SPIRE408_EDITOR_PROJECT must name the mod's existing Godot project.")
        quit(1)
        return
    for path in [
        "res://src/Core/Nodes/GodotExtensions/NClickableControl.cs",
        "res://src/Core/Nodes/GodotExtensions/NButton.cs",
        "res://src/Core/Nodes/GodotExtensions/NDropdown.cs",
        "res://src/Core/Nodes/CommonUi/NTickbox.cs",
        "res://src/Core/Nodes/Screens/Settings/NSettingsDropdown.cs",
        "res://src/Core/Nodes/Screens/Settings/NSettingsButton.cs",
        "res://scenes/ui/abandon_run_yes_button.tscn",
        "res://scenes/ui/abandon_run_no_button.tscn",
        "res://scenes/ui/tickbox.tscn",
        "res://scenes/ui/selection_reticle.tscn",
        "res://scenes/screens/settings_dropdown.tscn",
        "res://scenes/ui/dropdown_item.tscn",
        "res://themes/kreon_regular_shared.tres",
        "res://themes/kreon_bold_glyph_space_two.tres",
        "res://images/ui/reward_screen/reward_skip_button.png",
        "res://shaders/hsv.gdshader",
        "res://images/atlases/ui_atlas.sprites/popup_vertical.tres",
        "res://images/packed/common_ui/small_scrollbar_track_center.png",
        "res://images/packed/common_ui/small_scrollbar_train.png"
    ]:
        cache(path)
    print("[Spire408 editor] Cached ", seen.size(), " native resource dependencies.")
    quit()

func write(path: String, bytes: PackedByteArray):
    var output = target.path_join(path.trim_prefix("res://"))
    DirAccess.make_dir_recursive_absolute(output.get_base_dir())
    var file = FileAccess.open(output, FileAccess.WRITE)
    if file == null:
        push_error("Cannot write development cache: " + output)
        quit(1)
        return
    file.store_buffer(bytes)

func cache(path: String):
    if seen.has(path): return
    seen[path] = true
    if path.ends_with(".cs"):
        write(path, "// Native script supplied by the local sts2.dll; excluded from compilation.\n".to_utf8_buffer())
        return
    var imported = path + ".import"
    if FileAccess.file_exists(imported):
        write(imported, FileAccess.get_file_as_bytes(imported))
        var config = ConfigFile.new()
        config.load(imported)
        for key in config.get_section_keys("remap"):
            var value = config.get_value("remap", key)
            if value is String and value.begins_with("res://"):
                cache(value)
        return
    if not FileAccess.file_exists(path):
        push_warning("Native resource not stored as a file: " + path)
        return
    var bytes = FileAccess.get_file_as_bytes(path)
    if path.ends_with(".tscn") or path.ends_with(".tres"):
        # Native UIDs belong to the game's project; use paths in this dev project.
        var regex = RegEx.new()
        regex.compile(' uid="uid://[^\"]+"')
        bytes = regex.sub(bytes.get_string_from_utf8(), "", true).to_utf8_buffer()
    write(path, bytes)
    for dependency in ResourceLoader.get_dependencies(path):
        var segments = dependency.split("::")
        for segment in segments:
            if segment.begins_with("res://"):
                cache(segment)
