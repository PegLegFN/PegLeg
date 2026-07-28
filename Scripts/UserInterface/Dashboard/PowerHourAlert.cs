using Godot;
using System;
using System.Linq;

public partial class PowerHourAlert : Control
{
	[Export]
	string incomingText;
	[Export]
	string activeText;
	[Export]
	bool useHeadsUpForVisibility = true;
	[Export]
	Label header;
	[Export]
	RefreshTimerHook countdown;
	[Export]
	Control modifierContainer;
	[Export]
	GameItemEntry[] modifierEntryList;
	[Export]
	Control noModifiers;
	[Export]
	Label noModifiersLabel;
	[Export]
	Control anecdoteParent;
	[Export]
	Control secondaryVisibility;
	[Export]
	Label timeText;

	Control[] anecdotes = [];
	public override void _Ready()
	{
		timeText.Text = "";
		anecdotes = [.. anecdoteParent.GetChildren().OfType<Control>()];
		if (useHeadsUpForVisibility)
		{
			Visible = false;
			secondaryVisibility?.Visible = Visible;
			RefreshTimerController.OnMinuteChanged += CheckForHeadsUp;
		}
		PowerHourScheduleTracker.CurrentOrNextEventChanged += SetDetails;
		SetDetails();
	}

	public override void _ExitTree()
	{
		if (useHeadsUpForVisibility)
			RefreshTimerController.OnMinuteChanged -= CheckForHeadsUp;
		PowerHourScheduleTracker.CurrentOrNextEventChanged -= SetDetails;
	}

	private int HeadsUpHours =>
		Mathf.Max(AppConfig.Get("missions", "powerhour_headsup", 24), 0);
		//24 * 7;

	private void CheckForHeadsUp()
	{
		var phEvent = PowerHourScheduleTracker.CurrentOrNextEvent;
		var now = DateTime.UtcNow;
		int headsUpHours = HeadsUpHours;
		Visible = phEvent.Valid && phEvent.start.AddHours(-headsUpHours) < now;
		secondaryVisibility?.Visible = Visible;
	}

	private void SetDetails()
	{
		var phEvent = PowerHourScheduleTracker.CurrentOrNextEvent;
		var now = DateTime.UtcNow;
		int headsUpHours = HeadsUpHours;
		if (useHeadsUpForVisibility)
		{
			Visible = phEvent.Valid && phEvent.start.AddHours(-headsUpHours) < now;
			secondaryVisibility?.Visible = Visible;
		}

		bool isActive = phEvent.start < now;
		timeText.Text = isActive ? "Ends in" : "Starts in";
		header.Text = isActive ? activeText : incomingText;
		if (isActive)
			countdown.SetCustomRefreshTime(phEvent.end, phEvent.start);
		else
			countdown.SetCustomRefreshTime(phEvent.start, phEvent.start.AddHours(-headsUpHours));

		noModifiersLabel.Text = phEvent.confirmation switch
		{
			PowerHourScheduleTracker.ConfirmationState.InProgress => "(Confirming...)",
			PowerHourScheduleTracker.ConfirmationState.OnlyTimeConfirmed => "(Modifiers Unconfirmed)",
			PowerHourScheduleTracker.ConfirmationState.AllConfirmed => "",
			_ => "(Unconfirmed)",
		};

		var modifiers = phEvent.modifiers ?? [];
		modifierContainer.Visible = modifiers.Length > 0;
		if (!modifierContainer.Visible)
			return;
		for (int i = 0; i < Mathf.Min(modifiers.Length, modifierEntryList.Length); i++)
		{
			modifierEntryList[i].SetItem(modifiers[i].CreateInstance());
			modifierEntryList[i].Visible = true;
		}
		for (int i = modifiers.Length; i < modifierEntryList.Length; i++)
		{
			modifierEntryList[i].Visible = false;
		}

		var modifierNames = modifiers.Select(t => t.Name.ToLower()).ToHashSet();
		foreach (var item in anecdotes)
		{
			item.Visible = modifierNames.Contains(item.GetMeta("requirement").AsString()?.ToLower());
		}
	}

}
