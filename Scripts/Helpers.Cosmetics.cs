using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public static partial class Helpers
{
	public const string cosmeticSalsa = "676b8175-a049-4f03-b829-323c95153a43";

	static readonly string[] cosmeticKeys = ["brItems", "tracks", "instruments", "cars", "legoKits"];

	public static int GetCosmeticItemCounts(this JsonObject from)
	{
		int total = 0;
		foreach (var key in cosmeticKeys)
			if (from[key] is JsonArray arr)
				total += arr.Count;
		return total;
	}
	public static JsonObject GetFirstCosmeticItem(this JsonObject from)
	{
		foreach (var key in cosmeticKeys)
			if (from[key] is JsonArray { Count: > 0 } arr)
				return arr[0].AsObject();
		return null;
	}
	public static JsonArray MergeCosmeticItems(this JsonObject from)
	{
		List<JsonNode> resultNodes = [];
		foreach (var key in cosmeticKeys)
			if (from[key] is JsonArray arr)
				resultNodes.AddRange(arr);
		if (from["fallbackItems"] is JsonArray fallbackItems)
			resultNodes.AddRange(fallbackItems);
		return resultNodes.Count == 0 ? null : new(resultNodes.Select(n => n.SafeDeepClone()).ToArray());
	}

	//why merge into a json array when you can just have a regular array
	public static JsonObject[] GetAllCosmetics(this JsonObject from) =>
		cosmeticKeys
			.Select(key => from[key]?.AsArray())
			.Where(arr => arr is not null)
			.SelectMany(arr => arr)
			.Select(n => n.AsObject())
			.ToArray();
}