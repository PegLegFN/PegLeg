using Godot;
using System;
using System.Threading.Tasks;

public partial class PowerHourWebhookDispatcher : Node
{
	static PowerHourWebhookDispatcher inst;
	DiscordWebhookProxy webhook;
	[Export]
	SubViewportScreenshotter screenshotter;

	public override void _Ready()
	{
		inst = this;

		if (!DiscordWebhookProxy.TryGetProxy("powerHour", out webhook))
			webhook = new("PegLeg Power Hour Alert", "powerHour", imageProvider: GenerateImage);

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

	DateTime currentDispatchEnd;
	bool hasDispatchedEventStart;
	bool hasDispatchedEventHeadsup;

	private async void TryExecute()
	{
		if (!webhook.IsEnabled)
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
				await webhook.Execute(currentContentProvider: async () => $"The Power Hour has ended, but another one should be active {curEvt.start.Discordify()}.\n(Ongoing missions will keep the modifiers until they end)\n-# Try out [PegLeg](<https://peglegfn.com/releases>)");
				hasDispatchedEventHeadsup = true;
			}
			else
			{
				//event ended
				await webhook.Execute(currentContentProvider: async () => "The Power Hour has ended. (Ongoing missions will keep the modifiers until they end)", currentImageProvider: async () => []);
			}
		}
		else
		{
			currentDispatchEnd = curEvt.end;
			if (now < curEvt.start || hasDispatchedEventStart)
				return;
			//event started
			await webhook.Execute(currentContentProvider: async () => $"A Power Hour has started! It's expected to end {curEvt.end.Discordify()}.\n-# Try out [PegLeg](<https://peglegfn.com/releases>)");
			hasDispatchedEventStart = true;
		}
	}

	public async void ForceExecuteHeadsup()
	{
		var curEvt = PowerHourScheduleTracker.CurrentOrNextEvent;
		await inst.webhook.Execute(true, currentContentProvider: async () => $"A Power Hour is scheduled to occur {curEvt.start.Discordify()}.\n-# Try out [PegLeg](<https://peglegfn.com/releases>)");
	}

	public static async void AttemptHeadsup()
	{
		var now = DateTime.UtcNow;
		if (inst is null)
			return;
		if (inst.hasDispatchedEventHeadsup) //heads up has already been sent
			return;
		if ((now - GameMission.missionReset.AddHours(-24)).TotalSeconds > 60) //its been more than 60 seconds since reset
			return;

		var curEvt = PowerHourScheduleTracker.CurrentOrNextEvent;

		//only if event hasnt started, but is less than 24 hours away
		if (curEvt.start > now && curEvt.start.AddHours(-24) < now)
		{
			await inst.webhook.Execute(currentContentProvider: async () => $"A Power Hour is scheduled to occur {curEvt.start.Discordify()}.\n-# Try out [PegLeg](<https://peglegfn.com/releases>)");
			inst.hasDispatchedEventHeadsup = true;
		}
	}


	static async Task<Image[]> GenerateImage()
	{
		if (inst is null)
			return [];
		var screenshot = await inst.screenshotter.CaptureScreenshot();
		return [screenshot];
	}
}
