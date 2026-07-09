using Godot;
using System.Linq;
using System.Threading.Tasks;

public partial class DailySummaryWebhookDispatcher : Node
{
	DiscordWebhookProxy webhook;
	PublisherProxy publisher;
	[Export]
	SubViewportScreenshotter screenshotter;
	static DailySummaryWebhookDispatcher inst;
	public override void _Ready()
	{
		inst = this;
		//if (!DiscordWebhookProxy.TryGetProxy("dailySummary", out webhook))
		//	webhook = new("PegLeg Daily Summary", "dailySummary", contentProvider: Content, imageProvider: GenerateImage);
		publisher = PublisherProxy.GetOrCreatePublisher(new("dailySummary", "PegLeg Daily Summary"));
		RefreshTimerController.OnDayChanged += ExecuteWebhookDelayed;
	}

	static Task<string> Content()
	{
		//TODO: add option to ping roles based on presence of certain rewards
		return Task.FromResult(discordText);
	}

	public override void _ExitTree()
	{
		RefreshTimerController.OnDayChanged -= ExecuteWebhookDelayed;
	}

	const string standardText = "DAILY MISSIONS{vbucks}\n\nInstall PegLeg for more features.";
	const string discordText = "-# Install [PegLeg](<https://peglegfn.com/releases>) for more features.\n-# Follow the `daily-reset` channel in [Archers STW Dump](<https://peglegfn.com/archerdump>) to get these images in your own server.";

	async void ExecuteWebhookDelayed()
	{
		if (!publisher.IsEnabled)
			return;
		//if (webhook.UsesSync)
		//{
		//	//waits 9 seconds, abandons if missions arent fetched by then
		//	await Helpers.WaitForTimer(9);
		//}
		//else
		//{
		//}

		//waits for missions to be fetched, times out after 10 seconds
		await Task.WhenAny(
			GameMission.UpdateMissions(),
			Helpers.WaitForTimer(10)
		);
		if (AppConfig.TryGet("automation", "summary_160_fallback", out string _))
			await Helpers.WaitForTimer(6);
		await Helpers.WaitForFrames(3);

		if (GameMission.MissionList is null)
			return;
		await Publish();
	}

	public async void ForceExecuteWebhook()
	{
		var confirm = await GenericConfirmationWindow.ShowConfirmation("Publish Daily Summary?", warningText: "This will immediately publish onto all enabled platforms");
		if (confirm != true)
			return;
		if (GameMission.MissionList is null)
			await GameMission.UpdateMissions();
		await Helpers.WaitForFrames(3);
		await Publish();
	}

	async Task Publish()
	{
		var baseText = standardText;
		var vbuckCount = GameMission.MissionList.SelectMany(m => m.alertRewardItems ?? []).Where(i => i.template?.VBucksOrXRayTickets == true).Sum(i => i.quantity);
		baseText = baseText.Replace("{vbucks}", vbuckCount > 0 ? $": {vbuckCount} V-BUCKS/X-RAY TICKETS!" : "");

		//Image[] images = [await inst.screenshotter.CaptureScreenshot()];
		//Image[] opaqueImages;
		//if(AppConfig.Get("advanced", "share_bg", false))
		//	opaqueImages = images;
		//else
		//{
		//	AppConfig.Set("advanced", "share_bg", true);
		//	opaqueImages = [await inst.screenshotter.CaptureScreenshot()];
		//	AppConfig.Set("advanced", "share_bg", false);
		//}

		var (standard, opaque) = await screenshotter.CapturePublishingScreenshots();

		await publisher.AttemptPublish(platform => platform switch
		{
			"Discord" => new(discordText, images: standard),
			_ => new(baseText, images: opaque)
		});
	}

	static async Task<Image[]> GenerateImage()
	{
		if (inst is null)
			return [];
		var screenshot = await inst.screenshotter.CaptureScreenshot();
		return [screenshot];
	}
}
