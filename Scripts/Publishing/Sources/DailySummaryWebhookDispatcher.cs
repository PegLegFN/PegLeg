using Godot;
using System.Linq;
using System.Threading.Tasks;

public partial class DailySummaryWebhookDispatcher : Node
{
	static PublisherProxy publisher = PublisherProxy.GetOrCreatePublisher(new("dailySummary", "PegLeg Daily Summary"));
	[Export]
	SubViewportScreenshotter screenshotter;
	TriggerInstance forcePublishTrigger = new("forceShareDailySummary", true);

	public override void _Ready()
	{
		//if (!DiscordWebhookProxy.TryGetProxy("dailySummary", out webhook))
		//	webhook = new("PegLeg Daily Summary", "dailySummary", contentProvider: Content, imageProvider: GenerateImage);
		RefreshTimerController.OnDayChanged += ExecuteWebhookDelayed;
		forcePublishTrigger.BindNode(this, ForcePublishWebhook);
		UpdateEnabled();
		publisher.EnabledChanged += UpdateEnabled;

		Node ancestor = GetParent();
		while (ancestor is not null)
		{
			if (ancestor.IsInGroup("ShareTab"))
			{
				ancestor.SetMeta("publishTriggers", new Godot.Collections.Dictionary<string, string>()
				{
					["Summary"] = "forceShareDailySummary"
				});
				break;
			}
			ancestor = ancestor.GetParent();
		}
	}

	void UpdateEnabled() => forcePublishTrigger.Enabled = publisher.IsEnabled;

	public override void _ExitTree()
	{
		RefreshTimerController.OnDayChanged -= ExecuteWebhookDelayed;
		publisher.EnabledChanged -= UpdateEnabled;
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

		//waits for missions to be fetched, times out after 30 seconds
		var timeout = AppConfig.Get("missions", "summary_timeout", 30);
		await Task.WhenAny(
			GameMission.UpdateMissions(),
			Helpers.WaitForTimer(timeout)
		);

		if (GameMission.MissionList is null)
		{
			GD.Print($"Daily Summary Publisher timed out ({timeout} seconds)");
			return;
		}

		if (AppConfig.TryGet("automation", "summary_160_fallback", out string _))
			await Helpers.WaitForTimer(6);
		await Helpers.WaitForFrames(3);

		await Publish();
	}

	public async void ForceExecuteWebhook() => ForcePublishWebhook([]);
	public async void ForcePublishWebhook(string[] ctx)
	{
		if (!(ctx is not null && ctx.Length > 0 && ctx[0] == "force"))
		{
			var confirm = await GenericConfirmationWindow.ShowConfirmation("Publish Daily Summary?", warningText: "This will immediately publish onto all enabled platforms");
			if (confirm != true)
				return;
		}
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

		//using promises ensures screenshots are only captured when needed
		var transparant = screenshotter.GetPromise();
		var opaque = screenshotter.GetPromise(true);

		await publisher.AttemptPublish(async platform => platform switch
		{
			"Discord" => new(discordText, images: [await transparant.GetOrCapture()]),
			_ => new(baseText, images: [await opaque.GetOrCapture()])
		});
	}
}
