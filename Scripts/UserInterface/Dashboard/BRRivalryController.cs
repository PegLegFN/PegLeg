using Godot;
using System.Text.Json;
using System.Text.Json.Serialization;

public partial class BRRivalryController : Control
{
	[Export]
	Control rootControl;
	[Export]
	Control loadingIcon;

	[Export]
	Color foundationColor;
	[Export]
	Control foundationParent;
	[Export]
	Range foundationProgress;
	[Export]
	Label foundationValue;

	[Export]
	Color iceKingColor;
	[Export]
	Control iceKingParent;
	[Export]
	Range iceKingProgress;
	[Export]
	Label iceKingValue;

	public override void _Ready()
	{
		RefreshTimerController.OnHourChanged += UpdateProgress;
		UpdateProgress();
	}

	public override void _ExitTree()
	{
		RefreshTimerController.OnHourChanged -= UpdateProgress;
	}

	async void UpdateProgress()
	{
		rootControl.Visible = false;
		loadingIcon.Visible = true;

		var eventResponse = await WebHelpers.MakeRequest("https://mesh-public-service-live.ol.epicgames.com/mesh/Fortnite/Fortnite.Hera_Terrain.51188288/metadata").Send();
		if (await eventResponse.CheckForError())
		{
			loadingIcon.Visible = false;
			return;
		}
		var eventJson = await eventResponse.ReadJson();
		var eventData = eventJson.Deserialize<EventData>(Helpers.JsonOptions.Fields);

		rootControl.Visible = true;
		loadingIcon.Visible = false;

		foundationValue.Text = eventData.Foundation.Current.Compactify();
		foundationProgress.Value = eventData.Foundation.Progress;
		foundationParent.TooltipText = CustomTooltip.GenerateSimpleTooltip(
			"Team Foundation: Dual Victories",
			eventData.Foundation.Current.Notate(),
			bannerCol: foundationColor.ToHtml()
		);


		iceKingValue.Text = eventData.IceKing.Current.Compactify();
		iceKingProgress.Value = eventData.IceKing.Progress;
		iceKingParent.TooltipText = CustomTooltip.GenerateSimpleTooltip(
			"Team Ice King: Dual Victories",
			eventData.IceKing.Current.Notate(),
			bannerCol: iceKingColor.ToHtml()
		);
	}

	struct EventData
	{
		[JsonPropertyName("MeshNetworkedEvent.Clash.FactionA")]
		public MeshnetData Foundation;
		[JsonPropertyName("MeshNetworkedEvent.Clash.FactionB")]
		public MeshnetData IceKing;
	}

	struct MeshnetData
	{
		public MeshnetRequirements metadataStructData;
		public long Current => metadataStructData.currentValue;
		public float Progress => metadataStructData.currentValue / (float)metadataStructData.requiredValue;
	}

	struct MeshnetRequirements
	{
		public long requiredValue;
		public long currentValue;
	}
}
