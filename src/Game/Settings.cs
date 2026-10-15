using System;
using Godot;
using Mancala.Core;

namespace Mancala.Game;

/// <summary>Player preferences, saved on the device only (the privacy policy promises nothing leaves it).</summary>
public sealed class Settings
{
	private const string Path = "user://settings.cfg";
	private const string Section = "settings";

	public bool Muted { get; set; }
	public GameMode Mode { get; set; } = GameMode.PlayerVsComputer;
	public Difficulty Difficulty { get; set; } = Difficulty.Easy;

	/// <summary>Finished games since the last between-games ad; see <see cref="Renegade.Core.AdSchedule"/>.</summary>
	public int GamesSinceAd { get; set; }

	public static Settings Load()
	{
		var settings = new Settings();
		var file = new ConfigFile();
		if (file.Load(Path) != Error.Ok)
			return settings;
		settings.Muted = file.GetValue(Section, "muted", false).AsBool();
		settings.Mode = ReadEnum(file, "mode", settings.Mode);
		settings.Difficulty = ReadEnum(file, "difficulty", settings.Difficulty);
		settings.GamesSinceAd = file.GetValue(Section, "games_since_ad", 0).AsInt32();
		return settings;
	}

	public void Save()
	{
		var file = new ConfigFile();
		file.SetValue(Section, "muted", Muted);
		file.SetValue(Section, "mode", Mode.ToString());
		file.SetValue(Section, "difficulty", Difficulty.ToString());
		file.SetValue(Section, "games_since_ad", GamesSinceAd);
		file.Save(Path);
	}

	private static T ReadEnum<T>(ConfigFile file, string key, T fallback) where T : struct, Enum =>
		Enum.TryParse(file.GetValue(Section, key, fallback.ToString()).AsString(), out T value) ? value : fallback;
}
