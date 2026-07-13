using Godot;
using System;
using System.Linq;

public partial class QuestNode : Control
{
	[Signal]
	public delegate void NotificationVisibleEventHandler(bool visible);
	[Signal]
	public delegate void PinnedVisibleEventHandler(bool visible);
	[Signal]
	public delegate void KeyItemVisibleEventHandler(bool visible);
	[Signal]
	public delegate void ColorChangedEventHandler(Color color);
	[Signal]
	public delegate void PressedEventHandler();

	[Export(PropertyHint.ArrayType)]
	Color[] colorStages = [];
	[Export]
	Control flagParent;
	[Export]
	Control[] flags = [];
	[Export]
	CheckButton selectedToggle;
	[Export]
	GameItemEntry questItemEntry;
	[Export]
	Godot.Range progressDisplay;
	QuestSlot questData;
	bool displayAsLocked;

	//static readonly FrozenSet<string> keyItemTemplates = new string[]
	//{
	//    "AccountResource:reagent_alteration_gameplay_generic",
	//    "AccountResource:reagent_promotion_survivors",
	//    "AccountResource:reagent_promotion_heroes",
	//    "AccountResource:reagent_promotion_weapons",
	//    "AccountResource:reagent_promotion_traps",
	//}.ToFrozenSet(StringComparer.InvariantCultureIgnoreCase);

	public void SetupQuestNode(QuestSlot newQuestData, ButtonGroup buttonGroup, bool displayAsLocked)
	{
		this.displayAsLocked = displayAsLocked;
		selectedToggle.ButtonGroup = buttonGroup;
		questData?.OnPropertiesUpdated -= RefreshQuestNode;
		questData = newQuestData;
		questData.OnPropertiesUpdated += RefreshQuestNode;

		foreach (var flag in flags)
		{
			flag.Visible = false;
		}

		RefreshQuestNode();
	}

	void RefreshQuestNode()
	{
		questItemEntry.SetItem(questData.isUnlocked ? questData.questItem : questData.questTemplate.CreateInstance());
		EmitSignal(SignalName.NotificationVisible, questData.isNew && !questData.isClaimed);

		if(progressDisplay is not null)
		{
			progressDisplay.Visible = questData.isActive;
			if (questData.isActive)
			{
				var objectives = questData.questTemplate["Objectives"]?.AsArray() ?? [];
				double progress = 0;
				foreach (var objective in objectives)
				{
					var name = objective["BackendName"].ToString();
					var target = objective["Count"]?.GetValue<int>() ?? 1;
					var objProgress = questData.questItem?.attributes?["completion_" + name]?.GetValue<int>() ?? 0;
					progress += ((float)objProgress / target) / objectives.Count;
				}
				progressDisplay.Value = progress;
			}
		}

		var rewards = questData.questTemplate.GetQuestRewards().Select(r => r.templateId);
		SetFlags(
			questData.isPinned,
			rewards.Contains("AccountResource:reagent_alteration_gameplay_generic"),
			rewards.Any(r => r.StartsWith("AccountResource:reagent_promotion", StringComparison.InvariantCultureIgnoreCase)),
			rewards.Contains("AccountResource:voucher_herobuyback") || rewards.Contains("AccountResource:voucher_item_buyback"),
			rewards.Any(r => r.StartsWith("schematic:", StringComparison.InvariantCultureIgnoreCase)),
			rewards.Any(r => r.StartsWith("hero:", StringComparison.InvariantCultureIgnoreCase))
		);

		int colorIndex = 0;
		if (questData.isClaimed)
			colorIndex = 3;
		else if (questData.isCompleted)
			colorIndex = 2;
		else if (displayAsLocked)
			colorIndex = 0;
		else if (questData.isUnlocked)
			colorIndex = 1;
		EmitSignal(SignalName.ColorChanged, colorStages[colorIndex]);
	}
	
	void SetFlags(params bool[] flagConditions)
	{
		flagParent.Visible = false;
		if (!flagConditions.Any(b => b))
			return;
		for (int i = 0; i < flagConditions.Length; i++)
		{
			flags[i].Visible = flagConditions[i];
		}
		Helpers.Defer(() => flagParent.Visible = true);
	}

	public void Press()
	{
		selectedToggle.ButtonPressed = true;
		EmitSignal(SignalName.Pressed);
		EmitSignal(SignalName.NotificationVisible, false);
	}

	public override void _ExitTree()
	{
		questData?.OnPropertiesUpdated -= RefreshQuestNode;
	}
}
