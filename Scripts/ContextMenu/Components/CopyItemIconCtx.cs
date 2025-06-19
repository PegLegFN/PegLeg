
public partial class CopyItemIconCtx : BaseContextComponent
{
    public override string Id => "CopyItemIcon";
    GameItem currentItem;
    public override void Update(ContextMenuHook hook)
    {
        currentItem = hook?.itemSource?.currentItem;
        SetDisabled(true);
    }

    public void Copy()
    {
        if (currentItem is null)
            return;
        //todo: implement image clipboard stuff
        Win64Helpers.ClipboardSetImage(currentItem.GetTexture().GetImage());
        menu.CloseMenu();
    }
}
