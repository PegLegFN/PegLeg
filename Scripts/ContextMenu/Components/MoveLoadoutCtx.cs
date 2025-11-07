using Godot;
using System;

public partial class MoveLoadoutCtx : BaseContextComponent
{
    public override string Id => "MoveLoadout";
    HeroLoadoutEntry currentLoadout;
    public override void Update(ContextMenuHook hook)
    {
        currentLoadout = hook?.itemSource is HeroLoadoutEntry hl ? hl : null;
        SetDisabled(currentLoadout is null);
    }

    public void Inspect()
    {
        if (currentLoadout is null)
            return;
        currentLoadout.PerformRecycleSelection("move");
        menu.CloseMenu();
    }
}
