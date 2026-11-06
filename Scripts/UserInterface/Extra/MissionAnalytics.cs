using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

public partial class MissionAnalytics : Control
{
	[Export]
	LineEdit missionFilter;
	[Export]
	LineEdit itemFilter;
	[Export]
	CheckButton sampleItems;
	[Export]
	LineEdit fromDateText;
	[Export]
	LineEdit toDateText;
	[Export]
	PackedScene dataEntryScene;
	[Export]
	Control dataEntryParent;
	[Export]
	Label totalLabel;
	[Export]
	Label averageLabel;
	[Export]
	Button downloadMissing;
	[Export]
	Tree totalTree;

	List<MissionAnalyticsDataPoint> dataPointEntries = [];

	[GeneratedRegex("(\\d{4})-(\\d{1,2})-(\\d{1,2})")]
	private static partial Regex DateRegex();

	static readonly DateTime LocalResetTime = DateTime.UtcNow.Date.ToLocalTime();
	private static DateTime? ParseDate(string text)
	{
		var dateMatches = DateRegex().Match(text);
		if (!dateMatches.Success)
			return null;
		var local = new DateTime(
			int.Parse(dateMatches.Groups[1].Value),
			int.Parse(dateMatches.Groups[2].Value),
			int.Parse(dateMatches.Groups[3].Value),
			LocalResetTime.Hour,
			LocalResetTime.Minute,
			LocalResetTime.Second,
			DateTimeKind.Local
		);
		return local.ToUniversalTime().Date;
	}

	public override void _Ready()
	{
		totalLabel.Text = $"Total: ???";
		averageLabel.Text = $"Average: ???";
		missionFilter.TextChanged += _ => UpdateFilters();
		itemFilter.TextChanged += _ => UpdateFilters();
		sampleItems.Toggled += _ => UpdateAnalytics();
		Timeline.GetCurrentSeason(out var startDate);
		fromDateText.Text = $"{startDate:yyyy-M-d}";
		UpdateFilters();
	}

	DateTime fromDate;
	DateTime toDate;
	List<DateTime> missingDates = [];
	PLSearch.Instruction[] missionSearchInstructions = [];
	PLSearch.Instruction[] itemSearchInstructions = [];

	public async void UpdateTimeRange()
	{
		toDate = ParseDate(toDateText.Text) ?? DateTime.UtcNow.Date;
		fromDate = ParseDate(fromDateText.Text) ?? toDate.AddDays(-28);

		downloadMissing.Disabled = true;
		missingDates.Clear();
		int dayCount = ((int)(toDate - fromDate).TotalDays) + 1;
		using var loadToken = LoadingOverlay.CreateToken("Loading Archives", dayCount);
		for (int i = 0; i < dayCount; i++)
		{
			DateTime date = fromDate.AddDays(i);
			await Helpers.WaitForFrame();
			loadToken.SetLoadingProgress(i + 1);
			if (!GameMission.TryGetOrLoadArchive(date, out var archive))
			{
				downloadMissing.Disabled = false;
				missingDates.Add(date);
			}
		}
		UpdateAnalytics();
	}


	public async void PromptDownload()
	{
		if (missingDates.Count == 0)
			return;
		var prompt = await GenericConfirmationWindow.ShowConfirmation($"Download {missingDates.Count} Missing Archive{(missingDates.Count == 1 ? "" : "s")}?");
		if (prompt != true)
			return;

		using var download = LoadingOverlay.CreateToken();
		download.SetLoadingProgress(0, missingDates.Count);

		var indexResult = await ApiWebAddresses.lycanRocks.MakeRequest($"api/fortnite/stw/daily_archive/").Send();
		if (await indexResult.CheckForError())
			return;
		var indexJson = await indexResult.ReadJson<JsonArray>();
		var indexFiles = indexJson
			.Where(n => n["type"]?.ToString() == "file")//only files
			.Select(n => n["name"].ToString())//get filename
			.GroupBy(f => f[..10])//group by date
			.Select(g => g.OrderByDescending(f => int.TryParse(f.Split('_')[^1][..^5], out var order) ? order : 0).FirstOrDefault())//sort by suffix
			.ToDictionary(f => f[..10]);//map date to filename

		List<Task> downloadTasks = [];

		foreach (var date in missingDates)
		{
			GD.Print($"Checking for {date:yyyy-MM-dd} in index");
			GD.Print($"Template {indexFiles.Keys.FirstOrDefault()}");
			if (!indexFiles.TryGetValue($"{date:yyyy-MM-dd}", out var dateFile))
				continue;
			GD.Print($"Found {dateFile}");
			downloadTasks.Add(DownloadDate(dateFile));
			async Task DownloadDate(string dateFile)
			{
				var dateFileResult = await ApiWebAddresses.lycanRocks.MakeRequest($"api/fortnite/stw/daily_archive/{dateFile}").Send();
				if (await dateFileResult.CheckForError())
					return;
				var archive = await dateFileResult.ReadJson<GameMission.ArchiveData>();
				GD.Print($"Downloaded {dateFile}");
				GameMission.ImportArchive(archive);
			}
			download.SetLoadingProgress(download.progress+1);
		}

		await Task.WhenAll(downloadTasks);
		GD.Print($"Downloads complete");
		UpdateTimeRange();
	}

	public void UpdateFilters()
	{
		missionSearchInstructions = PLSearch.GenerateSearchInstructions(missionFilter.Text) ?? [];
		itemSearchInstructions = PLSearch.GenerateSearchInstructions(itemFilter.Text) ?? [];
		UpdateAnalytics();
	}

	bool MissionValidator(GameMission mission)
	{
		if (!PLSearch.EvaluateInstructions(missionSearchInstructions, mission.SearchObject))
			return false;
		if (sampleItems.ButtonPressed)
			return true;
		return mission.allItems.Any(ItemValidator);
	}
	//2025-9-4
	//2025-10-10

