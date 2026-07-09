using Godot;
using System;
using System.Threading.Tasks;
using TimeZoneNames;

public partial class PowerHourWebhookDispatcher : Node
{
	//DiscordWebhookProxy webhook;
	PublisherProxy publisher;
	[Export]
	SubViewportScreenshotter screenshotter;

	public override void _Ready()
	{
		//if (!DiscordWebhookProxy.TryGetProxy("powerHour", out webhook))
		//	webhook = new("PegLeg Power Hour Alert", "powerHour", imageProvider: GenerateImage);
		publisher = PublisherProxy.GetOrCreatePublisher(new("powerHour", "PegLeg Power Hour Alert"));

		PowerHourScheduleTracker.CurrentOrNextEventChanged += TryExecute;
		GameMission.OnMissionsUpdated += AttemptHeadsup;

		//todo: read dispatch state from file (might be unnececary)
		currentDispatchEnd = PowerHourScheduleTracker.CurrentOrNextEvent.end;
	}

	public override void _ExitTree()
	{
		PowerHourScheduleTracker.CurrentOrNextEventChanged -= TryExecute;
		GameMission.OnMissionsUpdated -= AttemptHeadsup;
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
				//await webhook.Execute(currentContentProvider: async () => $"The Power Hour has ended, but another one should be active {curEvt.start.Discordify()}.\n(Ongoing missions will keep the modifiers until they end)\n-# Try out [PegLeg](<https://peglegfn.com/releases>)");
				hasDispatchedEventHeadsup = true;
				await Publish(firstEnd, curEvt.end);
			}
			else
			{
				//event ended
				//await webhook.Execute(currentContentProvider: async () => "The Power Hour has ended. (Ongoing missions will keep the modifiers until they end)", currentImageProvider: async () => []);
				await Publish(secondEnd, now, noImage: true);
			}
		}
		else
		{
			currentDispatchEnd = curEvt.end;
			if (now < curEvt.start || hasDispatchedEventStart)
				return;
			//event started
			//await webhook.Execute(currentContentProvider: async () => $"A Power Hour has started! It's expected to end {curEvt.end.Discordify()}.\n-# Try out [PegLeg](<https://peglegfn.com/releases>)");
			await Publish(powerHourStart, curEvt.end);
			hasDispatchedEventStart = true;
		}
	}

	public async void ForceExecuteHeadsup()
	{
		var curEvt = PowerHourScheduleTracker.CurrentOrNextEvent;
		//await inst.webhook.Execute(true, currentContentProvider: async () => $"A Power Hour is scheduled to occur {curEvt.start.Discordify()}.\n-# Try out [PegLeg](<https://peglegfn.com/releases>)");
		await Publish(headsup, curEvt.start);
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
			//await inst.webhook.Execute(currentContentProvider: async () => $"A Power Hour is scheduled to occur {curEvt.start.Discordify()}.\n-# Try out [PegLeg](<https://peglegfn.com/releases>)");
			await Publish(headsup, curEvt.start);
			hasDispatchedEventHeadsup = true;
		}
	}

	static TimeZoneInfo displayTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time\\Dynamic DST");
	static TimeZoneValues displayTimeZoneShorthand = TZNames.GetAbbreviationsForTimeZone(displayTimeZone.Id, "en-GB");
	async Task Publish(string template, DateTime timestamp, bool noImage = false)
	{
		var utcTime = timestamp.ToUniversalTime();
		var displayTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, displayTimeZone);
		var displayZone = displayTimeZone.IsDaylightSavingTime(displayTime) ? displayTimeZoneShorthand.Standard : displayTimeZoneShorthand.Daylight;

		var (standard, opaque) = noImage ? ([], []) : await screenshotter.CapturePublishingScreenshots();

		await publisher.AttemptPublish(platform => platform switch
		{
			"Discord" => new(template.Replace("{timestamp}", timestamp.Discordify()) + discordSuffix, images: standard),
			_ => new(template.Replace("{timestamp}", $"at {displayTime:H:mm} {displayZone} ({utcTime:H:mm} UTC)"), images: opaque)
		});
	}


	//static async Task<Image[]> GenerateImage()
	//{
	//	if (inst is null)
	//		return [];
	//	var screenshot = await inst.screenshotter.CaptureScreenshot();
	//	return [screenshot];
	//}
}
