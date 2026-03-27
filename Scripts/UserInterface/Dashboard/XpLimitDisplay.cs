using Godot;
using System;

public partial class XpLimitDisplay : Control
{
	[Export]
	Label progressText;
	[Export]
	ProgressBar progressBar;
	[Export]
	Label progressPercent;
	[Export]
	RefreshTimerHook refreshTime;

	static DateTime earliest = new(2017, 1, 1);
	public void SetXpProgress(int current, int max, DateTime? nextRefresh)
	{
		if (nextRefresh is not null && nextRefresh.Value < earliest)
			nextRefresh = null;
		bool uncapped = max <= 0;
		if (uncapped)
			max = 1;
		progressText.Text = uncapped ? current.Notate() : $"{current.Notate()}/{max.Notate()}";
		float scale = (float)current / max;
		progressBar.Value = uncapped ? 1 : scale;
		progressPercent.Text = uncapped ? "Uncapped" : $"{Mathf.RoundToInt(scale * 100)}%";
		refreshTime.Visible = nextRefresh is not null;
		if (refreshTime.Visible)
			refreshTime.SetCustomRefreshTime(nextRefresh ?? default(DateTime).AddDays(1), nextRefresh?.AddDays(-7) ?? default);
	}
}
