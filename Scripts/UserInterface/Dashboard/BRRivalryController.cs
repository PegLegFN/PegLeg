using Godot;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

public partial class BRRivalryController : Control
{
	[Export]
	Control rootControl;
	[Export]
	Control loadingIcon;
	[Export]
	Label currentTeamLabel;
	[Export]
	Control switchButton;

	[Export]
	Color foundationColor;
	[Export]
	Control foundationParent;
	[Export]
	Godot.Range foundationProgress;
	[Export]
	Label foundationValue;

	[Export]
	Color iceKingColor;
	[Export]
	Control iceKingParent;
	[Export]
	Godot.Range iceKingProgress;
	[Export]
	Label iceKingValue;

	[Export]
	Label leaderText;

	string rivalryURL;

	public override void _Ready()
	{
		rivalryURL = "https://mesh-public-service-live.ol.epicgames.com/" +
			(
				PegLegResourceManager.MagicNumbers["rivalryURLPath"]?.ToString() ??
				"mesh/Fortnite/Fortnite.Hera_Terrain.51618937/metadata"
			);
		GameAccount.ActiveAccountChanged += ActiveAccountChanged;
		RefreshTimerController.OnMinuteChanged += UpdateProgress;
		UpdateProgress();
		ActiveAccountChanged();
		if (PegLegResourceManager.MagicNumbers.ContainsKey("hideRivalrySelector"))
			switchButton.Visible = false;
	}

	public override void _ExitTree()
	{
		GameAccount.ActiveAccountChanged -= ActiveAccountChanged;
		RefreshTimerController.OnMinuteChanged -= UpdateProgress;
	}

	private async void ActiveAccountChanged()
	{
		if (!GameAccount.ActiveAccount.isOwned)
			return;
		var athena = await GameAccount.ActiveAccount.GetProfile(FnProfileTypes.CosmeticInventory).Query();
		var token = athena.GetFirstTemplateItem("Token:athena_ch7s2_factionatoken");
		token ??= athena.GetFirstTemplateItem("Token:athena_ch7s2_factionbtoken");

		var text = token?.templateId switch
		{
			"Token:athena_ch7s2_factionatoken" => "Current: Team Foundation",
			"Token:athena_ch7s2_factionbtoken" => "Current: Team Ice King",
			_ => "Current: Neutral"
		};

		currentTeamLabel.Text = text;
	}

	private async void TryChangeTeam()
	{
		if (!GameAccount.ActiveAccount.isOwned)
			return;
		var choice = await GenericConfirmationWindow.ShowConfirmation("Choose your Alliegence", " Ice King ", " Foundation ", "Any Rivalry Victories earned in BR will contribute to your selected team.", headerSpace:30, highlightConfirm: false);
		if (choice is null)
			return;
		var athena = GameAccount.ActiveAccount.GetProfile(FnProfileTypes.CosmeticInventory);
		await athena.PerformOperation("SetFactionChoice", $$"""{"factionTokenTemplateId":"{{(choice.Value ? "Token:athena_ch7s2_factionbtoken" : "Token:athena_ch7s2_factionatoken")}}"}""");
		ActiveAccountChanged();
	}

	bool hasData = false;

	static SemaphoreSlim semaphore = new(1);
	async void UpdateProgress()
	{
		using var _ = await semaphore.AwaitToken();

		if (!hasData)
		{
			rootControl.Visible = false;
			loadingIcon.Visible = true;
		}

		var eventResponse = await WebHelpers.MakeRequest(rivalryURL).Send();
		if (await eventResponse.CheckForError())
		{
			loadingIcon.Visible = false;
			return;
		}
		var eventJson = await eventResponse.ReadJson();
		var eventData = eventJson.Deserialize<EventData>(Helpers.JsonOptions.Fields);

		hasData = true;
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

		var diff = Math.Abs(eventData.Foundation.Current - eventData.IceKing.Current);
		bool foundationLead = eventData.Foundation.Current > eventData.IceKing.Current;
		var leadingTeam = foundationLead ? "Team Foundation" : "Team Ice King";
		var leadingCol = foundationLead ? foundationColor : iceKingColor;

		leaderText.Text = $"{leadingTeam} are leading by {diff.Compactify()} rivalry wins";
		leaderText.TooltipText = CustomTooltip.GenerateSimpleTooltip(
			$"{leadingTeam}: Leader",
			diff.Notate(),
			bannerCol: leadingCol.ToHtml()
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
