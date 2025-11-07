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
        if (webhook.UsesSync)
        {
            //waits 6 seconds, abandons if missions arent fetched by then
            await Helpers.WaitForTimer(6);
        }
        else
        {
            //waits for missions to be fetched, times out after 10 seconds
            await Task.WhenAny(
                GameMission.UpdateMissions(),
                Helpers.WaitForTimer(10)
            );
        }

        if (GameMission.currentMissions is null)
            return;
        await webhook.Execute();
    }

    public async void ForceExecuteWebhook()
    {
        if (GameMission.currentMissions is null)
            await GameMission.UpdateMissions();
        await Helpers.WaitForFrames(5);
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
