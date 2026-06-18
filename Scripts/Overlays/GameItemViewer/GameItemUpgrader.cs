using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public partial class GameItemUpgrader : Control
{
	[Export]
	VirtualTabBar modeSelector;

	[Export]
	Control upgradePanel;

	[Export]
	Control increaseRarityPanel;

	[ExportGroup("Confirm Button")]
	[Export]
	Control confirmPanel;

	[Export]
	Label confirmLabel;

	[Export]
	ResponsiveButton confirmButton;

	[Export]
	string panelActiveVarient;

	[Export]
	string panelDisabledVarient;

	[ExportGroup("Upgrade Options")]
	[Export]
	Control upgradeControls;

	[Export]
	Label levelSliderMin;

	[Export]
	Slider levelSlider;

	[Export]
	Label levelSliderMax;

	[Export]
	VirtualTabBar tierSelector;

	[Export]
	VirtualTabBar weaponCoreSelector;

	[ExportGroup("Upgrade Preview")]
	[Export]
	TextureRect currentWeaponCore;

	[Export]
	Label currentLevelLabel;

	[Export]
	ItemTierDisplay currentTierDisplay;

	[Export]
	TextureRect nextWeaponCore;

	[Export]
	Label nextLevelLabel;

	[Export]
	ItemTierDisplay nextTierDisplay;

	[ExportGroup("Core Images")]
	[Export]
	Texture2D copper;
	[Export]
	Texture2D silver;
	[Export]
	Texture2D malachite;
	[Export]
	Texture2D obsidian;
	[Export]
	Texture2D shadowshard;
	[Export]
	Texture2D brightcore;
	[Export]
	Texture2D sunbeam;

	[ExportGroup("Increase Rarity Preview")]
	[Export]
	GameItemEntry fromRarity;

	[Export]
	GameItemEntry toRarity;

	[ExportGroup("Costs")]
	[Export]
	GameItemCost[] costItems;

	GameItem currentItem;
	int minLevel = 1;
	int maxTier = 1;
	int maxDisplayTier = 1;
	bool showCores = false;
	bool isShardable = false;
	bool canLevel = false;
	bool capped = false;
	bool canSupercharge = false;

	public override void _Ready()
	{
		modeSelector.LatestTabChanged += SetMode;
		levelSlider.ValueChanged += LevelSliderChanged;
		tierSelector.LatestTabChanged += TierSelectorChanged;
		weaponCoreSelector.LatestTabChanged += CoreSelectorChanged;
		confirmButton.HoldPressed += AttemptUpgrade;
	}

	public void SetItem(GameItem item)
	{
		if (item == currentItem)
		{
			UpdateItem();
			return;
		}

		if (currentItem is not null)
			currentItem.OnChanged -= UpdateItem;

		currentItem = item;
		if (currentItem?.template is null)
			currentItem = null;
		if (currentItem?.profile?.account?.isOwned != true)
			currentItem = null;
		if (currentItem?.profile?.profileId != FnProfileTypes.AccountItems)
			currentItem = null; ;//todo: support collection book profiles

		if (currentItem is not null)
		{
			currentItem.OnChanged += UpdateItem;
			UpdateItem();
		}
		else
		{
			Visible = false;
		}
	}

	void UpdateItem()
	{
		var campaign = currentItem?.profile.account.GetProfile(FnProfileTypes.AccountItems);

		int level = currentItem.attributes?["level"]?.GetValue<int>() ?? 0;
		int tier = currentItem.template.Tier;
		minLevel = Mathf.Min(level + 1, 50);
		int maxLevel = currentItem.template.MaxTier * 10;
		showCores = currentItem.template.Type == "Schematic" && (currentItem.template.Category ?? "Trap") != "Trap";
		isShardable = showCores && (currentItem.template.SubType ?? "Explosive") != "Explosive";
		canLevel = currentItem.template.CanBeLeveled == true && level < maxLevel;
		canSupercharge = currentItem.template.CanBeSupercharged == true && level >= 50 && level < 60;

		if (campaign.GetFirstTemplateItem("HomebaseNode:questreward_evolution4") is not null)
			maxTier = 5;
		else if (campaign.GetFirstTemplateItem("HomebaseNode:questreward_evolution3") is not null)
			maxTier = 4;
		else if (campaign.GetFirstTemplateItem("HomebaseNode:questreward_evolution2") is not null)
			maxTier = 3;
		else if (campaign.GetFirstTemplateItem("HomebaseNode:questreward_evolution") is not null)
			maxTier = 2;
		else
			maxTier = 2;

		canLevel &= currentItem.template.Type switch
		{
			"Worker" => campaign.GetFirstTemplateItem("HomebaseNode:questreward_feature_survivorlevelup") is not null,
			"Schematic" => campaign.GetFirstTemplateItem("HomebaseNode:questreward_feature_survivorlevelup") is not null,
			"Hero" or "Defender" => campaign.GetFirstTemplateItem("HomebaseNode:questreward_feature_herolevelup") is not null,
			_ => false
		};

		maxDisplayTier = currentItem.template.MaxTier;
		maxTier = Mathf.Min(maxTier, maxDisplayTier);
		capped = level == tier * 10 && maxTier == tier;
		var minTier = tier;
		if (level == tier * 10 && !capped)
		{
			minLevel -= 1;
			minTier += 1;
		}
		canLevel &= !capped;

		var rarityUp = currentItem.template.TryGetNextRarity();

		modeSelector.Visible = (canLevel || canSupercharge) && rarityUp is not null;
		modeSelector.SetTabPressed((!modeSelector.Visible && rarityUp is not null) ? 1 : 0);
		Visible = canLevel || canSupercharge || rarityUp is not null;

		if (canLevel || canSupercharge)
		{
			if (canLevel)
			{
				costLock++;
				for (int i = 0; i < 5; i++)
				{
					tierSelector.SetTabDisabled(i, i + 1 < minTier || i + 1 > maxTier);
					tierSelector.SetTabHidden(i, i + 1 > maxDisplayTier);
				}
				tierSelector.UpdateTabModes();
				tierSelector.SetFirstValidTabPressed();

				levelSlider.MinValue = (minTier - 1) * 10;
				levelSliderMin.Text = levelSlider.MinValue.ToString();
				levelSlider.MaxValue = minTier * 10;
				levelSliderMax.Text = levelSlider.MaxValue.ToString();
				levelSlider.Value = minLevel;
				costLock--;
			}

			currentWeaponCore.Visible = showCores;
			currentWeaponCore.Texture = SelectCore(tier, currentItem.template["EvoType"]?.ToString() == "crystal");
			nextWeaponCore.Visible = showCores;
			if (tier > 3)
				weaponCoreSelector.SetTabPressed(currentItem.template["EvoType"]?.ToString() == "crystal" ? 1 : 0);
			nextWeaponCore.Texture = SelectCore(minTier, weaponCoreSelector.LatestTab == 1);
			weaponCoreSelector.Visible = isShardable && currentItem.template.Tier <= 3 && minTier >= 3;

			currentLevelLabel.Text = $"Level {level}";
			currentTierDisplay.SetFromItem(currentItem);
			if (canSupercharge)
			{
				nextLevelLabel.Text = $"Level {level + 2}";
				nextTierDisplay.SetTier(maxDisplayTier);
				nextTierDisplay.SetMaxTier(maxDisplayTier);
				nextTierDisplay.SetSuperchargedTier((currentItem.attributes?["max_level_bonus"]?.GetValue<int>() ?? 0) + 2 / 2);
			}
			else
			{
				nextLevelLabel.Text = $"Level {(int)levelSlider.Value}";
				nextTierDisplay.SetTier(tierSelector.LatestTab + 1);
				nextTierDisplay.SetMaxTier(maxDisplayTier);
				nextTierDisplay.SetSuperchargedTier(0);
			}
		}

		if (rarityUp is not null)
		{
			fromRarity.SetItem(currentItem);
			toRarity.SetItem(rarityUp.CreateInstance());
		}

		SetMode(modeSelector.LatestTab);
	}

	private void SetMode(int value)
	{
		if (currentItem is null)
			return;
		upgradePanel.Visible = value == 0 && (canLevel || canSupercharge);
		upgradeControls.Visible = value == 0 && canLevel;
		increaseRarityPanel.Visible = value == 1;
		weaponCoreSelector.Visible = isShardable && currentItem.template.Tier <= 3 && tierSelector.LatestTab >= 3;
		if (value == 0)
			UpdateUpgradeCosts();
		else
			UpdateRarityIncreaseCosts();
	}

	private void LevelSliderChanged(double value)
	{
		if (levelSlider.Value < minLevel)
		{
			levelSlider.Value = minLevel;
			return;
		}

		if (!canSupercharge)
			nextLevelLabel.Text = $"Level {value}";

		UpdateUpgradeCosts();
	}

	int costLock = 0;

	private void TierSelectorChanged(int value)
	{
		if (currentItem is null)
			return;
		weaponCoreSelector.Visible = isShardable && currentItem.template.Tier <= 3 && value >= 3;
		if (weaponCoreSelector.Visible)
		{
			weaponCoreSelector.SetTabContents([
				new(){icon = SelectCore(value + 1, false), tooltip = (value == 3 ? "Obsidian" : "Brightcore")},
				new(){icon = SelectCore(value + 1, true), tooltip = (value == 3 ? "Shadowshard" : "Sunbeam")},
			]);
		}
		weaponCoreSelector.SetFirstValidTabPressed();
		CoreSelectorChanged(0);

		if (!canSupercharge)
		{
			nextTierDisplay.SetTier(value + 1);
			nextTierDisplay.SetMaxTier(maxDisplayTier);
			nextTierDisplay.SetSuperchargedTier(0);
		}

		costLock++;
		var oldTargetLevel = levelSlider.Value;
		levelSlider.MinValue = value * 10;
		levelSliderMin.Text = levelSlider.MinValue.ToString();
		levelSlider.MaxValue = (value + 1) * 10;
		levelSliderMax.Text = levelSlider.MaxValue.ToString();
		levelSlider.Value = Mathf.Clamp(oldTargetLevel, levelSlider.MinValue, levelSlider.MaxValue);
		if (!canSupercharge)
			nextLevelLabel.Text = $"Level {(int)levelSlider.Value}";
		costLock--;
		UpdateUpgradeCosts();
	}

	private void CoreSelectorChanged(int core)
	{
		bool crystal = isShardable && core == 1;
		nextWeaponCore.Texture = SelectCore(tierSelector.LatestTab + 1, crystal);
	}

	Texture2D SelectCore(int tier, bool crystal) => tier switch
	{
		5 when crystal => sunbeam,
		4 when crystal => shadowshard,
		5 => brightcore,
		4 => obsidian,
		3 => malachite,
		2 => silver,
		_ => copper
	};

	private void UpdateUpgradeCosts()
	{
		if (costLock > 0 || !upgradePanel.Visible)
			return;
		int level = currentItem.attributes?["level"]?.GetValue<int>() ?? 0;
		int tier = currentItem.template?.Tier ?? 0;

		var campaignProfile = currentItem.profile.account.GetProfile(FnProfileTypes.AccountItems);
		bool canAfford = true;

		if (canSupercharge)
		{
			var currentSuperchargeStage = (level - 50) / 2;
			var costDict = currentItem.template["PromotionCosts"]?[currentSuperchargeStage].Deserialize<Dictionary<string, int>>();
			var promotionCosts = GameItem.ItemData.FromDict(costDict);

			SetUpgradeCostItems(promotionCosts, campaignProfile, out canAfford);

			SetButtonEnabled(canAfford);
			confirmLabel.Text = canAfford ? "Supercharge" : "Can't Afford";
			return;
		}

		if (level == 0 || tier == 0)
			return;

		var targetTier = tierSelector.LatestTab + 1;
		var targetLevel = (int)levelSlider.Value;
		GameItemTemplate nextTier = currentItem.template;
		for (int i = tier; i < targetTier; i++)
		{
			var possibleTier = nextTier.TryGetNextTier();
			if (possibleTier is null)
				break;
			nextTier = possibleTier;
		}

		var currentCosts = currentItem.template.GetCombinedUpgradeValue(level);
		var nextCosts = nextTier.GetCombinedUpgradeValue(targetLevel);
		var diffCosts = GameItem.ItemData.Subtract(nextCosts, currentCosts);

		SetUpgradeCostItems(diffCosts, campaignProfile, out canAfford);

		SetButtonEnabled(canAfford);
		confirmLabel.Text = canAfford ? (tier == targetTier ? "Level Up" : "Evolve") : "Can't Afford";
	}

	void SetButtonEnabled(bool value)
	{
		confirmButton.Visible = value;
		confirmPanel.ThemeTypeVariation = value ? panelActiveVarient : panelDisabledVarient;
	}

	private void UpdateRarityIncreaseCosts()
	{
		if (costLock > 0 || !increaseRarityPanel.Visible)
			return;

		var nextRarity = currentItem.template.TryGetNextRarity();
		if (nextRarity is null)
			return;

		bool success = currentItem.template.TryGetRarityUpCost(out var recipeCosts);

		int level = currentItem.attributes?["level"]?.GetValue<int>() ?? 0;

		/*
		var currentBaseCosts = currentItem.template.GetCombinedUpgradeValue(minLevel);
		var currentLevelCosts = currentItem.template.GetCombinedUpgradeValue(Mathf.Min(level, 50));
		var currentLevelupOnlyCosts = GameItem.ItemData.Subtract(currentLevelCosts, currentBaseCosts);

		var nextBaseCosts = nextRarity.GetCombinedUpgradeValue(minLevel);
		var nextLevelCosts = nextRarity.GetCombinedUpgradeValue(Mathf.Min(level, 50));
		var nextLevelupOnlyCosts = GameItem.ItemData.Subtract(nextLevelCosts, nextBaseCosts);

		var resultCosts = GameItem.ItemData.Subtract(nextLevelupOnlyCosts, currentLevelupOnlyCosts);
		*/

		/*
		currentItem.template.TryGetCombinedLevelUpCost(level, out var currentLevelCost);
		currentItem.template.TryGetCombinedLevelUpCost(minLevel, out var currentDefaultXP);
		nextRarity.TryGetCombinedLevelUpCost(level, out var nextLevelCost);
		nextRarity.TryGetCombinedLevelUpCost(minLevel, out var nextDefaultXP);
		var diffCost = nextLevelCost with { quantity = (nextLevelCost.quantity - nextDefaultXP.quantity) - (currentLevelCost.quantity - currentDefaultXP.quantity) };
		*/

		//Man this was stupid to figure out.
		//Turns out that although Epic integrated evolution material costs from previous evolutions into the recipe costs, they
		//didn't integrate the XP costs of evolutions, meaning that the total xp difference needs to include the levelup XP difference PLUS
		//the evolution xp difference. At that point, its easier to just do a total upgrade cost difference, then exclude evo mats from the
		//standard recipe costs

		var currentCosts = currentItem.template.GetCombinedUpgradeValue(level);
		currentItem.template.TryGetCombinedLevelUpCost(1, out var currentDefaultXP);
		currentCosts = GameItem.ItemData.Subtract(currentCosts, [currentDefaultXP]);

		var nextCosts = nextRarity.GetCombinedUpgradeValue(level);
		nextRarity.TryGetCombinedLevelUpCost(1, out var nextDefaultXP);
		nextCosts = GameItem.ItemData.Subtract(nextCosts, [nextDefaultXP]);

		var diffCosts = GameItem.ItemData.Subtract(nextCosts, currentCosts);
		var diffCostTypes = diffCosts.Select(i => i.templateId).ToHashSet();

		var resultCosts = GameItem.ItemData.Add(diffCosts, [.. recipeCosts.Where(i => !diffCostTypes.Contains(i.templateId))]);
		var campaignProfile = currentItem.profile.account.GetProfile(FnProfileTypes.AccountItems);

		SetUpgradeCostItems(resultCosts, campaignProfile, out var canAfford);

		SetButtonEnabled(canAfford);
		confirmLabel.Text = canAfford ? "Increase Rarity" : "Can't Afford";
	}

	private void SetUpgradeCostItems(GameItem.ItemData[] costs, GameProfile compareProfile, out bool canAfford)
	{
		GameItem[] costInstances =
		[
			..costs
				.Select(i => i.ToItem())
				.OrderBy(i => !i.template.DisplayName.Contains("Flux", StringComparison.OrdinalIgnoreCase))
				//.ThenBy(i => !i.template.DisplayName.Contains("Supercharger", StringComparison.OrdinalIgnoreCase))
				.ThenBy(i => -i.template.RarityLevel)
		];

		for (int i = 0; i < Mathf.Min(costInstances.Length, costItems.Length); i++)
		{
			costItems[i].SetItem(costInstances[i], currentItem.profile.account);
			costItems[i].Visible = true;
		}

		for (int i = costInstances.Length; i < costItems.Length; i++)
		{
			costItems[i].ClearItem();
			costItems[i].Visible = false;
		}

		canAfford = costs.All(i => compareProfile.SumTemplateItems(i.templateId) >= i.quantity);
	}

	private void AttemptUpgrade()
	{
		async Task UpgradeTask()
		{
			var desiredTier = tierSelector.LatestTab switch
			{
				_ when tierSelector.LatestTab == currentItem.template.Tier - 1 => "no_tier",
				1 => "ii",
				2 => "iii",
				3 => "iv",
				4 => "v",
				_ => "no_tier"
			};
			bool includeCore = isShardable && tierSelector.LatestTab >= 3 && currentItem.template.Tier <= 3;
			var content = $$"""
            {
                "targetItemId" : "{{currentItem.uuid}}",
                "desiredLevel" : {{(int)levelSlider.Value}},
                "desiredTier" : "{{desiredTier}}",
                "conversionRecipeIndexChoice" : {{(includeCore ? weaponCoreSelector.LatestTab : "-1")}}
            }
            """;
			GD.Print(content.FixNewlines());
			//ensure that the profile is up to date before upgrading
			bool success = await currentItem.profile.TryQuery(true);
			if (success)
				await currentItem.profile.PerformOperation("UpgradeItemBulk", content);
		}
		async Task SuperchargeTask()
		{
			await currentItem.profile.PerformOperation("PromoteItem", $$"""
            {
                "targetItemId" : "{{currentItem.uuid}}"
            }
            """);
		}
		async Task IncreaseRarityTask()
		{
			//ensure that the profile is up to date before upgrading
			bool success = await currentItem.profile.TryQuery(true);
			if (success)
				await currentItem.profile.PerformOperation("UpgradeItemRarity", $$"""
                {
                    "targetItemId" : "{{currentItem.uuid}}"
                }
                """);
		}
		Func<Task> taskFunc = modeSelector.LatestTab == 1 ? IncreaseRarityTask : (canSupercharge ? SuperchargeTask : UpgradeTask);
		ItemUpgradeAnimation.PlayAnimation(currentItem.GetTexture(largePreview: true), taskFunc);
	}
}
