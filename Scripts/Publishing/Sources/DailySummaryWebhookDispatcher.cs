using Godot;
using System.Threading.Tasks;

public partial class DailySummaryWebhookDispatcher : Node
{
	DiscordWebhookProxy webhook;
	[Export]
	SubViewportScreenshotter screenshotter;
	static DailySummaryWebhookDispatcher inst;
	public override void _Ready()
	{
		inst = this;
		if (!DiscordWebhookProxy.TryGetProxy("dailySummary", out webhook))
			webhook = new("PegLeg Daily Summary", "dailySummary", contentProvider: Content, imageProvider: GenerateImage);
		RefreshTimerController.OnDayChanged += ExecuteWebhookDelayed;
	}

	static Task<string> Content()
	{
		//TODO: add option to ping roles based on presence of certain rewards
		return Task.FromResult("-# Get [PegLeg](<https://peglegfn.com/releases>) for customisation and notifications.\n-# Follow the `daily-reset` channel in [Archers STW Dump](<https://peglegfn.com/archerdump>) to get these images in your own server.");
	}

	public override void _ExitTree()
	{
		RefreshTimerController.OnDayChanged -= ExecuteWebhookDelayed;
	}

	async void ExecuteWebhookDelayed()
	{
		if (!webhook.IsEnabled)
			return;
		if (webhook.UsesSync)
		{
			//waits 9 seconds, abandons if missions arent fetched by then
			await Helpers.WaitForTimer(9);
		}
		else
		{
			//waits for missions to be fetched, times out after 10 seconds
			await Task.WhenAny(
				GameMission.UpdateMissions(),
				Helpers.WaitForTimer(10)
			);
			if (AppConfig.TryGet("automation", "summary_160_fallback", out string _))
				await Helpers.WaitForTimer(6);
			await Helpers.WaitForFrames(3);
		}

		if (GameMission.MissionList is null)
			return;
		await webhook.Execute();
	}

	public async void ForceExecuteWebhook()
	{
		if (GameMission.MissionList is null)
			await GameMission.UpdateMissions();
		await Helpers.WaitForFrames(3);
		await webhook.Execute(true);
	}

	static async Task<Image[]> GenerateImage()
	{
		if (inst is null)
			return [];
		var screenshot = await inst.screenshotter.CaptureScreenshot();
		return [screenshot];
	}
}
