
using Godot;

public partial class CopyCosmeticIconCtx : AbstractContextComponent
{
    public override string Id => "CopyCosmeticIcon";
    CosmeticShopOfferEntry currentCosmetic;
    public override void Update(ContextMenuHook hook)
    {
        currentCosmetic = hook?.cosmeticSource;
        SetDisabled(currentCosmetic?.imageUrl is null || !Win64Helpers.isWindows);
    }

    public void Copy()
    {
        if (currentCosmetic is null)
            return;
        Image cosmeticImage = Image.LoadFromFile(CatalogRequests.LocalCosmeticResourcePath(currentCosmetic.imageUrl));
        Win64Helpers.ClipboardSetImage(cosmeticImage);
        menu.CloseMenu();
    }
}
