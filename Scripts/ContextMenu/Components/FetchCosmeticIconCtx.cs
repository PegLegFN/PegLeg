public partial class FetchCosmeticIconCtx : AbstractContextComponent
{
	public override string Id => "FetchCosmeticIcon";
	CosmeticShopOfferEntry currentCosmetic;
	public override void Update(ContextMenuHook hook)
	{
		currentCosmetic = hook?.cosmeticSource;
		SetDisabled(currentCosmetic?.imageUrl is not null || !Win64Helpers.isWindows);
	}

	public void Copy()
	{
		if (currentCosmetic is null)
			return;
		currentCosmetic.TryFetchResource(true);
		menu.CloseMenu();
	}
}
