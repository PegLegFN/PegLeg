using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

public partial class AppConfig
{
	public ThemeConfig theme;
	public class ThemeConfig
	{
		public string current = "";
	}
}

public partial class ThemeController : Node
{
	//todo: move to calender estimate
	static readonly DateTime referenceStartDate = new(2024, 1, 25);
	static readonly int[] seasonLengths =
	[
		10,
		11,
		11,
		11,
		9
	];
	static readonly int weeksInSeasonalYear = seasonLengths.Sum();

	const string blankTheme = "builtin_blank";
	static readonly string[] seasonThemes =
	[
		"builtin_autumn",
		"builtin_pirate",
		"builtin_desert",
		"builtin_spooky",
		"builtin_winter",
	];

	static string seasonTheme;
	bool seasonCheckAttached = false;

	public override void _Ready()
	{
		seasonTheme = GetSeasonThemeName();
		ImportThemes();
		Helpers.Defer(() =>
		{
			seasonCheckAttached = true;
			RefreshTimerController.OnDayChanged += CheckForNewSeason;
			SetActiveTheme(AppConfig.Get("theme", "current", ""));
			MusicController.ResumeMusic();
		}, 10);
	}

	public override void _ExitTree()
	{
		if (seasonCheckAttached)
			RefreshTimerController.OnDayChanged -= CheckForNewSeason;
	}

	void CheckForNewSeason()
	{
		var newSeasonTheme = GetSeasonThemeName();
		if (newSeasonTheme != seasonTheme)
		{
			seasonTheme = newSeasonTheme;
			if (selectedThemeName == "")
				SetActiveTheme(AppConfig.Get("theme", "current", ""));
		}
	}

	static string GetSeasonThemeName()
	{
		//TODO: timeline-related logic should be moved to a timeline class
		int weekCount = ((RefreshTimerController.RightNow.Date - referenceStartDate).Days / 7) % weeksInSeasonalYear;
		int seasonIndex = -1;
		for (int i = 0; i < seasonLengths.Length; i++)
		{
			if (weekCount < seasonLengths[i])
			{
				seasonIndex = i;
				break;
			}
			weekCount -= seasonLengths[i];
		}
		return seasonThemes[seasonIndex];
	}

	public static string selectedThemeName { get; private set; }
	public static string activeThemeName { get; private set; }
	public static AppTheme activeTheme { get; private set; }

	public static event Action OnThemeChanged;

	public static void SetActiveTheme(string themeId)
	{
		//themeId = appThemes.ContainsKey(themeId) ? themeId : "";
		AppConfig.Set("theme", "current", themeId);
		selectedThemeName = themeId;
		activeThemeName = GetWorkingThemeKey(selectedThemeName);
		activeTheme = appThemes[activeThemeName];
		OnThemeChanged?.Invoke();
	}

	static Dictionary<string, AppTheme> appThemes;
	public static string[] ThemeKeys => appThemes?.Keys?.ToArray() ?? [];
	static void ImportThemes()
	{
		if (appThemes is not null)
			return;
		appThemes = [];

		var themeList = PegLegResourceManager.LoadThemeList();
		if (!themeList.Contains(blankTheme))
			appThemes.Add(blankTheme, new() { displayName = "Blank" });
		foreach (var themeId in themeList)
		{
			if (PegLegResourceManager.LoadResourceObj<AppTheme>($"Themes/{themeId}/theme.json") is AppTheme theme)
			{
				appThemes.Add(themeId, theme);
				theme.SetRoot($"Themes/{themeId}/");
			}
		}
	}

	public static AppTheme GetTheme(string key) => appThemes[GetWorkingThemeKey(key)];
	public static bool HasTheme(string key) => appThemes.ContainsKey(key);
	public static string ThemeKeyOf(AppTheme theme) => appThemes?.FirstOrDefault(kvp => kvp.Value == theme).Key;
	public static string GetWorkingThemeKey(string key) =>
		appThemes.ContainsKey(key) ?
			key :
			(
				key != seasonTheme && appThemes.ContainsKey(seasonTheme) ?
					seasonTheme :
					blankTheme
			);
}

public class AppTheme : IJsonOnDeserialized
{
	[JsonRequired]
	public string displayName { get; init; }
	[JsonInclude]
	JsonElement backgrounds { get; init; }
	[JsonInclude]
	JsonElement music { get; init; }

	TextureFile[] backgroundFiles;
	public TextureFile[] Backgrounds => backgroundFiles ??= [];
	MusicPlaylist[] musicPlaylists;
	public MusicPlaylist[] Music => musicPlaylists ??= [];

	public void OnDeserialized()
	{
		backgroundFiles = TextureFile.FromJson(backgrounds);
		musicPlaylists = MusicPlaylist.FromJson(music);
	}

