using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

public static partial class Helpers
{
	public static class JsonOptions
	{
		public static JsonSerializerOptions Fields { get; private set; } = new()
		{
			IncludeFields = true,
			WriteIndented = true
		};
		public static JsonSerializerOptions CamelCase { get; private set; } = new()
		{
			IncludeFields = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true
		};
	}

	public static T[] FlexDeserialise<T>(this JsonElement ele, Func<JsonElement, T> innerCtor, int depth = 1) =>
		ele.ValueKind switch
		{
			JsonValueKind.Array when depth > 0 => ele.Deserialize<JsonElement[]>().SelectMany(e => e.FlexDeserialise<T>(innerCtor, depth - 1)).ToArray(),
			JsonValueKind.Object => [ele.Deserialize<T>()],
			JsonValueKind.Undefined => [],
			_ => [innerCtor(ele).FakeDeserialise()]
		};

	static T FakeDeserialise<T>(this T construct)
	{
		if (construct is IJsonOnDeserialized jsonConstruct)
			jsonConstruct.OnDeserialized();
		return construct;
	}

	public static T SafeDeepClone<T>(this T toReserialise) where T : JsonNode
	{
		if (toReserialise is null)
			return null;
		lock (toReserialise)
		{
			return (T)toReserialise.DeepClone();
		}
	}

	public static JsonNode DetachNode(this JsonNode targetParent, string name) => targetParent.AsObject().DetachNode(name);
	public static JsonNode DetachNode(this JsonObject targetParent, string name)
	{
		if (targetParent is null || name is null || !targetParent.ContainsKey(name))
			return null;
		var targetNode = targetParent[name];
		targetParent.Remove(name);
		return targetNode;
	}

	public static JsonNode DetachNode(this JsonNode targetParent, int idx) => targetParent.AsArray().DetachNode(idx);
	public static JsonNode DetachNode(this JsonArray targetParent, int idx)
	{
		if (targetParent is null || targetParent.Count == 0 || targetParent.Count <= idx)
			return null;
		var targetNode = targetParent[idx];
		targetParent.Remove(targetNode);
		return targetNode;
	}

	[GeneratedRegex("^([a-z]+)(?:\\[(\\d)\\])?$", RegexOptions.IgnoreCase)]
	private static partial Regex NodePathParserGeneratedRegex();
	public static bool TryGetNodeFromPath(this JsonObject root, string path, out JsonNode node)
	{
		node = null;
		var splitPath = path.Split('.');
		if (splitPath.Length == 0)
			return false;
		JsonNode current = root;
		for (int i = 0; i < splitPath.Length; i++)
		{
			var parsedPath = NodePathParserGeneratedRegex().Match(splitPath[i]);
			if (!parsedPath.Success)
				return false;
			if (current is not JsonObject)
				return false;
			current = current[parsedPath.Groups[0].Value];

			if (parsedPath.Groups.Count == 2)
			{
				//handle array index
				if (current is not JsonArray)
					return false;
				current = current[int.Parse(parsedPath.Groups[1].Value)];
			}
		}
		return true;
	}

	public static DateTime AsTime(this JsonNode value) =>
		value.Deserialize<DateTime>();

	public static JsonNode[] DetachAll(this JsonArray targetParent)
	{
		if (targetParent is null)
			return null;
		var values = targetParent.ToArray();
		targetParent.Clear();
		return values;
	}
	public static KeyValuePair<string, JsonNode>[] DetachAll(this JsonObject targetParent)
	{
		if (targetParent is null)
			return null;
		var values = targetParent.ToArray();
		targetParent.Clear();
		return values;
	}

	public static JsonObject AsFlexibleObject(this JsonNode node, string objectKey)
	{
		if (node is JsonObject nodeObj)
			return nodeObj;
		return new() { [objectKey] = node.SafeDeepClone() };
	}

	public static JsonArray AsFlexibleArray(this JsonNode node)
	{
		if (node is JsonArray nodeArr)
			return nodeArr;
		return [node.SafeDeepClone()];
	}
	public static JsonArray Slice(this JsonArray array, System.Range range)
	{
		(int startIdx, int length) = range.GetOffsetAndLength(array.Count);
		JsonArray result = [];
		for (int i = startIdx; i < startIdx + length; i++)
		{
			result.Add(array[i].SafeDeepClone());
		}
		return result;
	}

	public static KeyValuePair<string, JsonNode> CreateKVP(this JsonObject from, string keyTerm)
	{
		return KeyValuePair.Create<string, JsonNode>(from[keyTerm]?.ToString() ?? from.ToString(), from.SafeDeepClone());
	}
}
