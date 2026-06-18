using Godot;
using Polly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

public partial class SurvivorSquadEntry : Control
{
	[Export]
	string synergy;

	[Export(PropertyHint.ArrayType)]
	string[] slotRequirements;

	[ExportGroup("References")]
	[Export]
	Label squadNameLabel;

	[Export]
	TextureRect squadIcon;

	[Export]
	Label fortPointsLabel;

	[Export]
	TextureRect fortPointsIcon;

	[Export]
	InventoryItemSlot leadSurvivorSlot;

	[Export(PropertyHint.ArrayType)]
	InventoryItemSlot[] survivorSlots;

	[Export]
	Control summaryParent;

	Control[] summaryNodes = [];

	bool squadUpdateQueued = false;
	GameAccount overrideAccount;

	public override void _Ready()
	{
		//GD.Print($"{synergy} ({BanjoAssets.supplimentaryData.SquadNames.ContainsKey(synergy)})");
		squadNameLabel.Text = PegLegResourceManager.supplimentaryData.SquadNames[synergy];
		squadIcon.Texture = PegLegResourceManager.supplimentaryData.SquadIcons[synergy];
		fortPointsIcon.Texture = PegLegResourceManager.supplimentaryData.SquadFortIcons[synergy];

		summaryNodes = summaryParent?.GetChildren().OfType<Control>().ToArray() ?? summaryNodes;

		leadSurvivorSlot.OnItemChangeRequested += slot => HandleChangeRequest(slot, 0);
		leadSurvivorSlot.OnSlotItemChanged += _ =>
		{
			for (int i = 0; i < survivorSlots.Length; i++)
				survivorSlots[i].UpdateItem();
			squadUpdateQueued = true;
		};
		leadSurvivorSlot.SetSlotData(
			FnProfileTypes.AccountItems,
			"Worker",
			PegLegResourceManager.supplimentaryData.SynergyToSquadId[synergy],
			0,
			"HomebaseNode:questreward_" + slotRequirements[0].ToLower()
		);

		for (int i = 0; i < survivorSlots.Length; i++)
		{
			int slotIndex = i + 1;

			survivorSlots[i].OnItemChangeRequested += slot => HandleChangeRequest(slot, slotIndex);
			survivorSlots[i].OnSlotItemChanged += _ => squadUpdateQueued = true;

			survivorSlots[i].SetSlotData(
				FnProfileTypes.AccountItems,
				"Worker",
				PegLegResourceManager.supplimentaryData.SynergyToSquadId[synergy],
				slotIndex,
				"HomebaseNode:questreward_" + slotRequirements[slotIndex].ToLower()
			);
		}

		SetOverrideAccount();
		GameAccount.ActiveAccountChanged += OnActiveAccountChanged;
	}

	public override void _ExitTree()
	{
		GameAccount.ActiveAccountChanged -= OnActiveAccountChanged;
	}

	void OnActiveAccountChanged()
	{
		if (overrideAccount is null)
			UpdateAccount();
	}

	public void SetOverrideAccount(GameAccount account = null)
	{
		overrideAccount = account;
		UpdateAccount();
	}

	CancellationTokenSource accountChangeCts;
	public async void UpdateAccount()
	{
		//show loading icon?
		Visible = false;
		fortPointsLabel.Text = "+???";

		accountChangeCts = accountChangeCts.CancelAndRegenerate(out var ct);

		var account = overrideAccount ?? GameAccount.ActiveAccount;
		var newProfile = await account.GetProfile(FnProfileTypes.AccountItems).Query();
		if (newProfile is null || ct.IsCancellationRequested)
			return;

		bool hasAnySlot = slotRequirements.Distinct().Any(requirement =>
			newProfile.GetFirstTemplateItem("HomebaseNode:questreward_" + requirement.ToLower()) is not null
		);

		leadSurvivorSlot.SetOverrideAccount(overrideAccount);

		for (int i = 0; i < survivorSlots.Length; i++)
		{
			survivorSlots[i].SetOverrideAccount(overrideAccount);
		}

		if (hasAnySlot)
			Visible = true;

		UpdateSquadSummary();
	}

	public override void _Process(double delta)
	{
		if (squadUpdateQueued)
			UpdateSquadSummary();
		squadUpdateQueued = false;
	}

