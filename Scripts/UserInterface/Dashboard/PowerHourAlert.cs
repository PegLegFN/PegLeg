using Godot;
using System;
using System.Linq;
using System.Text.Json;

public partial class PowerHourAlert : Control
{
	[Export]
	string incomingText;
	[Export]
	string activeText;
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

	public override void _Ready()
	{
		Visible = false;
		GameCalender.OnCalenderUpdate += OnCalenderUpdate;
		OnCalenderUpdate();
	}

	private async void OnCalenderUpdate()
	{
		if (!GameCalender.HasCalender)
		{
			await GameCalender.Check();
			return;
		}
		var now = DateTime.UtcNow;
		var flagActiveOrIncoming = GameCalender.TryGetFlagRange("EventFlag.PreChrysus", out var start, out var end) && now < end;
		Visible = flagActiveOrIncoming;
		if (!flagActiveOrIncoming)
			return;
		var realStart = start.AddMinutes(30);
		bool active = realStart < now && now < end;
		header.Text = active ? activeText : incomingText;
		if (active)
			countdown.SetCustomRefreshTime(end, realStart);
		else
			countdown.SetCustomRefreshTime(realStart, start);

		var modifierFlags = GameCalender.EventFlagsWithPrefix("EventFlag.Chrysus");
		noModifiers.Visible = modifierFlags.Length == 0;
		if(modifierFlags.Length == 0)
		{
			var fallbackData = PegLegResourceManager.MagicNumbers["powerHourPrediction"]?.AsObject();
			if (modifierFlags.Length == 0 && fallbackData is not null && fallbackData["expires"]?.Deserialize<DateTime>() is DateTime expires && expires > DateTime.UtcNow)
			{
				var possibleModifiers = fallbackData["modifiers"].Deserialize<string[]>().Select(GameItemTemplate.Get).ToArray();
				SetModifiers(possibleModifiers);
				noModifiersLabel.Text = "Expected modifiers, may be innacuate:";
				return;
			}
			SetModifiers([]);
			noModifiersLabel.Text = "Modifiers unknown";
			return;
		}

		if (GameMission.MissionList is null)
			await GameMission.UpdateMissions();
		var modifiers = GameMission.MissionList.FirstOrDefault(m => m.TheaterCat != "v").theaterInfo.GetModifiers();
		var activeModifiers = modifierFlags.Select(f => modifiers.TryGetValue(f, out var m) ? m : null).Where(m => m is not null).ToArray();
		SetModifiers(activeModifiers);
	}

	void SetModifiers(GameItemTemplate[] activeModifiers)
	{
		modifierContainer.Visible = activeModifiers.Length > 0;
		if (!modifierContainer.Visible)
			return;
		for (int i = 0; i < Mathf.Min(activeModifiers.Length, modifierEntryList.Length); i++)
		{
			modifierEntryList[i].SetItem(activeModifiers[i].CreateInstance());
			modifierEntryList[i].Visible = true;
		}
		for (int i = activeModifiers.Length; i < modifierEntryList.Length; i++)
		{
			modifierEntryList[i].Visible = false;
		}
	}
}
