using Godot;
using System;
using System.Reflection;

public partial class FavouriteItemCtx : BaseContextComponent
{
    public override string Id => "FavouriteItem";
    GameItem currentItem;
    [Export]
    ToggleIconAnim toggleIcon;

    public override void Update(ContextMenuHook hook)
    {
        currentItem = hook?.itemSource?.currentItem;
        bool disabled = currentItem?.profile?.account.isOwned != true || currentItem.template?.CanBeFavourited != true;
        SetDisabled(disabled);
        if (disabled)
            currentItem = null;
        toggleIcon.SetState(currentItem?.IsFavourited ?? false);
    }

    bool isToggling;
    public void ToggleFavourite()
    {
        if (currentItem is null || isToggling)
            return;
        bool newState = !currentItem.IsFavourited;
        currentItem.SetFavourited(newState);
        toggleIcon.Animate(newState);
    }
}
