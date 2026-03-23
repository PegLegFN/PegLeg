public partial class TransferItemCtx : AbstractContextComponent
{
	public override string Id => "TransferItem";
	GameItem currentItem;

	public override void Update(ContextMenuHook hook)
	{
		currentItem = hook?.itemSource?.currentItem;
		bool disabled =
			currentItem?.profile?.account.isOwned != true ||
			!(
				currentItem.profile.profileId == "theater0" ||
				currentItem.profile.profileId == "outpost0"
			);
		SetDisabled(disabled);
		if (disabled)
			currentItem = null;
	}

	//todo: allow granular item transfer
	public async void Transfer()
	{
		menu.CloseMenu();
		await currentItem.TransferStorage();
	}
}
