using Godot;

public partial class FetchCosmeticPreviewCtx : AbstractContextComponent
{
	public override string Id => "FetchCosmeticPreview";
	CosmeticShopOfferEntry currentCosmetic;
	public override void Update(ContextMenuHook hook)
	{
		currentCosmetic = hook?.cosmeticSource;
		SetDisabled(!Win64Helpers.isWindows);
	}

	public async void Copy()
	{
		if (currentCosmetic?.primaryTemplate is not string primaryTemplate)
			return;
		menu.CloseMenu();
		GD.Print(primaryTemplate);
		GD.Print(CosmoRequests.GetCosmoURL(primaryTemplate));
		Image img = null;
		using (var _ = LoadingOverlay.CreateToken())
		{
			img = await CosmoRequests.FetchCosmoImage(primaryTemplate);
		}
		if (img is not null)
			ShareImagePopup.ShowImage(img);
	}
}
