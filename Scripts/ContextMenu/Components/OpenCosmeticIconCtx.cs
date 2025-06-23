
public partial class OpenCosmeticIconCtx : BaseContextComponent
{
    public override string Id => "OpenCosmeticIcon";
    CosmeticShopOfferEntry currentCosmetic;
    public override void Update(ContextMenuHook hook)
    {
        currentCosmetic = hook?.cosmeticSource;
        SetDisabled(currentCosmetic?.HasImage != true);
    }

    public void Copy()
    {
        if (currentCosmetic is null)
            return;
        currentCosmetic.LoadOrOpenImage();
        menu.CloseMenu();
    }
}
