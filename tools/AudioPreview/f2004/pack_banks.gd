extends SceneTree

# Godot --headless --path game --script pack_banks.gd -- <WAV directory>
func _initialize() -> void:
	var directory := OS.get_cmdline_user_args()[0]
	for bank in ["Pickup", "Pull", "Straight", "Overrun", "Lift", "Upshift", "Downshift"]:
		var clip := AudioStreamWAV.load_from_file(directory.path_join(bank + ".wav"))
		if not clip or not clip.stereo or clip.format != AudioStreamWAV.FORMAT_16_BITS:
			printerr("Expected stereo PCM16 in ", bank)
			quit(1)
			return
		if bank not in ["Upshift", "Downshift"]:
			clip.loop_mode = AudioStreamWAV.LOOP_FORWARD
			clip.loop_end = clip.data.size() / 4
		var err := ResourceSaver.save(clip, "res://art/cars/f2004/" + bank + ".res", ResourceSaver.FLAG_COMPRESS)
		if err != OK:
			printerr("Could not save ", bank, ": ", err)
			quit(1)
			return
	quit()
