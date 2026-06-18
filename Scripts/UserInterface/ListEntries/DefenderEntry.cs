using Godot;
using System.Collections.Generic;

public partial class DefenderEntry : GameItemEntry
{
	[Signal]
	public delegate void WeaponPressedEventHandler();
	[Export]
	GameItemEntry equippedItemEntry;
	[Export]
	Control emptyItem;

	protected override void UpdateItem(GameItem item)
	{
		base.UpdateItem(item);
		if (equippedItemEntry is null)
			return;
		if (item?.templateId?.StartsWith("Defender") != true)
		{
			equippedItemEntry.ClearItem();
			equippedItemEntry.Visible = false;
			emptyItem.Visible = false;
			return;
		}
		GameItem weapon = null;
		if (item.attributes?["weapon_schematic"]?.ToString() is string weaponUUID)
			weapon = item.profile?.GetItem(weaponUUID);

		if(weapon is null)
		{
			equippedItemEntry.ClearItem();
			equippedItemEntry.Visible = false;
			emptyItem.Visible = item.profile?.account.isOwned == true;
			return;
		}

		equippedItemEntry.SetItem(weapon);
		equippedItemEntry.Visible = true;
		emptyItem.Visible = false;
	}

	public void InspectWeapon() => equippedItemEntry.Inspect();
	void PressWeapon() => EmitSignalWeaponPressed();

	public override void ClearItem(Texture2D clearIcon)
	{
		base.ClearItem(clearIcon);
		if (equippedItemEntry is null)
			return;
		equippedItemEntry.ClearItem();
		equippedItemEntry.Visible = false;
		emptyItem.Visible = false;
	}
}
