using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using TimeZoneNames;

public partial class PowerHourWebhookDispatcher : Node
{
	//DiscordWebhookProxy webhook;
	static PublisherProxy publisher = PublisherProxy.GetOrCreatePublisher(new("powerHour", "PegLeg Power Hour Alert"));
	[Export]
	SubViewportScreenshotter screenshotter;
	TriggerInstance forcePublishHeadsupTrigger = new("forcePublishPowerHourHeadsup", true);
	TriggerInstance forcePublishStartTrigger = new("forcePublishPowerHourStart", true);
	TriggerInstance forcePublishFirstEndTrigger = new("forcePublishPowerHourFirstEnd", true);
	TriggerInstance forcePublishSecondEndTrigger = new("forcePublishPowerHourSecondEnd", true);

	public override void _Ready()
	{
		PowerHourScheduleTracker.CurrentOrNextEventChanged += TryExecute;
		GameMission.OnMissionsUpdated += AttemptHeadsup;

		//todo: read dispatch state from file (might be unnececary)
		currentDispatchEnd = PowerHourScheduleTracker.CurrentOrNextEvent.end;

		forcePublishHeadsupTrigger.BindNode(this, ForceExecuteHeadsupCtx);
		forcePublishStartTrigger.BindNode(this, ForceExecuteStarted);
		forcePublishFirstEndTrigger.BindNode(this, ForceExecuteFirstEnd);
		forcePublishSecondEndTrigger.BindNode(this, ForceExecuteSecondEnd);
		UpdateEnabled();
		publisher.EnabledChanged += UpdateEnabled;


		Node ancestor = GetParent();
		while (ancestor is not null)
		{
			if (ancestor.IsInGroup("ShareTab"))
			{
				ancestor.SetMeta("publishTriggers", new Godot.Collections.Dictionary<string, string>()
				{
					["Heads-Up"] = "forcePublishPowerHourHeadsup",
					["Start"] = "forcePublishPowerHourStart",
					["First End"] = "forcePublishPowerHourFirstEnd",
					["Second End"] = "forcePublishPowerHourSecondEnd"
				});
				break;
			}
			ancestor = ancestor.GetParent();
		}
	}

	void UpdateEnabled()
	{
		forcePublishHeadsupTrigger.Enabled = publisher.IsEnabled;
		forcePublishStartTrigger.Enabled = publisher.IsEnabled;
		forcePublishFirstEndTrigger.Enabled = publisher.IsEnabled;
		forcePublishSecondEndTrigger.Enabled = publisher.IsEnabled;
	}

	public override void _ExitTree()
	{
		PowerHourScheduleTracker.CurrentOrNextEventChanged -= TryExecute;
		GameMission.OnMissionsUpdated -= AttemptHeadsup;
		publisher.EnabledChanged -= UpdateEnabled;
	}

	const string headsup = "A Power Hour is scheduled to occur {timestamp}";
	const string powerHourStart = "A Power Hour has started! It's expected to end {timestamp}";
	const string firstEnd = "The Power Hour has ended, but another one should be active {timestamp}.\n(Ongoing missions will keep the modifiers until they end)";
	const string secondEnd = "The Power Hour has ended.\n(Ongoing missions will keep the modifiers until they end)";

	const string discordSuffix = "\n-# Try out [PegLeg](<https://peglegfn.com/releases>)";

	DateTime currentDispatchEnd;
	bool hasDispatchedEventStart;
	bool hasDispatchedEventHeadsup;

	private async void TryExecute()
	{
		if (!publisher.IsEnabled)
			return;
		var curEvt = PowerHourScheduleTracker.CurrentOrNextEvent;
		if (curEvt.confirmation == PowerHourScheduleTracker.ConfirmationState.InProgress)
			return;

		var now = DateTime.UtcNow;

		if (currentDispatchEnd != curEvt.end) //last event target is different to current event, last event may have recently ended
		{
			hasDispatchedEventStart = false;
			hasDispatchedEventHeadsup = false;
			if (Mathf.Abs((now - currentDispatchEnd).TotalMinutes) > 3)
			{
				currentDispatchEnd = curEvt.end;
				return;
			}
			currentDispatchEnd = curEvt.end;

			//dispatch that the previous event has ended
			//if we are within 24hrs of the next event, treat this as a heads up dispatch as well
			if (now > curEvt.start.AddHours(-24))
			{
				//event ended + headsup for next event
				hasDispatchedEventHeadsup = true;
				await Publish(firstEnd, curEvt.start, isStartStamp: true);
			}
			else
			{
				//event ended
				await Publish(secondEnd, now, noImage: true);
			}
		}
		else
		{
			currentDispatchEnd = curEvt.end;
			if (now < curEvt.start || hasDispatchedEventStart)
				return;
			//event started
			await Publish(powerHourStart, curEvt.end);
			hasDispatchedEventStart = true;
		}
	}

