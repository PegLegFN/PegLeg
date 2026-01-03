using Godot;

public partial class BookmarkItemCtx : BaseContextComponent
{
    public override string Id => "BookmarkItem";
    GameItem currentItem;
    [Export]
    ToggleIconAnim toggleIcon;

    public override void Update(ContextMenuHook hook)
    {
        currentItem = hook?.itemSource?.currentItem;
        SetDisabled(currentItem?.template.CanBeFavourited != true);
        toggleIcon.SetState(GameAccount.ActiveAccount.IsBookmarked(currentItem?.template));
    }

    public void ToggleBookmark()
    {
        if (currentItem?.template.CanBeFavourited != true)
            return;
        GameAccount.ActiveAccount.ToggleBookmarked(currentItem.template);
        toggleIcon.Animate(GameAccount.ActiveAccount.IsBookmarked(currentItem.template));
    }
}
