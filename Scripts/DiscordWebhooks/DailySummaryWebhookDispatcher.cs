using Godot;
using System;
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
		if(!DiscordWebhookProxy.TryGetProxy("dailySummary", out webhook))
			webhook = new("PegLeg Daily Summary", "dailySummary", GenerateImage);
		RefreshTimerController.OnDayChanged += ExecuteWebhookDelayed;
	}

    public override void _ExitTree()
    {
        RefreshTimerController.OnDayChanged -= ExecuteWebhookDelayed;
    }

    async void ExecuteWebhookDelayed()
    {
        await Helpers.WaitForTimer(9);
		ExecuteWebhook();
    }

    async void ExecuteWebhook()
    {
		if (GameMission.currentMissions is null)
			return;
		await webhook.Execute();
    }

    static async Task<Image[]> GenerateImage()
	{
		if (inst is null)
			return [];
		var screenshot = await inst.screenshotter.CaptureScreenshot();
		return [screenshot];
	}
}
