using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class StatChangeWeapon : Control
{
	[Export]
	PackedScene statChangeScene;
	[Export]
	Control statChangeParent;
	[Export]
	GameItemEntry currentItem;
	[Export]
	Button[] rarityToggles;
	[Export]
	Button[] tierToggles;
	[Export]
	Control coreContainer;
	[Export]
	Button crystalToggle;
	[Export]
	Button oreToggle;
	[Export]
	string[] ranges;
	[Export]
	Control rangeSliderContainer;
	[Export]
	Slider rangeSlider;
	[Export]
	FrozenStringSetProxy excludedStats;
	[Export]
	FrozenStringToStringProxy statNameLookup;
	[Export]
	FrozenStringToFloatProxy statPriorityLookup;
	[Export]
	bool dontAutoUpdate = false;
	List<StatChangeEntry> statChanges = [];

	public override void _Ready()
	{
		for (int i = 0; i < tierToggles.Length; i++)
		{
			int idx = i;
			tierToggles[i].Pressed += () => SwitchTier(idx);
		}
		for (int i = 0; i < rarityToggles.Length; i++)
		{
			int idx = i;
			rarityToggles[i].Pressed += () => SwitchRarity(idx);
		}
		crystalToggle.Toggled += SwitchCrystal;
		rangeSlider.ValueChanged += SwitchRange;
	}

	bool itemDirty = false;
	public override void _Process(double delta)
	{
		if (itemDirty && !dontAutoUpdate)
		{
			itemDirty = false;
			UpdateChanges();
		}
	}

	bool isCrystal = false;
	void SwitchCrystal(bool newVal)
	{
		newVal &= currentTier >= 3;
		if (newVal != isCrystal)
			itemDirty = true;
		isCrystal = newVal;
		crystalToggle.ButtonPressed = newVal;
		oreToggle.ButtonPressed = !newVal;
	}

	int currentTier = 4;
	void SwitchTier(int newTier)
	{
		newTier = Mathf.Min(newTier, currentRarity + 1);
		if (newTier != currentTier)
			itemDirty = true;
		currentTier = newTier;
		for (int i = 0; i < tierToggles.Length; i++)
		{
			tierToggles[i].ButtonPressed = currentTier >= i;
		}
		if (currentTier < 3 && isCrystal)
			SwitchCrystal(false);
	}

	int currentRarity = 4;
	void SwitchRarity(int newRarity)
	{
		if (newRarity != currentRarity)
			itemDirty = true;
		currentRarity = newRarity;

		for (int i = 0; i < tierToggles.Length; i++)
		{
			tierToggles[i].ButtonPressed = i > currentRarity + 1;
		}
		if (currentTier > currentRarity + 1)
			SwitchTier(currentRarity + 1);
	}


	private void SwitchRange(double _)
	{
		itemDirty = true;
	}

	string groupTemplate;
	Dictionary<string, Dictionary<string, StatChange>> changes = [];


	public void SetStatChanges(Dictionary<string, Dictionary<string, StatChange>> changes, string groupTemplate)
	{
		this.groupTemplate = groupTemplate;
		this.changes = changes;
		var keys = changes.Keys.ToArray();
		rarityToggles[0].Disabled = !keys.Any(k => k.Contains("_C_", StringComparison.OrdinalIgnoreCase));
		rarityToggles[1].Disabled = !keys.Any(k => k.Contains("_UC_", StringComparison.OrdinalIgnoreCase));
		rarityToggles[2].Disabled = !keys.Any(k => k.Contains("_R_", StringComparison.OrdinalIgnoreCase));
		rarityToggles[3].Disabled = !keys.Any(k => k.Contains("_VR_", StringComparison.OrdinalIgnoreCase));
		rarityToggles[4].Disabled = !keys.Any(k => k.Contains("_SR_", StringComparison.OrdinalIgnoreCase));

		tierToggles[0].Disabled = !keys.Any(k => k.Contains("_T01", StringComparison.OrdinalIgnoreCase));
		tierToggles[1].Disabled = !keys.Any(k => k.Contains("_T02", StringComparison.OrdinalIgnoreCase));
		tierToggles[2].Disabled = !keys.Any(k => k.Contains("_T03", StringComparison.OrdinalIgnoreCase));
		tierToggles[3].Disabled = !keys.Any(k => k.Contains("_T04", StringComparison.OrdinalIgnoreCase));
		tierToggles[4].Disabled = !keys.Any(k => k.Contains("_T05", StringComparison.OrdinalIgnoreCase));

		for (int i = rarityToggles.Length - 1; i >= 0; i--)
		{
			if (!rarityToggles[i].Disabled)
			{
				rarityToggles[i].ButtonPressed = true;
				SwitchRarity(i);
				for (int j = tierToggles.Length - 1; j >= 0; j--)
				{
					if (!tierToggles[j].Disabled && j <= i + 1)
					{
						SwitchTier(j);
						break;
					}
				}
				break;
			}
		}
		coreContainer.Visible = keys.Any(k => k.Contains("_Crystal", StringComparison.OrdinalIgnoreCase));
		SwitchCrystal(coreContainer.Visible);
		rangeSliderContainer.Visible = changes.Values.Any(cd => cd.Keys.Any(k => ranges.Any(r => k.EndsWith(r))));
		rangeSlider.Value = 0;

		UpdateChanges();
	}

	void UpdateChanges()
	{
		string tierReplacement = currentTier switch
		{
			0 => "_t01",
			1 => "_t02",
			2 => "_t03",
			3 => "_t04",
			4 => "_t05",
			_ => "_t00",
		};
		string coreReplacement = isCrystal ? "_crystal" : "_ore";
		string rarityReplacement = currentRarity switch
		{
			0 => "_c",
			1 => "_uc",
			2 => "_r",
			3 => "_vr",
			4 => "_sr",
			_ => "_c",
		};
		string finalItem = groupTemplate
			.Replace("{r}", rarityReplacement)
			.Replace("{c}", coreReplacement)
			.Replace("{t}", tierReplacement);
		currentItem.SetItem(GameItemTemplate.Get(finalItem)?.CreateInstance());

		//load stats
		if (!changes.TryGetValue(finalItem, out var templateChanges))
		{
			foreach (var changeNode in statChanges)
			{
				changeNode.Visible = false;
			}
			return;
		}
		SetDisplayChanges(templateChanges, (int)rangeSlider.Value);
	}

	public void ForceSetStats(string templateId, Dictionary<string, StatChange> changes, int range)
	{
		var template = GameItemTemplate.Get(templateId);
		currentItem.SetItem(template?.CreateInstance());
		SwitchRarity(template.RarityLevel - 1);
		SwitchTier(template.Tier - 1);
		SwitchCrystal(template["EvoType"]?.ToString() == "crystal");
		rangeSlider.Value = range;
		SetDisplayChanges(changes, range);
	}

	void SetDisplayChanges(Dictionary<string, StatChange> changes, int range)
	{
		var allowedRange = ranges[range];
		var changeArray = changes
			.Where(c =>
			{
				if (excludedStats.Contains(c.Key))
					return false;
				if (!rangeSliderContainer.Visible)
					return true;
				if (c.Key == "RngMax") //...why, epic?
					return range == 3;
				if (c.Key.Contains("KnockbackMagnitude"))
				{
					return (c.Key == range switch
					{
						0 => "KnockbackMagnitude",
						1 => "MidRangeKnockbackMagnitude",
						2 => "LongRangeKnockbackMagnitude",
						_ => null
					});
				}
				return !ranges.Any(r => c.Key.EndsWith(r)) || c.Key.EndsWith(allowedRange);
			})
			.OrderBy(c => statPriorityLookup.TryGetValue(c.Key, out var priority) ? -priority : 0)
			.ThenBy(c => statNameLookup.TryGetValue(c.Key, out var mappedName) ? mappedName : $"ZZZ{c.Key}")
			.ToArray();
		for (int i = 0; i < changeArray.Length; i++)
		{
			if (statChanges.Count <= i)
			{
				var newNode = statChangeScene.Instantiate<StatChangeEntry>();
				statChangeParent.AddChild(newNode);
				statChanges.Add(newNode);
			}
			var changeNode = statChanges[i];
			changeNode.Visible = true;
			changeNode.SetChange(changeArray[i]);
		}
		for (int i = changeArray.Length; i < statChanges.Count; i++)
		{
			statChanges[i].Visible = false;
		}
	}
}
