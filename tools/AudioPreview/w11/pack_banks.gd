extends SceneTree

# Godot --headless --path game --script <pack_banks.gd> -- <WAV directory>
const LOOPS := ["LowZ", "LowA", "LowB", "LowC", "LowD", "LowE", "LowF", "LowTop",
	"HighZ", "HighA", "HighB", "HighC", "HighTop", "Idle", "CoastLow", "CoastHigh", "Limiter"]
const SHOTS := ["UpshiftLow", "UpshiftHigh", "DownshiftLow", "DownshiftHigh"]

func _initialize() -> void:
	var directory := OS.get_cmdline_user_args()[0]
	for bank in LOOPS + SHOTS:
		var wav := AudioStreamWAV.load_from_file(directory.path_join(bank + ".wav"))
		if not wav or not wav.stereo or wav.format != AudioStreamWAV.FORMAT_16_BITS:
			printerr("Expected stereo PCM16 in ", bank)
			quit(1)
			return
		if bank in LOOPS:
			wav.loop_mode = AudioStreamWAV.LOOP_FORWARD
			wav.loop_end = wav.data.size() / 4
		var result := ResourceSaver.save(wav, "res://art/cars/w11/" + bank + ".res", ResourceSaver.FLAG_COMPRESS)
		if result != OK:
			printerr("Could not save ", bank, ": ", result)
			quit(1)
			return
	quit()
