using Godot;
using System;

public partial class InspectOfferCtx : AbstractContextComponent
{
    public override string Id => "InspectOffer";
    GameOfferEntry entry;
    public override void Update(ContextMenuHook hook)
    {
        entry = hook?.offerSource;
        SetDisabled(entry?.currentOffer is null);
    }

    public void Inspect()
    {
        if (entry?.currentOffer is null)
            return;
        entry.Inspect();
        menu.CloseMenu();
    }
}
