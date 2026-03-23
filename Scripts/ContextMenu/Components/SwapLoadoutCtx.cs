public partial class SwapLoadoutCtx : AbstractContextComponent
{
	public override string Id => "SwapLoadout";
	HeroLoadoutEntry currentLoadout;
	public override void Update(ContextMenuHook hook)
	{
		currentLoadout = hook?.itemSource is HeroLoadoutEntry hl ? hl : null;
		SetDisabled(currentLoadout?.currentItem?.profile is null);
	}

	public void Inspect()
	{
		if (currentLoadout?.currentItem?.profile is null)
			return;
		currentLoadout.PerformRecycleSelection("swap");
		menu.CloseMenu();
	}
}
