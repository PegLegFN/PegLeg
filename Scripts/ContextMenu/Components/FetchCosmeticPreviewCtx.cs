using Godot;

public partial class FetchCosmeticPreviewCtx : AbstractContextComponent
{
	public override string Id => "FetchCosmeticPreview";
	CosmeticShopOfferEntry currentCosmetic;
	public override void Update(ContextMenuHook hook)
	{
		currentCosmetic = hook?.cosmeticSource;
		SetDisabled(!Win64Helpers.isWindows || currentCosmetic?.primaryTemplate is null);
	}

	public async void Copy()
	{
		if (currentCosmetic?.primaryTemplate is not string primaryTemplate)
			return;
		menu.CloseMenu();
		var cosmoImgData = CosmoRequests.GetItemPreview(primaryTemplate);
		GD.Print(primaryTemplate);
		GD.Print(cosmoImgData.url);
		Image img = null;
		using (var _ = LoadingOverlay.CreateToken())
		{
			await cosmoImgData.FetchImage();
			img = cosmoImgData.ReadLocalImageDirect();
		}
		if (img is not null)
			ShareImagePopup.ShowImage(img);
	}
}
