using Godot;

public partial class CopyOfferIdCtx : AbstractContextComponent
{
	public override string Id => "CopyOfferId";
	GameOffer currentOffer;
	public override void Update(ContextMenuHook hook)
	{
		currentOffer = hook?.offerSource?.currentOffer;
		SetDisabled(currentOffer is null);
	}

	public void Copy()
	{
		if (currentOffer is null)
			return;
		DisplayServer.ClipboardSet(Input.IsKeyPressed(Key.Shift) ? currentOffer.rawData.ToString() : currentOffer.OfferId);
		menu.CloseMenu();
	}
}