	public void SetRoot(string themePath)
	{
		Array.ForEach(backgroundFiles, b => b.SetRoot(themePath));
		Array.ForEach(musicPlaylists, m => m.SetRoot(themePath));
	}
	public string GetThemeKey() => ThemeController.ThemeKeyOf(this);

	public TextureFile PickBackground(TextureFile prev = null, float[] weights = null)
	{
		var key = GetThemeKey();
		if (AppConfig.TryGet("theme", $"{key}_bgpref", out int pref))
		{
			if (pref >= 0 && pref < backgroundFiles.Length)
				return backgroundFiles[pref];
		}
		return backgroundFiles.PickFromWeights(p => p.Weight, prev, weights);
	}

	public MusicPlaylist PickPlaylist(MusicPlaylist prev = null, float[] weights = null)
	{
		var key = GetThemeKey();
		if (AppConfig.TryGet("theme", $"{key}_musicpref", out int pref))
		{
			if (pref >= 0 && pref < musicPlaylists.Length)
				return musicPlaylists[pref];
		}
		return musicPlaylists.PickFromWeights(p => p.weight, prev, weights);
	}

	public class TextureFile : ThemeFile<Texture2D>
	{
		public static TextureFile[] FromJson(JsonElement ele) =>
			ele.FlexDeserialise<TextureFile>(e => new() { path = e.ToString() });
	}

	public class MusicPlaylist : IJsonOnDeserialized
	{
		public static MusicPlaylist[] FromJson(JsonElement ele) =>
			ele.FlexDeserialise<MusicPlaylist>(e => new() { tracks = e });
		[JsonInclude]
		JsonElement intros { get; init; }
		MusicFile[] introFiles;
		public MusicFile[] Intros => introFiles ?? [];
		[JsonRequired]
		[JsonInclude]
		JsonElement tracks { get; init; }
		MusicTrack[] musicTracks;
		public MusicTrack[] Tracks => musicTracks ?? [];
		[JsonInclude]
		string displayName { get; init; }
		[JsonIgnore]
		public string DisplayName => displayName ?? musicTracks?.FirstOrDefault()?.Layers?.FirstOrDefault()?.DisplayName;
		public float weight { get; init; } = 1;
		public float layerSwitchChance { get; init; } = 0.15f;
		public float trackSwitchChance { get; init; } = 0.5f;
		public float trackSwitchCooldown { get; init; } = 50;

		public void OnDeserialized()
		{
			musicTracks = MusicTrack.FromJson(tracks);
			introFiles = MusicFile.FromJson(intros);
		}

		public void SetRoot(string themePath)
		{
			Array.ForEach(musicTracks, t => t.SetRoot(themePath));
			if (introFiles is not null)
				Array.ForEach(introFiles, t => t.SetRoot(themePath));
		}

		public MusicFile PickIntro(MusicFile prev = null, float[] weights = null) =>
			introFiles.PickFromWeights(p => p.Weight, prev, weights);

		public MusicTrack PickTrack(MusicTrack prev = null, float[] weights = null) =>
			musicTracks.PickFromWeights(p => p.weight, prev, weights);
	}

	public class MusicTrack : IJsonOnDeserialized
	{
		public static MusicTrack[] FromJson(JsonElement ele) =>
			ele.FlexDeserialise<MusicTrack>(e => new() { layers = e });

		[JsonRequired]
		[JsonInclude]
		JsonElement layers { get; init; }
		MusicFile[] layerFiles;
		public MusicFile[] Layers => layerFiles ?? [];
		public float weight { get; init; } = 1;

		public void OnDeserialized()
		{
			layerFiles = MusicFile.FromJson(layers);
		}

		public void SetRoot(string themePath) =>
			Array.ForEach(layerFiles, l => l.SetRoot(themePath));

		public MusicFile PickLayer(MusicFile prev = null, float[] weights = null) =>
			layerFiles.PickFromWeights(p => p.Weight, prev, weights);

		public int IndexOf(MusicFile file) => Array.IndexOf(layerFiles, file);
	}

	public class MusicFile : ThemeFile<AudioStream>
	{
		public static MusicFile[] FromJson(JsonElement ele) =>
			ele.FlexDeserialise<MusicFile>(e => new() { path = e.ToString() });
	}

	public abstract class ThemeFile<T> where T : Resource
	{
		string themeRoot;
		[JsonRequired]
		public string path { get; init; }
		[JsonInclude]
		string displayName { get; init; }
		[JsonIgnore]
		public string DisplayName => displayName ?? path.Split('/')[^1].Split('.')[0].Replace("_", " ");
		public float weight { private get; init; } = 1;
		T file;

		public void SetRoot(string themeRoot) => this.themeRoot = themeRoot;

		[JsonIgnore]
		public float Weight => File is null ? 0 : weight;
		[JsonIgnore]
		public T File => file ??= PegLegResourceManager.LoadResourceAsset<T>(Path.Combine(themeRoot, path));
	}
}