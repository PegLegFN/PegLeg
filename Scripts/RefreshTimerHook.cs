using Godot;
using System;

public partial class RefreshTimerHook : Control
{
	[Signal]
	public delegate void IsWarningEventHandler(bool visible);
	[Signal]
	public delegate void IsCriticalEventHandler(bool visible);
	[Export]
	Label target;
	[Export]
	Control tooltipTarget;
	[Export(PropertyHint.Enum, "Hour, Day, Week, BR Week, Season, Custom")]
	int timerType;
	[Export(PropertyHint.Enum, "Timer, SigShort, SigLong")]
	int formatType;
	[Export]
	int customWarningTime;
	[Export]
	int customCritTime;
	[Export]
	bool useColours = true;
	[Export]
	bool useWeeks;
	[Export]
	bool useDecimalDaysAndWeeks = true;
	[Export]
	string tooltipPrefix;
	[Export]
	ProgressBar progressBar;

	string CustomText
	{
		set => ((Control)target ?? this).Set("text", value);
	}
	string CustomTooltipText
	{
		set => (tooltipTarget ?? this).TooltipText = string.IsNullOrWhiteSpace(tooltipPrefix) ? value : $"{tooltipPrefix}\n{value}";
	}

	public override void _Ready()
	{
		CustomTooltipText = "";
		CustomText = "";
		UpdateRefreshTime();
		criticalCountdownTime = timerType switch
		{
			0 => 1,         // last minute of hour
			1 => 5,         // last 5 minutes of day
			2 => 60,        // last hour of week
			3 => 60,        // last hour of week
			4 => 60 * 24,   // last day of event
			5 => customCritTime,
			_ => 5,
		};
		warningCountdownTime = timerType switch
		{
			0 => 10,            // last 10 minutes of hour
			1 => 60,            // last hour of day
			2 => 60 * 24,       // last 24 hours of week
			3 => 60 * 24,       // last 24 hours of week
			4 => 60 * 24 * 7,   // last week of event
			5 => customWarningTime,
			_ => 60,
		};
		RefreshTimerController.OnDayChanged += UpdateRefreshTime;
		RefreshTimerController.OnSecondChanged += UpdateTimeText;
		UpdateTimeText();

		VisibilityChanged += UpdateTimeTextDelayed;
		if (target is null)
			MouseFilter = MouseFilterEnum.Stop;
	}

	public void SetTimerType(int timerType)
	{
		this.timerType = timerType;
		UpdateTimeText();
	}

	public void SetCustomRefreshTime(DateTime customRefreshTime, DateTime? customLastRefreshTime = null)
	{
		timerType = 5;
		refreshTime = customRefreshTime;
		lastRefreshTime = customLastRefreshTime;
		CustomTooltipText = refreshTime.ToLocalTime().ToString("g");
		warningCountdownTime = customWarningTime;
		criticalCountdownTime = customCritTime;
		UpdateTimeText();
	}

	DateTime refreshTime;
	DateTime? lastRefreshTime;
	int criticalCountdownTime = 1;
	int warningCountdownTime = 60;
	void UpdateRefreshTime()
	{
		if (timerType == 5)
		{
			CustomTooltipText = refreshTime.ToLocalTime().ToString("g");
			return;
		}
		var type = timerType switch
		{
			0 => RefreshTimeType.Hourly,
			1 => RefreshTimeType.Daily,
			2 => RefreshTimeType.Weekly,
			3 => RefreshTimeType.BRWeekly,
			4 => RefreshTimeType.Event,
			_ => RefreshTimeType.Daily,
		};
		refreshTime = RefreshTimerController.GetRefreshTime(type);
		lastRefreshTime = RefreshTimerController.GetLastRefreshTime(type);
		CustomTooltipText = refreshTime.ToLocalTime().ToString("g");
	}

	async void UpdateTimeTextDelayed()
	{
		await Helpers.WaitForFrame();
		UpdateTimeText(true);
	}

	void UpdateTimeText() => UpdateTimeText(false);
	void UpdateTimeText(bool force)
	{
		if (!force && !IsVisibleInTree())
			return;
		var remainingTime = refreshTime - RefreshTimerController.RightNow;
		if (useColours)
		{
			var colorTarget = (Control)target ?? this;
			if (remainingTime.TotalMinutes < criticalCountdownTime)
				colorTarget.SelfModulate = Colors.Red;
			else if (remainingTime.TotalMinutes < warningCountdownTime)
				colorTarget.SelfModulate = Colors.Orange;
			else
				colorTarget.SelfModulate = Colors.White;
		}
		EmitSignalIsWarning(remainingTime.TotalMinutes < warningCountdownTime);
		EmitSignalIsCritical(remainingTime.TotalMinutes < criticalCountdownTime);
		CustomText = remainingTime.FormatTime(formatType switch
		{
			2 => Helpers.TimeFormat.SigLong,
			1 => Helpers.TimeFormat.SigShort,
			_ => Helpers.TimeFormat.Full,
		}, useWeeks, useDecimalDaysAndWeeks);
		progressBar?.Value = ProgressBarValue();
		if (DateTime.UtcNow.CompareTo(refreshTime) >= 0)
			UpdateRefreshTime();
	}

	double ProgressBarValue()
	{
		if (lastRefreshTime is not DateTime realLastRefresh)
			return 0;
		var duration = (refreshTime - realLastRefresh).TotalDays;
		var progress = (RefreshTimerController.RightNow - realLastRefresh).TotalDays;
		return progress / duration;
	}

	public override void _ExitTree()
	{
		RefreshTimerController.OnDayChanged -= UpdateRefreshTime;
		RefreshTimerController.OnSecondChanged -= UpdateTimeText;
	}
}