	bool ItemValidator(GameItem item) => PLSearch.EvaluateInstructions(itemSearchInstructions, item.RawData);

	record struct TotalMeta
	{
		public string type { get; init; }
		public string subtype { get; init; }
	}
	public void UpdateAnalytics()
	{
		if (fromDate.Year < 2025 || toDate.Year < 2025)
			return;
		if (fromDate > toDate)
			fromDate = toDate;
		int dayCount = ((int)(toDate - fromDate).TotalDays) + 1;
		Dictionary<DateTime, int> dataResults = [];

		Dictionary<string, long> templateTotals = [];
		Dictionary<string, TotalMeta> templateMetas = [];
		long allTotals = 0;

		for (int i = 0; i < dayCount; i++)
		{
			DateTime date = fromDate.AddDays(i);
			if (!GameMission.TryGetArchive(date, out var archive))
				continue;
			int value = -1;
			if (sampleItems.ButtonPressed)
			{
				var items = archive.Missions
					.Where(MissionValidator)
					.SelectMany(m => m.allItems)
					.Where(ItemValidator)
					.ToArray();
				value = items.Sum(i => i.quantity);
				allTotals += value;
				var groupedTypes = items.GroupBy(i =>
				{
					var name = i.template?.ItemName;
					if (name == "Survivor XP" || name == "Hero XP")
						name = "People XP";
					return name;
				});
				foreach (var typeGroup in groupedTypes)
				{
					if (typeGroup.Key is null)
						continue;
					templateTotals[typeGroup.Key] = templateTotals.GetValueOrDefault(typeGroup.Key) + typeGroup.Sum(i => i.quantity);
					if (templateMetas.ContainsKey(typeGroup.Key))
					{
						var template = typeGroup.FirstOrDefault().template;
						templateMetas[typeGroup.Key] = new()
						{
							type = template.Type,
							subtype = template.SubType
						};
					}
				}
			}
			else
			{
				var missions = archive.Missions.Where(MissionValidator).ToArray();
				value = missions.Length;
				allTotals += missions.Length;
				var groupedTypes = missions.GroupBy(m => m.missionGenerator.ItemName);
				foreach (var typeGroup in groupedTypes)
				{
					templateTotals[typeGroup.Key] = templateTotals.GetValueOrDefault(typeGroup.Key) + typeGroup.Count();
				}
			}
			dataResults.Add(date, value);
		}

		int maxValue = dataResults.Count > 0 ? dataResults.Values.Max() : 1;

		for (int i = 0; i < dayCount; i++)
		{
			if (dataPointEntries.Count <= i)
			{
				var newEntry = dataEntryScene.Instantiate<MissionAnalyticsDataPoint>();
				dataEntryParent.AddChild(newEntry);
				dataPointEntries.Add(newEntry);
			}
			var entry = dataPointEntries[i];
			entry.Visible = true;
			DateTime date = fromDate.AddDays(i);
			int value = dataResults.TryGetValue(date, out int val) ? val : -1;
			entry.SetData(date, value, maxValue);
		}

		for (int i = dayCount; i < dataPointEntries.Count; i++)
		{
			dataPointEntries[i].Visible = false;
		}

		int total = dataResults.Count > 0 ? dataResults.Values.Where(v => v >= 0).Sum() : 0;
		float average = total / Mathf.Max(dataResults.Count(kvp => kvp.Value >= 0), 1);

		var mostCommon = templateTotals.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key;

		totalLabel.Text = $"Total: {total}";
		averageLabel.Text = $"Average: {average:0.##}/day";
		if (mostCommon is not null && templateTotals.Count > 1)
			averageLabel.Text += $"   Most Common Type: {mostCommon}";

		totalTree.Visible = templateTotals.Count > 1;
		totalTree.Clear();
		if (!totalTree.Visible)
			return;
		totalTree.Columns = 2;
		totalTree.SetColumnExpand(1, false);
		//totalTree.SetColumnCustomMinimumWidth(1, 30);
		var root = totalTree.CreateItem();
		//Dictionary<string, TreeItem> treeItemTypes = [];
		//Dictionary<string, TreeItem> treeItemSubTypes = [];
		foreach (var item in templateTotals.OrderByDescending(kvp => kvp.Value))
		{
			TreeItem parent = root;
			//if (templateMetas.TryGetValue(item.Key, out var meta))
			//{
			//	if(!treeItemTypes.TryGetValue(meta.type, out var treeType))
			//	{
			//		treeType = root.CreateChild();
			//		treeType.SetText(0, meta.type);
			//		treeItemTypes[meta.type] = treeType;
			//	}
			//	parent = treeType;

			//	if (meta.subtype is string subtype)
			//	{
			//		if (!treeItemSubTypes.TryGetValue(subtype, out var treeSubType))
			//		{
			//			treeSubType = root.CreateChild();
			//			treeSubType.SetText(0, subtype);
			//			treeItemSubTypes[subtype] = treeSubType;
			//		}
			//		parent = treeSubType;
			//	}

			//}
			var treeItem = parent.CreateChild();
			treeItem.SetText(0, item.Key);
			treeItem.SetText(1, $" {item.Value.Compactify()} ");
			treeItem.SetTextOverrunBehavior(1, TextServer.OverrunBehavior.NoTrimming);
			treeItem.SetTooltipText(1, item.Value.Notate());
		}

		if (Bootstrap.IsEditor)
		{
			int count = 0;
			foreach (var item in templateTotals.OrderByDescending(kvp => kvp.Value))
			{
				count++;
				GD.Print($"{count}. {item.Key}: {item.Value}");
			}
		}
	}
}
