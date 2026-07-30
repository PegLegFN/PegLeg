
using Godot;

public partial class CopyCosmeticIconCtx : AbstractContextComponent
{
	public override string Id => "CopyCosmeticIcon";
	CosmeticShopOfferEntry currentCosmetic;
	CosmeticOfferEntryNew currentCosmeticNew;
	public override void Update(ContextMenuHook hook)
	{
		currentCosmetic = hook?.cosmeticSource;
		currentCosmeticNew = hook?.newCosmeticSource;
		SetDisabled((currentCosmetic?.imageUrl is null && currentCosmeticNew?.currentOffer is null) || !Win64Helpers.isWindows);
	}

	public void Copy()
	{
		Image cosmeticImage = null;
		if (currentCosmetic is not null)
		{
			cosmeticImage = Image.LoadFromFile(CatalogRequests.LocalCosmeticResourcePath(currentCosmetic.imageUrl));
		}
		else if (currentCosmeticNew is not null)
		{
			cosmeticImage = currentCosmeticNew?.currentOffer?.ReadCosmeticDisplayImageDirect();
		}
		if (cosmeticImage is not null)
			Win64Helpers.ClipboardSetImage(cosmeticImage);
		menu.CloseMenu();
	}
}
