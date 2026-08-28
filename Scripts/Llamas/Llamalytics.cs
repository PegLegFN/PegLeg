using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

public static class Llamalytics
{
	const string Comment = "Llamalytics locally logs the contents of Llamas and other Card Pack types to a json file in appdata. This only applies to packs viewed or opened from within PegLeg. PegLeg does not share this file automatically, it's up to you if you want to manually share the file, but it contains no identifiable information.";

	class DataFile
	{
		[JsonInclude]
		string comment { get; } = Comment;
		[JsonInclude]
		public Dictionary<string, PackEntry> Packs { get; init; } = [];

		public void MergeFrom(DataFile other)
		{
			if (other == this)
				return;
			foreach (var kvp in other.Packs.Where(kvp=>Packs.ContainsKey(kvp.Key)))
			{
				Packs.Add(kvp.Key, kvp.Value);
			}
		}
	}

	record class PackEntry
	{
		public bool xRay { get; init; }
		public string type { get; init; }
		public int? displayLevel { get; init; } //default 0
		public string tierGroup { get; init; }
		public int? tier { get; init; } //default -1
		public int? overrideTier { get; init; } //default -1
		public int? packLevel { get; init; } //default 1
		public Dictionary<string, int> fixedRewards { get; init; }
		public string[][] choiceRewards { get; init; }
	}

	static DataFile currentData = new();

	public static void TryAddPreroll(GameItem prerollData)
	{
		if (!currentData.Packs.ContainsKey(prerollData.uuid))
		{
			currentData.Packs.Add(prerollData.uuid, new PackEntry());
		}
	}

	public static void TryAddCardpack(GameItem pack, JsonObject resultNotification)
	{
		if (!currentData.Packs.ContainsKey(pack.uuid))
		{
			currentData.Packs.Add(pack.uuid, new PackEntry());
		}
	}
}
