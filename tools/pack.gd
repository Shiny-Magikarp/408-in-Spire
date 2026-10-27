extends SceneTree

func _initialize():
    var output = ""
    for arg in OS.get_cmdline_user_args():
        if arg.begins_with("--output="):
            output = arg.substr(9)
    if output.is_empty():
        push_error("Expected --output=<absolute pck path>")
        quit(1)
        return
    # Store import-independent textures so the game can load relic assets from
    # the mod PCK without shipping an editor's .godot cache.
    for name in ["study_book", "study_book_icon", "study_book_outline", "knowledge_resource"]:
        var base = "res://Spire408/Art/" + name
        var img = Image.new()
        if img.load_png_from_buffer(FileAccess.get_file_as_bytes(base + ".png")) != OK:
            push_error("Cannot load relic image " + base)
            quit(1)
            return
        if ResourceSaver.save(ImageTexture.create_from_image(img), base + ".res") != OK:
            quit(1)
            return
    var packer = PCKPacker.new()
    if packer.pck_start(output) != OK:
        quit(1)
        return
    if not add_directory(packer, "res://Spire408"):
        quit(1)
        return
    var result = packer.flush()
    print("408 PCK result: ", result)
    quit(0 if result == OK else 1)

func add_directory(packer: PCKPacker, folder: String) -> bool:
    var dir = DirAccess.open(folder)
    if dir == null:
        push_error("Missing directory " + folder)
        return false
    for name in dir.get_files():
        if name.ends_with(".import"):
            continue
        var resource = folder.path_join(name)
        if packer.add_file(resource, resource) != OK:
            push_error("Cannot pack " + resource)
            return false
    for name in dir.get_directories():
        if not add_directory(packer, folder.path_join(name)):
            return false
    return true