	static async Task<bool> ShowConfirmation(string[] ctx, string type)
	{
		if (ctx is not null && ctx.Length > 0 && ctx[0] == "force")
			return true;
		var confirm = await GenericConfirmationWindow.ShowConfirmation($"Publish Power Hour {type}?", warningText: "This will immediately publish onto all enabled platforms");
		return confirm == true;
	}

	public void ForceExecuteHeadsup() => ForceExecuteHeadsupCtx([]);
	public async void ForceExecuteHeadsupCtx(string[] ctx)
	{
		if (!await ShowConfirmation(ctx, "Headsup"))
			return;
		var curEvt = PowerHourScheduleTracker.CurrentOrNextEvent;
		await Publish(headsup, curEvt.start, isStartStamp: true);
	}

	public async void ForceExecuteStarted(string[] ctx)
	{
		if (!await ShowConfirmation(ctx, "Start Alert"))
			return;
		var curEvt = PowerHourScheduleTracker.CurrentOrNextEvent;
		await Publish(powerHourStart, curEvt.end);
	}

	public async void ForceExecuteFirstEnd(string[] ctx)
	{
		if (!await ShowConfirmation(ctx, "End Alert with Headsup"))
			return;
		var curEvt = PowerHourScheduleTracker.CurrentOrNextEvent;
		await Publish(firstEnd, curEvt.start, isStartStamp: true);
	}

	public async void ForceExecuteSecondEnd(string[] ctx)
	{
		if (!await ShowConfirmation(ctx, "End Alert"))
			return;
		var curEvt = PowerHourScheduleTracker.CurrentOrNextEvent;
		await Publish(secondEnd, curEvt.start, noImage: true);
	}

	public async void AttemptHeadsup()
	{
		var now = DateTime.UtcNow;
		if (hasDispatchedEventHeadsup) //heads up has already been sent
			return;
		if ((now - GameMission.missionReset.AddHours(-24)).TotalSeconds > 60) //its been more than 60 seconds since reset
			return;

		var curEvt = PowerHourScheduleTracker.CurrentOrNextEvent;

		//only if event hasnt started, but is less than 24 hours away
		if (curEvt.start > now && curEvt.start.AddHours(-24) < now)
		{
			await Publish(headsup, curEvt.start, isStartStamp: true);
			hasDispatchedEventHeadsup = true;
		}
	}

	static TimeZoneInfo displayTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
	static TimeZoneValues displayTimeZoneShorthand = TZNames.GetAbbreviationsForTimeZone(displayTimeZone.Id, "en-US");
	//static string[] timezones = [.. TimeZoneInfo.GetSystemTimeZones().Select(i => i.Id)];
	async Task Publish(string template, DateTime timestamp, bool noImage = false, bool isStartStamp = false)
	{
		var endStamp = timestamp.AddHours(2);
		var utcTime = timestamp.ToUniversalTime();
		var utcEnd = utcTime.AddHours(2);
		var displayTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, displayTimeZone);
		var endTime = displayTime.AddHours(2);
		var displayZone = displayTimeZone.IsDaylightSavingTime(displayTime) ? displayTimeZoneShorthand.Standard : displayTimeZoneShorthand.Daylight;

		//using promises ensures screenshots are only captured when needed
		var transparant = screenshotter.GetPromise();
		var opaque = screenshotter.GetPromise(true);

		await publisher.AttemptPublish(async platform => platform switch
		{
			"Discord" => new(
				template.Replace("{timestamp}", isStartStamp ? 
					$"{timestamp.Discordify()} ({timestamp.Discordify(Helpers.DiscordTimeFormat.ShortTime)}-({endStamp.Discordify(Helpers.DiscordTimeFormat.ShortTime)}))" : 
					$"{timestamp.Discordify()} ({timestamp.Discordify(Helpers.DiscordTimeFormat.ShortTime)})"
				) + discordSuffix, 
				images: [noImage ? null : await transparant.GetOrCapture()]
			),
			_ => new(
				template.Replace("{timestamp}", isStartStamp ? 
				$"from {displayTime:h:mmtt}-{endTime:h:mmtt} {displayZone} ({utcTime:H:mm}-{utcEnd:H:mm} UTC)" : 
				$"at {displayTime:h:mmtt} {displayZone} ({utcTime:H:mm} UTC)"), 
				images: [noImage ? null : await opaque.GetOrCapture()]
			)
		});
	}
}