	void UpdateSquadSummary()
	{
		if (!IsInstanceValid(fortPointsLabel))
			return;
		int statValue = leadSurvivorSlot.slottedItem?.CalculateSurvivorRating(true) ?? 0;
		statValue += survivorSlots.Sum(slot => slot.slottedItem?.CalculateSurvivorRating(true) ?? 0);
		fortPointsLabel.Text = $"+{statValue}";
		summaryParent.Visible = false;

		if (leadSurvivorSlot.slottedItem is GameItem item)
		{
			var targetPersonality = item.Personality;
			var matchingCount = survivorSlots.Count(slot => slot.slottedItem?.Personality == targetPersonality);
			SetSummaryCount(
				0,
				true,
				item.GetTexture(FnItemTextureType.Personality),
				$"{matchingCount}/7",
				matchingCount == 7 ? Colors.Yellow : Colors.White,
				null,
				$"Leader Personality Match\n{matchingCount}/7"
			);
		}
		else
		{
			SetSummaryCount(0, false);
		}
		var distinctSetBonuses = survivorSlots
			.Select(slot => slot.slottedItem?.SetBonus)
			.Where(s => s is not null)
			.Distinct()
			.ToArray();
		for (int i = 0; i < distinctSetBonuses.Length; i++)
		{
			var setBonus = distinctSetBonuses[i];
			var matching = survivorSlots.Where(slot => slot.slottedItem?.SetBonus == setBonus).ToArray();
			var baseRequiredCount = setBonus switch
			{
				"Ability Damage" or "Melee Damage" or
				"Ranged Damage" or "Trap Damage" => 3,
				_ => 2
			};
			var boostCount = matching.Length / baseRequiredCount;
			var boostPercent = setBonus == "Trap Durability" ? 8 : 5;
			var countText = $"{matching.Length}/{baseRequiredCount}{(boostCount > 1 ? $" (x{boostCount})" : "")}";
			SetSummaryCount(
				i + 1,
				true,
				matching[0].slottedItem.GetTexture(FnItemTextureType.SetBonus),
				countText,
				matching.Length >= baseRequiredCount ? Colors.Yellow : Colors.White,
				matching.Length >= baseRequiredCount ? $"+{boostPercent*boostCount}%" : null,
				$"{setBonus}\n{countText}"
			);
		}
		for (int i = distinctSetBonuses.Length + 1; i < summaryNodes.Length; i++)
		{
			SetSummaryCount(i, false);
		}
	}

	void SetSummaryCount(int idx, bool visible) =>
		SetSummaryCount(idx, visible, null, null, default, null, null);

	void SetSummaryCount(int idx, bool visible, Texture2D icon, string countText, Color countTint, string bonusText, string tooltip)
	{
		if (summaryNodes.Length <= idx || summaryNodes.Length == 0)
			return;
		var node = summaryNodes[idx];
		node.Visible = visible;
		if (!visible)
			return;
		summaryParent.Visible = true;
		node.TooltipText = tooltip;
		if (node.GetNodeOrNull<TextureRect>("%Icon") is TextureRect iconRect)
			iconRect.Texture = icon;
		if (node.GetNodeOrNull<Label>("%Counter") is Label countLabel)
		{
			countLabel.Text = countText;
			countLabel.SelfModulate = countTint;
		}
		if (node.GetNodeOrNull<Label>("%BonusText") is Label bonusLabel)
		{
			bonusLabel.Text = bonusText;
			bonusLabel.Visible = bonusText is not null;
		}
	}

	static readonly Predicate<GameItem> standardFilter = item =>
		(item.attributes?["squad_id"]?.ToString() ?? "") == "" &&
		item.template.SubType is null;

	static readonly Predicate<GameItem> leaderFilter = item =>
		(item.attributes?["squad_id"]?.ToString() ?? "") == "" &&
		item.template.SubType is not null;

	async void HandleChangeRequest(InventoryItemSlot slot, int slotIndex)
	{
		var profile = slot.currentProfile;
		if (!(profile?.account.isOwned ?? false) || squadLocked)
			return;

		var filter = slot == leadSurvivorSlot ? leaderFilter : standardFilter;
		var fromItem = slot.slottedItem;
		var squadID = PegLegResourceManager.supplimentaryData.SynergyToSquadId[synergy];

		var selectedItem = await SimpleItemSelector.OpenSelector(profile.GetItems("Worker", filter), SimpleItemSelector.DefaultConfig with
		{
			title = "Select a Survivor",
			overrideSurvivorSquad = squadID,
			allowEmptySelection = true,
			showSurvivorFilters = true,
		});

		//occurs when cancelled
		if (selectedItem is null)
			return;

		JsonObject body = null;
		if (selectedItem?.profile is not null)
		{
			//set slotted survivor
			body = new()
			{
				["characterId"] = selectedItem.uuid,
				["squadId"] = squadID,
				["slotIndex"] = slotIndex
			};
		}
		else if (fromItem?.profile is not null)
		{
			//unslot slotted survivor
			body = new()
			{
				["characterId"] = fromItem.uuid,
				["squadId"] = "",
				["slotIndex"] = 0
			};
		}

		if (body is not null && profile.profileId == FnProfileTypes.AccountItems)
		{
			try
			{
				squadLocked = true;
				await profile.PerformOperation("AssignWorkerToSquad", body.ToString());
				GD.Print("Last Op: " + profile.lastOp);
			}
			finally
			{
				squadLocked = false;
			}
		}
	}

	bool squadLocked = false;
}
