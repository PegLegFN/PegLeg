using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class PowerHourScheduleTracker : Node
{
	public static PowerHourScheduleTracker Instance { get; private set; }
	public static event Action CurrentOrNextEventChanged;

	public record struct PowerHourSchedule()
	{
		public DateTime anchor { get; init; } = DateTime.Parse("2026-06-13T00:00:00.000Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
		public int[] timeslotHours { get; init; } = [16, 24];
		public int durationHours { get; init; } = 2;
		public int preChrysusOffset { get; init; } = 30;
		public string[] alwaysModifiers { get; init; } = ["GameplayModifier:GM_Chrysus_EnemyMod"];
		public string[][] weeklyModifiers { get; init; } = [
			["GameplayModifier:GM_Phoenix_SuperHeroic"],
			["GameplayModifier:GM_Phoenix_SuperConstructor"],
			["GameplayModifier:GM_Phoenix_SuperOutlander"],
			["GameplayModifier:GM_Phoenix_SuperNinja"]
		];
	}

	public enum ConfirmationState
	{
		InProgress,
		Unconfirmed,
		OnlyTimeConfirmed,
		AllConfirmed
	}

	public readonly record struct PowerHourEvent(ConfirmationState confirmation, DateTime start, DateTime end, GameItemTemplate[] modifiers)
	{
		public bool Valid => start != default;
	}

	PowerHourSchedule schedule = new();

	public static PowerHourEvent CurrentOrNextEvent { get; private set; }

	public override async void _Ready()
	{
		Instance = this;
		await PegLegResourceManager.AwaitResourceLoad();
		if (PegLegResourceManager.MagicNumbers["powerHourSchedule"] is JsonNode scheduleNode)
			schedule = scheduleNode.Deserialize<PowerHourSchedule>();
		RefreshTimerController.OnMinuteChanged += TryCheckSchedule;
		TryCheckSchedule();
	}

	const int calendarOffset = 115;//slightly under 2 hours just in case
	DateTime nextCheck = DateTime.MinValue;

	private async void TryCheckSchedule()
	{
		var now = DateTime.UtcNow;
		if (now < nextCheck)
			return;

		GD.Print("Checking Power Hour...");

		int targetWeek = Mathf.FloorToInt((now - schedule.anchor).TotalDays / 7);
		DateTime weekStart = schedule.anchor.AddDays(7 * targetWeek);

		//target the first hour of the week that hasnt ended yet
		int targetOffset = schedule.timeslotHours.FirstOrDefault(i => weekStart.AddHours(i + schedule.durationHours) > now, -1);
		if (targetOffset < 0)
		{
			//if all hours of the week have ended, target the first hour of the following week
			targetWeek += 1;
			weekStart = weekStart.AddDays(7);
			targetOffset = schedule.timeslotHours[0];
		}

		DateTime targetPowerHourStart = weekStart.AddHours(targetOffset);
		DateTime targetPowerHourEnd = targetPowerHourStart.AddHours(schedule.durationHours);
		DateTime validationStart = targetPowerHourStart.AddMinutes(-(calendarOffset + schedule.preChrysusOffset));

		int weekIndex = targetWeek % schedule.weeklyModifiers.Length;
		string[] modifiers = [.. schedule.alwaysModifiers, .. schedule.weeklyModifiers[weekIndex]];
		GameItemTemplate[] modifierTemplates = [.. modifiers.Select(GameItemTemplate.Get)];

		//validate with calendar and missions
		if (GameAccount.ActiveAccount.isOwned && validationStart < now)
		{
			UpdateEventAndNextCheck(
				new(
					ConfirmationState.InProgress,
					targetPowerHourStart,
					targetPowerHourEnd,
					modifierTemplates
				),
				validationStart
			);
			var confirmedEvent = await TryConfirmEvent();
			if (confirmedEvent?.confirmation == ConfirmationState.OnlyTimeConfirmed)
				confirmedEvent = confirmedEvent.Value with { modifiers = modifierTemplates };
			UpdateEventAndNextCheck(
				confirmedEvent ?? new(
					ConfirmationState.Unconfirmed,
					targetPowerHourStart,
					targetPowerHourEnd,
					modifierTemplates
				),
				validationStart
			);
		}
		else
		{
			UpdateEventAndNextCheck(
				new(
					ConfirmationState.Unconfirmed,
					targetPowerHourStart,
					targetPowerHourEnd,
					modifierTemplates
				),
				validationStart
			);
		}
	}

	async Task<PowerHourEvent?> TryConfirmEvent()
	{
		await GameCalender.Check();
		if (!GameCalender.HasCalender)
			return null;

		var flagActiveOrIncoming = GameCalender.TryGetFlagRange("EventFlag.PreChrysus", out var start, out var end) && DateTime.UtcNow < end;
		if (!flagActiveOrIncoming)
			return null;

		var realStart = start.AddMinutes(schedule.preChrysusOffset);

		var modifierFlags = GameCalender.EventFlagsWithPrefix("EventFlag.Chrysus");
		if (modifierFlags.Length == 0)
			return new(
				 ConfirmationState.OnlyTimeConfirmed,
				 realStart,
				 realStart.AddHours(schedule.durationHours),
				 null
			);

		await GameMission.CheckMissions();
		if (GameMission.MissionList.Length == 0)
			return new(
				 ConfirmationState.OnlyTimeConfirmed,
				 realStart,
				 realStart.AddHours(schedule.durationHours),
				 null
			);

		var modifierDict = GameMission.MissionList.FirstOrDefault(m => m.TheaterCat != "v").theaterInfo.GetModifiers();
		return new(
			ConfirmationState.AllConfirmed,
			realStart,
			realStart.AddHours(schedule.durationHours),
			[.. modifierFlags.Select(f => modifierDict.TryGetValue(f, out var m) ? m : null).Where(m => m is not null)]
		);
	}

	void UpdateEventAndNextCheck(PowerHourEvent newEvent, DateTime validationStart)
	{
		CurrentOrNextEvent = newEvent;
		CurrentOrNextEventChanged?.Invoke();

		var prevNextCheck = nextCheck;
		var now = DateTime.UtcNow;
		if (newEvent.start < now) //power hour has started
			nextCheck = newEvent.end;
		else if (GameAccount.ActiveAccount.isOwned && validationStart > now) //validation is possible, and validation is upcoming
			nextCheck = validationStart;
		else // we must be after validation (or validation is impossible), but before the start of power hour
			nextCheck = newEvent.start;

		if (prevNextCheck != nextCheck)
			GD.Print("Next Power Hour Check will occur at " + nextCheck);
	}
}
