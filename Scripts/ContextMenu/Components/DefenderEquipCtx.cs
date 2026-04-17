using Godot;
using System;

public partial class DefenderEquipCtx : AbstractContextComponent
{
	public override string Id => "DefenderEquip";
	GameItem currentItem;

	public override void Update(ContextMenuHook hook)
	{
		currentItem = hook?.itemSource?.currentItem;
		bool disabled = 
			currentItem?.profile.profileId != FnProfileTypes.AccountItems || 
			currentItem?.profile?.account?.isOwned != true || 
			!currentItem.templateId.StartsWith("Defender:");
		if (disabled)
			currentItem = null;
		SetDisabled(disabled);
	}

	public async void OpenEquipMenu()
	{
		if (currentItem?.profile is null)
			return;
		var cur = currentItem;
		//TODO: filter based on subtype
		menu.CloseMenu();
		var selectedItem = await SimpleItemSelector.OpenSelector(cur.profile.GetItems("Schematic", i => i.template?.Category == "Ranged" || i.template?.Category == "Melee"));
		if (selectedItem is null || selectedItem == GameItem.Empty)
			return;
		await cur.profile.PerformOperation("AssignWeaponToDefender", $$"""
		{
			"weaponSchematicId": "{{selectedItem.uuid}}",
			"defenderId": "{{cur.uuid}}"
		}
		""");
	}
}
