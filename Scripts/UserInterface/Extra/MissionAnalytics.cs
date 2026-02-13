using Godot;
using Polly.Caching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

    List<MissionAnalyticsDataPoint> dataPointEntries = [];

    [GeneratedRegex("(\\d{4})-(\\d{1,2})-(\\d{1,2})")]
    private static partial Regex DateRegex();

    private static DateTime? ParseDate(string text)
    {
        var dateMatches = DateRegex().Match(text);
        if(!dateMatches.Success)
            return null;
        return new DateTime(
            int.Parse(dateMatches.Groups[1].Value),
            int.Parse(dateMatches.Groups[2].Value),
            int.Parse(dateMatches.Groups[3].Value),
            0,
            0,
            0,
            DateTimeKind.Local
        ).ToUniversalTime().Date;
    }

    public override void _Ready()
    {
        totalLabel.Text = $"Total: ???";
        averageLabel.Text = $"Average: ???";
        missionFilter.TextChanged += _ => UpdateFilters();
        itemFilter.TextChanged += _ => UpdateFilters();
        sampleItems.Toggled += _ => UpdateAnalytics();
        UpdateFilters();
    }

    DateTime fromDate;
    DateTime toDate;
    PLSearch.Instruction[] missionSearchInstructions = [];
    PLSearch.Instruction[] itemSearchInstructions = [];

    public async void UpdateTimeRange()
    {
        toDate = ParseDate(toDateText.Text) ?? DateTime.UtcNow.Date;
        fromDate = ParseDate(fromDateText.Text) ?? toDate.AddDays(-28);

        int dayCount = (int)((toDate - fromDate).TotalDays + 1);
        using var loadToken = LoadingOverlay.CreateToken("Loading Archives", dayCount);
        for (int i = 0; i < dayCount; i++)
        {
            DateTime date = fromDate.AddDays(i);
            await Helpers.WaitForFrame();
            loadToken.SetLoadingProgress(i + 1);
            if (!GameMission.TryGetOrLoadArchive(date, out var archive))
            {
                GD.Print($"archive missing for {date}");
                //TODO: add to list of unavailable archives, prompt to download from archive server
            }
        }
        UpdateAnalytics();
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

    public void UpdateAnalytics()
    {
        if (fromDate.Year < 2000 || fromDate.Year < 2000)
            return;
        if (fromDate > toDate)
            fromDate = toDate;
        int dayCount = (int)((toDate - fromDate).TotalDays + 1);
        Dictionary<DateTime, int> dataResults = [];

        for (int i = 0; i < dayCount; i++)
        {
            DateTime date = fromDate.AddDays(i);
            if (!GameMission.TryGetArchive(date, out var archive))
                continue;
            int value = -1;
            if (sampleItems.ButtonPressed)
            {
                value = archive.Missions
                    .Where(MissionValidator)
                    .SelectMany(m=>m.allItems)
                    .Where(ItemValidator)
                    .Select(i=>i.quantity)
                    .Sum();
            }
            else
            {
                value = archive.Missions.Count(MissionValidator);
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

        totalLabel.Text = $"Total: {total}";
        averageLabel.Text = $"Average: {average:0.##}/day";
    }
}
