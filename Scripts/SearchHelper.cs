using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public static partial class PLSearch
{
	const string termExpression =
@"
(?>
 (?>
  (?<=\s|\A)+
  (?>[\s(]|\!\()*
 )
 (?:
  (?<Prefix>\!)?
  (?>
   (?<Union>(?<!\!)\|)|
   (?<TextQuery>
    (?<TQVar>[a-zA-Z_]+(?:\.(?:[a-zA-Z_]+|(?:\^?\d+)))*)=
    (?<TQVal>[\""'][\w:\-_.!?\/\\ ]*[\""'])
   )|
   (?<NumericQuery>
    (?<NQVar>[a-zA-Z_]+(?:\.(?:[a-zA-Z_]+|(?:\^?\d+)))*)=
    (?<NQVal>
       (?:\d+(?:\.\d+)?\.\.\d+(?:\.\d+)?)|
       (?:             \.\.\d+(?:\.\d+)?)|
       (?:                 \d+(?:\.\d+)?(?:\.\.)?)
    )
   )|
   (?<FullSearchQuery>[\""'][\w:\-_.!?\/\\ ]+[\""'])|
   (?<SearchQuery>[\w:\-_.!?\/\\]+)
  )
 )
 (?>[\s)]+|\Z)
)|
(?<Failure>[^\s\b]+)
";

	[GeneratedRegex(termExpression, RegexOptions.IgnorePatternWhitespace)]
	private static partial Regex SearchTermParserRegex();

	[GeneratedRegex(@"^(?:[^()]|(?<Push>\()|(?<Pop-Push>\)))*(?(Push)(?!))$", RegexOptions.IgnorePatternWhitespace)]
	private static partial Regex BracketRegex();

	public enum InstructionOperation
	{
		Push,
		Pop,
		Union,
		NumericQuery,
		TextQuery,
		FullSearchQuery,
		SearchQuery
	}
	public record Instruction(int index, InstructionOperation operation, JsonNode meta = null, int endIndex = -1, bool inverted = false);

	public static JsonObject CreateSearchObject(string[] searchTags) => new JsonObject() { ["searchTags"] = new JsonArray([.. searchTags]) };
	public static Instruction[] GenerateSearchInstructions(string fromText) =>
		GenerateSearchInstructions(fromText, out var _);
	public static Instruction[] GenerateSearchInstructions(string fromText, out string failureText)
	{
		List<Instruction> instructions = [];
		failureText = "";

		var match = BracketRegex().Match(fromText);
		if (!match.Success)
		{
			failureText = "Bracket Pattern Failure";
			return null;
		}
		var captures = match.Groups.Values.SelectMany(g => g.Captures);
		foreach (var capture in captures)
		{
			if (capture.Index == 0)
				continue;
			bool inverted = capture.Index > 1 && fromText[capture.Index - 2] == '!';
			instructions.Add(new(capture.Index - 1, InstructionOperation.Push, endIndex: capture.Index + capture.Length + 1));
			instructions.Add(new(capture.Index + capture.Length, InstructionOperation.Pop, inverted: inverted));
		}

		string partialFailText = null;
		EvaluateRegexMatches(fromText, match =>
		{
			var validGroups = match.Groups.Values.Where(g => g.Name != "0" && !string.IsNullOrEmpty(g.Value)).ToArray();
			var firstGroup = validGroups[0];

			bool isInverted = false;
			if (firstGroup.Name == "Prefix")
			{
				if (firstGroup.Value == "!")
					isInverted = true;
				firstGroup = validGroups[1];
			}


			//GD.Print($"Checking \"{match.Value}\" ({firstGroup.Name}:{firstGroup.Value})");
			JsonObject meta;
			switch (firstGroup.Name)
			{
				case "TextQuery":
					if (instructions.Any(i => i.index == firstGroup.Index))
						return;
					meta = new()
					{
						["varPath"] = match.Groups["TQVar"].Value,
					};
					string checks = match.Groups["TQVal"].Value;
					if (checks.StartsWith("["))
						meta["checks"] = new JsonArray(checks[1..^2].Split(',').Select(c => (JsonNode)c.Trim()).ToArray());
					else
						meta["checks"] = new JsonArray() { checks };

					instructions.Add(new(
						firstGroup.Index,
						InstructionOperation.TextQuery,
						meta,
						firstGroup.Index + firstGroup.Length - 1,
						isInverted
					));

					return;
				case "NumericQuery":
					if (instructions.Any(i => i.index == firstGroup.Index))
						return;
					var valText = match.Groups["NQVal"].Value;
					meta = new()
					{
						["varPath"] = match.Groups["NQVar"].Value,
					};
					if (valText.StartsWith(".."))
					{
						meta["maxValue"] = JsonNode.Parse(valText[2..]);
					}
					else if (valText.EndsWith(".."))
					{
						meta["minValue"] = JsonNode.Parse(valText[..^2]);
					}
					else if (valText.Contains(".."))
					{
						var rangeText = valText.Split("..");
						meta["minValue"] = JsonNode.Parse(rangeText[0]);
						meta["maxValue"] = JsonNode.Parse(rangeText[1]);

						if (float.Parse(rangeText[0]) > float.Parse(rangeText[1]))
						{
							partialFailText ??= "";
							partialFailText += $"Parsing failure at line {firstGroup.Index}: \"{firstGroup.Value}\"\n";
							return;
						}
					}
					else
					{
						meta["minValue"] = JsonNode.Parse(valText);
						meta["maxValue"] = JsonNode.Parse(valText);
					}

					instructions.Add(new(
						firstGroup.Index,
						InstructionOperation.NumericQuery,
						meta,
						firstGroup.Index + firstGroup.Length - 1,
						isInverted
					));
					return;
				case "FullSearchQuery":
					if (instructions.Any(i => i.index == firstGroup.Index))
						return;
					instructions.Add(new(
						firstGroup.Index,
						InstructionOperation.FullSearchQuery,
						firstGroup.Value,
						firstGroup.Index + firstGroup.Length - 1,
						isInverted
					));
					return;
				case "SearchQuery":
					if (instructions.Any(i => i.index == firstGroup.Index))
						return;
					instructions.Add(new(
						firstGroup.Index,
						InstructionOperation.SearchQuery,
						firstGroup.Value,
						firstGroup.Index + firstGroup.Length - 1,
						isInverted
					));
					return;
				case "Union":
					instructions.Add(new(firstGroup.Index, InstructionOperation.Union));
					return;
				case "Failure":
					partialFailText ??= "";
					partialFailText += $"Parsing failure at line {firstGroup.Index}: \"{firstGroup.Value}\"\n";
					return;
				default:
					return;
			}
		});

		if (partialFailText is not null)
		{
			failureText = partialFailText;
			return null;
		}
		return instructions.ToArray();
	}

	public static bool EvaluateInstructions(Instruction[] instructions, string[] searchTags) =>
		EvaluateInstructions(instructions, CreateSearchObject(searchTags), out var _, false);
	public static bool EvaluateInstructions(Instruction[] instructions, JsonObject target) =>
		EvaluateInstructions(instructions, target, out var _, false);

	struct SearchState
	{
		public bool overallResult = true;
		public bool operationResult = true;
		public bool union = false;

		public SearchState() { }
	}

	public static bool EvaluateInstructions(Instruction[] instructions, JsonObject target, out string output, bool useOutput = true)
	{
		output = "";
		if (instructions is null)
			return true;
		int currentDepth = 0;
		int skipUntilIndex = 0;
		SearchState currentState = new();
		Stack<SearchState> stateStack = new();
		foreach (var item in instructions.OrderBy(i => i.index))
		{
			if (!currentState.union && item.operation != InstructionOperation.Union)
			{
				currentState.overallResult &= currentState.operationResult;
			}

			bool skipThisOp = item.index < skipUntilIndex;
			if (!currentState.overallResult && item.operation != InstructionOperation.Pop)
			{
				skipThisOp = true;
			}
			if (currentState.union && currentState.operationResult)
			{
				skipThisOp = true;
			}

			string toInsert = "";
			if (item.operation == InstructionOperation.Push)
			{
				toInsert += Indent(currentDepth) + "(";
				currentDepth += 1;
				if (!skipThisOp)
				{
					stateStack.Push(currentState);
					currentState = new();
				}
				else
				{
					skipUntilIndex = Mathf.Max(item.endIndex, skipUntilIndex);
				}
				output += toInsert + (skipThisOp ? " (Skipped)" : "") + "\n";
				continue;
			}
			if (item.operation == InstructionOperation.Pop)
			{
				currentDepth -= 1;
				toInsert += ")";

				if (!skipThisOp)
				{
					bool resolvedResult = currentState.overallResult;
					toInsert += (item.inverted ? " NOT " : " ") + (resolvedResult ? "PASS" : "FAIL");
					if (item.inverted)
						resolvedResult = !resolvedResult;
					currentState = stateStack.Pop();
					if (currentState.union)
					{
						currentState.union = false;
						currentState.operationResult |= resolvedResult;
					}
					else
						currentState.operationResult = resolvedResult;
				}

				output += Indent(currentDepth) + toInsert + (skipThisOp ? " (Skipped)" : "") + "\n";
				continue;
			}


			if (!useOutput && skipThisOp)
			{
				currentState.union = false;
				continue;
			}

			toInsert += item.inverted ? "NOT " : "";
			bool currentResult = false;
			JsonObject metaObj;
			switch (item.operation)
			{
				case InstructionOperation.Union:
					if (!skipThisOp)
						currentState.union = true;
					toInsert += " OR";
					currentResult = currentState.operationResult;
					break;
				case InstructionOperation.TextQuery:
					{
						if (!skipThisOp)
							skipUntilIndex = item.endIndex;
						metaObj = item.meta.AsObject();
						string varPath = metaObj["varPath"].ToString();
						if (EvaluateJsonPath(target, varPath) is not JsonNode sourceNode)
						{
							toInsert += "T ERROR";
							if (!skipThisOp)
								currentResult = false;
							break;
						}
						string[] checks = metaObj["checks"].AsArray().Select(n => n.ToString()).ToArray();
						bool comparisonTrue = false;
						if (sourceNode is JsonArray sourceArray)
						{
							foreach (var sourceStringVal in sourceArray)
							{
								if ((string)sourceStringVal is string sourceString)
								{
									comparisonTrue |= MulticheckString(sourceString, checks);
								}
								if (comparisonTrue)
									break;
							}
						}
						else if (sourceNode is JsonValue sourceVal)
						{
							//GD.Print(sourceVal.ToString());
							if (sourceVal.TryGetValue(out bool sourceBool)) //todo: add proper bool check
							{
								comparisonTrue = sourceBool;
							}
							else if (sourceVal.TryGetValue(out string sourceString))
								comparisonTrue = MulticheckString(sourceString, checks);
						}
						if (item.inverted)
							comparisonTrue = !comparisonTrue;
						if (!skipThisOp)
							currentResult = comparisonTrue;
						toInsert += comparisonTrue ? "PASS" : "FAIL";
					}
					break;
				case InstructionOperation.NumericQuery:
					{
						if (!skipThisOp)
							skipUntilIndex = item.endIndex;
						metaObj = item.meta.AsObject();
						string varPath = metaObj["varPath"].ToString();
						var endNode = EvaluateJsonPath(target, varPath);
						if (ExtractJsonNumber(endNode) is not float sourceVar)
						{
							toInsert += "N ERROR";
							if (!skipThisOp)
								currentResult = false;
							break;
						}
						bool comparisonTrue = true;
						if (ExtractJsonNumber(metaObj["minValue"]) is float minValue)
						{
							comparisonTrue &= sourceVar >= minValue;
						}
						if (ExtractJsonNumber(metaObj["maxValue"]) is float maxValue)
						{
							comparisonTrue &= sourceVar <= maxValue;
						}
						if (item.inverted)
							comparisonTrue = !comparisonTrue;
						if (!skipThisOp)
							currentResult = comparisonTrue;
						toInsert += comparisonTrue ? "PASS" : "FAIL";
					}
					break;
				case InstructionOperation.FullSearchQuery:
					{
						if (!skipThisOp)
							skipUntilIndex = item.endIndex;
						bool comparisonTrue = false;
						if (target["searchTags"] is JsonArray tagArray)
						{
							string checkString = item.meta.ToString();
							comparisonTrue = tagArray.Any(t => CheckString(ParseTag(t?.ToString().ToLower()), checkString.ToLower()));
						}
						if (item.inverted)
							comparisonTrue = !comparisonTrue;
						if (!skipThisOp)
							currentResult = comparisonTrue;
						toInsert += comparisonTrue ? "PASS" : "FAIL";
					}
					break;
				case InstructionOperation.SearchQuery:
					{
						if (!skipThisOp)
							skipUntilIndex = item.endIndex;
						bool comparisonTrue = false;
						string checkString = item.meta.ToString();
						string uuid = target["uuid"]?.GetValue<string>();
						if (uuid == checkString)
						{
							comparisonTrue = true;
						}
						else if (checkString == "NEW" && target["attributes"] is JsonObject itemAttributes)
						{
							//special query that checks an items isSeen property
							comparisonTrue = !(itemAttributes["item_seen"]?.GetValue<bool>() ?? false);
						}
						else if (checkString == "REMINDER" && GameAccount.ActiveAccount.HasReminder(target["templateId"]?.ToString()))
						{
							comparisonTrue = true;
						}
						else if (target["searchTags"] is JsonArray tagArray)
						{
							comparisonTrue = tagArray.Any(t => ParseTag(t?.ToString().ToLower())?.Contains(checkString, StringComparison.CurrentCultureIgnoreCase) ?? false);
						}
						if (item.inverted)
							comparisonTrue = !comparisonTrue;
						if (!skipThisOp)
							currentResult = comparisonTrue;
						toInsert += comparisonTrue ? "PASS" : "FAIL";
					}
					break;
			}
			if (currentState.union && item.operation != InstructionOperation.Union)
			{
				currentState.union = false;
				currentResult |= currentState.operationResult;
			}
			if (!skipThisOp)
				currentState.operationResult = currentResult;
			output += Indent(currentDepth) + toInsert + (skipThisOp ? " (Skipped)" : "") + "\n";
		}
		currentState.overallResult &= currentState.operationResult;
		output += "final result: " + currentState.overallResult;
		return currentState.overallResult;
	}

	static string ParseTag(string tag)
	{
		if (tag is null)
			return null;
		if (tag.StartsWith("hidetag_") && tag.Length > 8)
			return tag[8..];
		return tag;
	}

	static string Indent(int level)
	{
		string indentText = "";
		for (int i = 0; i < level; i++)
			indentText += "   ";
		return indentText;
	}

	static float? ExtractJsonNumber(JsonNode possibleVal)
	{
		if (possibleVal is not JsonValue numberValue)
			return null;
		if (numberValue.TryGetValue(out float floatValue))
			return floatValue;
		if (numberValue.TryGetValue(out int intValue))
			return intValue;
		if (numberValue.TryGetValue(out bool boolValue))
			return boolValue ? 1 : 0;
		return null;
	}

	static JsonNode EvaluateJsonPath(JsonNode parent, string path) =>
		EvaluateJsonPath(parent, new Queue<string>(path.Split(".")));
	static JsonNode EvaluateJsonPath(JsonNode parent, Queue<string> pathQueue)
	{
		if (pathQueue.Count == 0)
			return parent;
		string nextKey = pathQueue.Dequeue();
		if (parent is JsonObject)
		{
			if (parent[nextKey] is JsonNode nextNode)
				return EvaluateJsonPath(nextNode, pathQueue);
		}
		else if (parent is JsonArray parentArray)
		{
			bool fromEnd = nextKey.StartsWith("^");
			if (fromEnd)
				nextKey = nextKey[1..];
			if (int.TryParse(nextKey, out int nextIndex))
			{
				if (fromEnd)
				{
					if (nextIndex > 0 && parentArray.Count >= nextIndex && parentArray[^nextIndex] is JsonNode nextNode)
						EvaluateJsonPath(nextNode, pathQueue);
				}
				else if (nextIndex >= 0 && parentArray.Count > nextIndex && parentArray[nextIndex] is JsonNode nextNode)
					EvaluateJsonPath(nextNode, pathQueue);
			}
		}
		return null;
	}

	static bool MulticheckString(string sourceString, string[] checks) => checks.Any(c => CheckString(sourceString, c));
	static bool CheckString(string sourceString, string check)
	{
		if (check.Length <= 2)
			return false;
		bool ends = check.StartsWith('\'');
		bool starts = check.EndsWith('\'');
		string content = check[1..^1];

		if (ends && starts)
			return sourceString.Contains(content);
		else if (ends)
			return sourceString.EndsWith(content);
		else if (starts)
			return sourceString.StartsWith(content);
		else
			return sourceString == content;
	}

	static void EvaluateRegexMatches(string sourceText, Action<Match> doPerMatch)
	{
		var matches = SearchTermParserRegex().Matches(sourceText);
		foreach (var item in matches.Cast<Match>())
		{
			doPerMatch?.Invoke(item);
		}
	}
}
