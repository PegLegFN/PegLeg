public partial class MoveLoadoutCtx : AbstractContextComponent
{
	public override string Id => "MoveLoadout";
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
		currentLoadout.PerformRecycleSelection("move");
		menu.CloseMenu();
	}
}
