using Godot;
using System;
using System.Reflection;

public static class PaletteHelper
{
	public static event Action OnPaletteUpdated;
	static bool initialised = false;

	public static void Initialise()
	{
		if (initialised) 
			return;
		initialised = true;
		AppConfig.OnConfigChanged += OnConfigChanged;
		RarityColours = InitArray("rarity_", PegLegResourceManager.supplimentaryData.RarityColours);
		ZoneColours = InitArray("zone_", PegLegResourceManager.supplimentaryData.ZoneColours);
		OnPaletteUpdated?.Invoke();
	}

	public static Color DefaultFor(string key)
	{
		if (!IdxFromKey(key, out var idx))
			return DefaultFor(key, 0);
		return DefaultFor(key[..(idx.ToString().Length + 1)], idx);
	}

	public static Color DefaultFor(string prefix, int index)
	{
		if (prefix[^1] == '_')
			prefix = prefix[..^1];
		return prefix switch
		{
			"rarity_" => PegLegResourceManager.supplimentaryData.RarityColours[index],
			"zone_" => PegLegResourceManager.supplimentaryData.ZoneColours[index],
			_ => Colors.Black
		};
	}

	private static void OnConfigChanged(string section, string key, System.Text.Json.Nodes.JsonNode value)
	{
		if (section != "palette")
			return;
		var strColor = value?.ToString();
		bool useDefault = Color.HtmlIsValid(strColor);
		var newColour = useDefault ? default : Color.FromHtml(strColor);

		bool changed = key switch
		{
			_ when key.StartsWith("rarity_") => UpdateArrayColor(key, newColour, useDefault, PegLegResourceManager.supplimentaryData.RarityColours, (i, c) => RarityColours[i] = c),
			_ => false
		};
		if (changed)
			OnPaletteUpdated?.Invoke();
	}

	static bool IdxFromKey(string key, out int idx) => int.TryParse(key.Split('_')[^1], out idx) && idx >= 0;
	static Color[] InitArray(string prefix, Color[] defaultCols)
	{
		var result = new Color[defaultCols.Length];
		for (int i = 0; i < defaultCols.Length; i++)
		{
			if (AppConfig.TryGet("palette", $"{prefix}{i}", out string colString) && Color.HtmlIsValid(colString))
				result[i] = Color.FromHtml(colString);
			else
				result[i] = defaultCols[i];
		}
		return result;
	}

	static bool UpdateArrayColor(string key, Color newColour, bool useDefault, Color[] fallback, Action<int, Color> output)
	{
		if (!IdxFromKey(key, out var idx) || idx >= fallback.Length)
			return false;
		output?.Invoke(idx, useDefault ? fallback[idx] : newColour);
		return true;
	}

	public static Color[] RarityColours { get; private set; }
	public static Color[] ZoneColours { get; private set; }
}
