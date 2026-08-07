using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class PerkViewer : Control
{
	[Export]
	float tweenDuration = 0.1f;
	[Export]
	Control realPerkArea;

	[Export]
	Control optionalPerkArea;

	[Export]
	Control interactionBlocker;

	[Export]
	PerkEntry[] currentPerkEntries;

	[Export]
	Control perkUpArea;

	[Export]
	PerkEntry perkUpEntry;

	[Export]
	PerkEntry[] reperkEntries;

	[Export]
	Control costArea;

	[Export]
	GameItemCost[] costEntries;

	[Export]
	Button perkApplyButton;
	[Export]
	bool previewMaxTierByDefault;

	public override void _Ready()
	{
		for (int i = 0; i < currentPerkEntries.Length; i++)
		{
			currentPerkEntries[i].Pressed += OpenPerkChanger;
		}

		perkUpEntry.Pressed += SelectReplacementPerk;
		for (int i = 0; i < reperkEntries.Length; i++)
		{
			reperkEntries[i].Pressed += SelectReplacementPerk;
		}
	}

	void OpenPerkChanger(int index, string _, bool locked) => OpenPerkChanger(index, locked);
	void SelectReplacementPerk(int index, string id, bool _) => SelectReplacementPerk(id, index);

	GameItem currentItem;
	public void SetItem(GameItem item)
	{
		if (currentItem == item)
			return;
		bool hadItem = currentItem is not null;
		if (hadItem)
			currentItem.OnChanged -= UpdateItem;

		currentItem = item;

		if (currentItem is not null)
		{
			currentItem.OnChanged += UpdateItem;
			UpdateItem(hadItem);
		}
	}

	bool isSchematic = true;
	bool isDefender = true;
	GameItemTemplate.AlterationSlot[] perkSlots = [];
	string[] activePerks = [];
	int unlockedPerks = 0;
	int visiblePerks = 0;

	void UpdateItem() => UpdateItem(true);
	void UpdateItem(bool animateToReset)
	{
		if (currentItem.template is null)
			return;

		isSchematic = currentItem.template.Type == "Schematic";
		isDefender = currentItem.template.Type == "Defender";
		unlockedPerks = 10;
		visiblePerks = currentItem.template.AlterationSlots?.Length ?? 10;
		if (currentItem.profile is null || !isSchematic)
			visiblePerks = 10;


		if (animateToReset)
		{
			ClosePerkChanger();
		}
		else
		{
			selectedPerkIndex = -1;
			interactionBlocker.MouseFilter = MouseFilterEnum.Ignore;
			realPerkArea.AnchorLeft = 0;
			realPerkArea.AnchorRight = 1;
			optionalPerkArea.AnchorLeft = 1;
			optionalPerkArea.AnchorRight = 2;
		}
		activePerks = currentItem.Alterations;
		if (isSchematic)
		{
			//set interactable and assign possibilities (if possibilities greater than one and not max level)
			perkSlots = currentItem.template.AlterationSlots;
			unlockedPerks = 0;
			activePerks ??= new string[perkSlots?.Length ?? 0];
			int itemLevel = currentItem.attributes?["level"]?.GetValue<int>() ?? 0;
			int itemRarity = currentItem.template.RarityLevel;
			for (int i = 0; i < (perkSlots?.Length ?? 0); i++)
			{
				if (perkSlots[i].requiredLevel <= itemLevel && perkSlots[i].RequiredRarityLevel <= itemRarity)
					unlockedPerks = i + 1;
				var baseAlteration = activePerks[i] is not null ? GameItemTemplate.Get(activePerks[i]) : null;
				if (baseAlteration is null && perkSlots[i].OptionsForLevel(1) is string[] options && options.Length == 1)
				{
					activePerks[i] = options[0];
				}
			}
		}
		else
			perkSlots = [];
		RefreshActivePerks();
	}

	void RefreshActivePerks()
	{
		activePerks ??= [];
		for (int i = 0; i < activePerks.Length; i++)
		{
			if (i + 1 > visiblePerks)
			{
				currentPerkEntries[i].Visible = false;
				continue;
			}
			currentPerkEntries[i].Visible = true;
			currentPerkEntries[i].SetPerkAlteration(activePerks[i], !isDefender, i);
			if (currentItem.profile?.account?.isOwned == false)
			{
				currentPerkEntries[i].SetInteractable(true);
				currentPerkEntries[i].SetLocked(i + 1 > unlockedPerks);
				continue;
			}

			if (i >= perkSlots.Length)
			{
				currentPerkEntries[i].SetInteractable(false);
				currentPerkEntries[i].SetLocked(isSchematic || isDefender);
				continue;
			}
			currentPerkEntries[i].SetInteractable(perkSlots[i].options.Length > 1 || PerkIsUpgradeable(activePerks[i]));
			currentPerkEntries[i].SetLocked(currentItem.profile is not null && i + 1 > unlockedPerks);

			currentPerkEntries[i].SetLockLevel(perkSlots[i].requiredLevel);
			currentPerkEntries[i].SetLockRarity(perkSlots[i].RequiredRarityLevel);
		}
		for (int i = activePerks.Length; i < currentPerkEntries.Length; i++)
		{
			currentPerkEntries[i].Visible = false;
		}
	}

	static bool PerkIsUpgradeable(string perk) =>
		perk is null ||
		perk.EndsWith("t01") ||
		perk.EndsWith("t02") ||
		perk.EndsWith("t03") ||
		perk.EndsWith("t04");

	int selectedPerkIndex = -1;
	GameItemTemplate selectedPerk;
	bool selectedPerkLocked = false;
	Tween wipeTween = null;

	public void OpenPerkChanger(int index, bool isLocked = false)
	{
		//GD.Print("opening perk changer for index: " + index);
		var baseAlteration = activePerks[index] is not null ? GameItemTemplate.Get(activePerks[index]) : null;

		int currentLevel = baseAlteration?.RarityLevel ?? 1;
		bool useMaxLevel = currentItem.profile is null && Input.IsKeyPressed(Key.Shift);
		useMaxLevel |= currentItem?.profile?.account?.isOwned == true && currentItem.profile.profileId == FnProfileTypes.AccountItems && Input.IsKeyPressed(Key.Shift);
		if (useMaxLevel)
			currentLevel = 5;

		string[] possibilities = perkSlots[index].OptionsForLevel(currentLevel);

		selectedPerk = null;
		if (baseAlteration?["RarityUpRecipe"] is null && possibilities.Length == 0)
		{
			//this shouldnt happen, but if it does, kablam
			GD.PushWarning("Kablam (no perk possibilities?)");
			return;
		}

		selectedPerkLocked = isLocked;

		bool wasOpen = selectedPerkIndex != -1;
		selectedPerkIndex = index;
		selectedPerk = baseAlteration;
		bool interactable = currentItem?.profile?.account == null ||
			(currentItem?.profile?.account == GameAccount.ActiveAccount && !isLocked);

		if (!useMaxLevel && baseAlteration?["RarityUpRecipe"] is JsonObject rarityUpRecipe)
		{
			string perkUpAlteration = rarityUpRecipe["Result"].ToString();
			perkUpEntry.SetPerkAlteration(perkUpAlteration, true);
			perkUpEntry.SetInteractable(interactable);
			perkUpArea.Visible = true;
		}
		else
			perkUpArea.Visible = false;

		for (int i = 0; i < possibilities.Length; i++)
		{
			string perk = possibilities[i];

			if (perk == baseAlteration?.TemplateId)
			{
				reperkEntries[i].Visible = false;
				continue;
			}

			reperkEntries[i].SetPerkAlteration(perk, true, i + 1);
			reperkEntries[i].SetInteractable(interactable);
			reperkEntries[i].Visible = true;
		}
		//if(index == 5 && !isTrap)
		//    reperkEntries[possibilities.Length].Visible = false;
		for (int i = possibilities.Length; i < reperkEntries.Length; i++)
		{
			reperkEntries[i].Visible = false;
		}

		//reset cost visuals
		selectedReplacementPerk = null;
		costArea.Visible = false;
		for (int i = 0; i < costEntries.Length; i++)
		{
			costEntries[i].Visible = false;
		}
		perkApplyButton.Visible = false;

		if (wasOpen)
			return;

		UISounds.PlaySound("WipeAppear");
		interactionBlocker.MouseFilter = MouseFilterEnum.Stop;
		if (wipeTween?.IsRunning() ?? false)
			wipeTween.Kill();
		wipeTween = GetTree().CreateTween().Parallel();
		wipeTween.SetTrans(Tween.TransitionType.Linear);
		wipeTween.Parallel().TweenProperty(realPerkArea, "anchor_left", -1, tweenDuration).SetEase(Tween.EaseType.Out);
		wipeTween.Parallel().TweenProperty(realPerkArea, "anchor_right", 0, tweenDuration).SetEase(Tween.EaseType.Out);
		wipeTween.Parallel().TweenProperty(optionalPerkArea, "anchor_left", 0, tweenDuration).SetEase(Tween.EaseType.In);
		wipeTween.Parallel().TweenProperty(optionalPerkArea, "anchor_right", 1, tweenDuration).SetEase(Tween.EaseType.In);
		wipeTween.Finished += () => interactionBlocker.MouseFilter = MouseFilterEnum.Ignore;
	}

	string selectedReplacementPerk;
	int selectedReplacementPerkRarity;
	bool replacementIsReperk;
	public void SelectReplacementPerk(string replacementId, int replacementIndex)
	{
		if (currentItem.profile is null)
		{
			activePerks[selectedPerkIndex] = replacementId;
			RefreshActivePerks();
			ClosePerkChanger();
			return;
		}

		GD.Print("selecting perk: " + replacementId);

		selectedReplacementPerk = replacementId;
		GameItemTemplate replacementPerkTemplate = GameItemTemplate.Get(replacementId);
		selectedReplacementPerkRarity = replacementPerkTemplate.RarityLevel;

		GameItem.ItemData[] costItemData = [];
		var upgradedPerk = selectedPerk;
		while (replacementPerkTemplate.Rarity != upgradedPerk.Rarity)
		{
			var upgradeCosts =
				(upgradedPerk["RarityUpRecipe"]?["Cost"]?.Deserialize<Dictionary<string, int>>() ?? [])
				.Select(kvp => new GameItem.ItemData(kvp.Key, kvp.Value))
				.ToArray();
			var next = GameItemTemplate.Get(upgradedPerk["RarityUpRecipe"]?["Result"]?.ToString());
			if (next is null)
				break;
			upgradedPerk = next;
			costItemData = GameItem.ItemData.Add(costItemData, upgradeCosts);
		}

		replacementIsReperk = upgradedPerk != replacementPerkTemplate;

		if (replacementIsReperk)
		{
			var extraCost = (replacementPerkTemplate["AdditionalRespecCost"]?.Deserialize<Dictionary<string, int>>() ?? [])
						.Select(kvp => new GameItem.ItemData(kvp.Key, kvp.Value))
						.ToArray();
			costItemData = GameItem.ItemData.Add(costItemData, GameItem.ItemData.Add(perkSlots[selectedPerkIndex].respecCost, extraCost));
		}

		var costItems = costItemData.Select(data => data.ToItem()).ToArray();
		bool allCostsMet = costItems.Length > 0;
		costArea.Visible = costItems.Length > 0;
		for (int i = 0; i < costItems.Length; i++)
		{
			costEntries[i].SetItem(costItems[i]);
			if (!costEntries[i].CanAfford)
				allCostsMet = false;
			costEntries[i].Visible = true;
		}
		GD.Print($"Perk affordable: {allCostsMet}");
		for (int i = costItems.Length; i < costEntries.Length; i++)
		{
			costEntries[i].Visible = false;
		}
		perkApplyButton.Visible = !selectedPerkLocked;
		// perma disable for now
		//perkApplyButton.Disabled = true;
	}

	public async void ApplyReplacementPerk()
	{
		if (currentItem.profile is null || selectedReplacementPerk == null)
			return;
		GD.Print("applying perk: " + selectedReplacementPerk);
		int upgrades = selectedReplacementPerkRarity - selectedPerk.RarityLevel;
		bool fullAnim = upgrades > 1 || (upgrades > 0 && replacementIsReperk);
		string targetId = currentItem.uuid;
		int targetPerk = selectedPerkIndex;
		string targetReperk = replacementIsReperk ? selectedReplacementPerk : null;
		async Task PerkTask()
		{
			for (int i = 0; i < upgrades; i++)
			{
				var result = await currentItem.profile.PerformOperation("UpgradeAlteration", new JsonObject()
				{
					["targetItemId"] = targetId,
					["alterationSlot"] = targetPerk
				});
				if (result is null)
					return;
			}
			if (targetReperk is null)
				return;
			await currentItem.profile.PerformOperation("RespecAlteration", new JsonObject()
			{
				["targetItemId"] = targetId,
				["alterationSlot"] = targetPerk,
				["alterationId"] = targetReperk
			});
		}
		//await profile request
		Task upgradeTask = PerkTask();
		ItemUpgradeAnimation.PlayAnimation(currentItem.GetTexture(), () => upgradeTask, !fullAnim);
		//await Task.WhenAll(upgradeTask);
		//OpenPerkChanger(selectedPerkIndex, selectedPerkLocked);
	}

	public void ClosePerkChanger()
	{
		if (selectedPerkIndex == -1)
			return;
		selectedPerkIndex = -1;

		UISounds.PlaySound("WipeDisappear");
		interactionBlocker.MouseFilter = MouseFilterEnum.Stop;
		if (wipeTween?.IsRunning() ?? false)
			wipeTween.Kill();
		wipeTween = GetTree().CreateTween();
		wipeTween.Parallel().TweenProperty(realPerkArea, "anchor_left", 0, tweenDuration).SetEase(Tween.EaseType.In);
		wipeTween.Parallel().TweenProperty(realPerkArea, "anchor_right", 1, tweenDuration).SetEase(Tween.EaseType.In);
		wipeTween.Parallel().TweenProperty(optionalPerkArea, "anchor_left", 1, tweenDuration).SetEase(Tween.EaseType.Out);
		wipeTween.Parallel().TweenProperty(optionalPerkArea, "anchor_right", 2, tweenDuration).SetEase(Tween.EaseType.Out);
		wipeTween.Finished += () => interactionBlocker.MouseFilter = MouseFilterEnum.Ignore;
	}
}
