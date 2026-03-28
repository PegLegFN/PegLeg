using Godot;
using System;
using System.Linq;

public partial class AverageVbuckCaclulator : Control
{
	[Export]
	Label weekAverage;
	[Export]
	Label monthAverage;
	[Export]
	Label seasonAverage;

	string weekText;
	string monthText;
	string seasonText;

	public override void _Ready()
	{
		weekText = weekAverage.Text.FixNewlines();
		monthText = monthAverage.Text.FixNewlines();
		seasonText = seasonAverage.Text.FixNewlines();
		GameMission.OnMissionsUpdated += UpdateVbucks;

		Visible = false;

		if (!AppConfig.Get("advanced", "archive_missions", false))
			return;

		Visible = true;

		var today = DateTime.UtcNow.Date;
		var seasonStart = RefreshTimerController.GetLastRefreshTime(RefreshTimeType.Event);
		var monthAgo = today.AddDays(-30);
		var startDate = seasonStart < monthAgo ? seasonStart : monthAgo;
		int days = (int)(today - startDate).TotalDays;
		for (int i = 0; i <= days; i++)
		{
			var date = startDate.AddDays(i);
			GameMission.TryGetOrLoadArchive(date, out _);
		}
		UpdateVbucks();
	}

	public override void _ExitTree()
	{
		GameMission.OnMissionsUpdated -= UpdateVbucks;
	}

	private void UpdateVbucks()
	{
		if (!AppConfig.Get("advanced", "archive_missions", false))
			return;

		var today = DateTime.UtcNow.Date;
		weekAverage.Text = weekText.Replace("{x}", CalcAverage(today.AddDays(-6)).ToString());
		monthAverage.Text = monthText.Replace("{x}", CalcAverage(today.AddDays(-29)).ToString());
		seasonAverage.Text = seasonText.Replace("{x}", CalcAverage(RefreshTimerController.GetLastRefreshTime(RefreshTimeType.Event)).ToString());
	}

	int CalcAverage(DateTime startDate)
	{
		int days = (int)(DateTime.UtcNow.Date - startDate).TotalDays + 1;
		int totalDays = days;
		int vBucks = 0;
		for (int i = 0; i < days; i++)
		{
			var date = startDate.AddDays(i);
			if (GameMission.TryGetOrLoadArchive(date, out var archive))
				vBucks += archive.Missions
					.SelectMany(m => m.alertRewardItems)
					.Where(item => item.template.VBucksOrXRayTickets)
					.Select(i => i.quantity)
					.Sum();
			else
				totalDays--;
		}
		if (totalDays == 0)
			return 0;
		return vBucks / totalDays;
	}
}
