using Godot;
using System;

public partial class SwapLoadoutCtx : BaseContextComponent
{
    public override string Id => "SwapLoadout";
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
        currentLoadout.PerformRecycleSelection("swap");
        menu.CloseMenu();
    }
}
