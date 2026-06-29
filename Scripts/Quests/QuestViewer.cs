using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class QuestViewer : Control
{
	[Signal]
	public delegate void NameChangedEventHandler(string name);
	[Signal]
	public delegate void IconChangedEventHandler(Texture2D icon);
	[Signal]
	public delegate void DescriptionChangedEventHandler(string description);
	[Signal]
	public delegate void CompleteVisibleEventHandler(bool visible);
	[Signal]
	public delegate void QuestUpdatedEventHandler();

	[Export]
	Control objectiveParent;
	[Export]
	PackedScene objectiveScene;
	List<QuestObjective> objectiveEntries = [];

	[Export]
	CheckButton pinButton;
	[Export]
	Button rerollButton;

	[Export]
	Control rewardParent;
	[Export]
	PackedScene rewardScene;
	List<GameItemEntry> rewardEntries = [];

	[Export]
	Button claimButton;
	[Export]
	OptionButton claimRewardSelector;

	public override void _Ready()
	{
		claimButton.Visible = false;
		claimRewardSelector.Visible = false;

		claimButton.Pressed += ClaimQuest;
		claimRewardSelector.ItemSelected += _ => claimButton.Disabled = claimRewardSelector.Selected <= 1;
		pinButton.Pressed += UpdatePinnedState;
		rerollButton.Pressed += RerollQuest;
	}

	static readonly string[] cardPackFromRarity =
	[
		"CardPack:cardpack_choice_all_r",
		"CardPack:cardpack_choice_all_r",
		"CardPack:cardpack_choice_all_r",
		"CardPack:cardpack_choice_all_r",
		"CardPack:cardpack_choice_all_vr",
		"CardPack:cardpack_choice_all_sr",
	];

	QuestSlot currentQuest;
	async void UpdatePinnedState()
	{
		var account = GameAccount.ActiveAccount;
		if (
			currentQuest is null ||
			!currentQuest.isUnlocked ||
			currentQuest.isClaimed ||
			currentQuest.questItem?.profile?.account != account
			)
			return;

		await currentQuest.questItem.SetPinnedAsync(pinButton.ButtonPressed);
		pinButton.ButtonPressed = currentQuest.isPinned;
	}

	async void RerollQuest()
	{
		var account = GameAccount.ActiveAccount;
		if (currentQuest is null ||
			!currentQuest.isUnlocked ||
			currentQuest.questItem?.profile?.account != account
			)
			return;

		using var _ = LoadingOverlay.CreateToken();

		var newQuest = await account.RerollQuest(currentQuest.questItem);
		if (newQuest == null)
			return;

		currentQuest.LinkQuestItem(newQuest);
		SetupQuest(currentQuest);
		rerollButton.Visible = account.CanRerollQuest();
	}

	async void ClaimQuest()
	{
		var account = GameAccount.ActiveAccount;
		if (currentQuest is null ||
			!currentQuest.isCompleted ||
			currentQuest.questItem?.profile?.account != account
			)
			return;

		GameItem[] rewards = [];
		using (LoadingOverlay.CreateToken())
		{
			int idx = claimRewardSelector.Visible ? claimRewardSelector.Selected - 2 : 0;
			rewards = await currentQuest.questItem.ClaimQuest(idx);

			SetupQuest(currentQuest);
		}

		if (rewards.Length > 0)
		{
			foreach (var item in rewards)
			{
				item.GetSearchTags();
				item.GenerateRawData();
			}
			var toRecycle = await SimpleItemSelector.OpenMultiSelector(rewards, SimpleItemSelector.RecycleConfig with
			{
				allowCancel = false,
				allowEmptySelection = true,
				unselectableMarkerTex = null,
				unselectableTintColor = Colors.Transparent,
			});
			var recycleIds = toRecycle.Select(item => (JsonNode)item.uuid).Where(id => id is not null).ToArray();
			if (toRecycle.Length > 0)
			{
				JsonObject content = new()
				{
					["targetItemIds"] = new JsonArray(recycleIds)
				};
				GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems).PerformOperation("RecycleItemBatch", content).StartTask();
			}
		}
	}

	public void SetupQuest(QuestSlot quest)
	{
		currentQuest = quest;
		EmitSignal(SignalName.NameChanged, quest.questTemplate.DisplayName);
		EmitSignal(SignalName.DescriptionChanged, quest.questTemplate.Description);
		EmitSignal(SignalName.IconChanged, quest.questTemplate.GetTexture());
		EmitSignal(SignalName.CompleteVisible, quest.isClaimed);

		rerollButton.Visible = quest.isRerollable;
		pinButton.Visible = quest.isUnlocked && !quest.isClaimed;
		pinButton.ButtonPressed = quest.isPinned;

		var rewards = quest.questTemplate.GetVisibleQuestRewards();

		rewardParent.Visible = false;
		for (int i = 0; i < rewards.Length; i++)
		{
			if (i >= rewardEntries.Count)
			{
				var newEntry = rewardScene.Instantiate<GameItemEntry>();
				rewardParent.AddChild(newEntry);
				rewardEntries.Add(newEntry);
			}
			rewardEntries[i].SetItem(rewards[i]);
			rewards[i].SetRewardNotification();
			rewardEntries[i].Visible = true;
		}

		for (int i = rewards.Length; i < rewardEntries.Count; i++)
		{
			rewardEntries[i].Visible = false;
		}
		rewardParent.Visible = true;

		claimButton.Visible = quest.isCompleted;
		claimButton.Disabled = false;
		claimRewardSelector.Visible = false;
		if (quest.isCompleted && rewards.FirstOrDefault(i => i.templateId.StartsWith("CardPack:") && i.attributes?.ContainsKey("options") == true) is GameItem choicePack)
		{
			var options = choicePack.attributes["options"].Deserialize<GameItem.ItemReward[]>();
			claimRewardSelector.Visible = true;
			claimButton.Disabled = true;
			claimRewardSelector.Clear();
			claimRewardSelector.AddItem("Select Reward");
			claimRewardSelector.AddSeparator();
			foreach (var item in options)
			{
				var template = GameItemTemplate.Get(item.itemType);
				var name = template?.DisplayName ?? item.itemType;
				claimRewardSelector.AddItem($"{name}{(item.quantity <= 1 ? "" : $" x{item.quantity}")}");
			}
		}

		var objectives = quest.questTemplate["Objectives"].AsArray();
		for (int i = 0; i < objectives.Count; i++)
		{
			if (i >= objectiveEntries.Count)
			{
				var newEntry = objectiveScene.Instantiate<QuestObjective>();
				objectiveParent.AddChild(newEntry);
				objectiveEntries.Add(newEntry);
			}
			var objective = objectives[i].AsObject();
			if (
				objective["Hidden"]?.GetValue<bool>() ?? false ||
				(
					string.IsNullOrWhiteSpace(objective["Description"]?.ToString()) &&
					string.IsNullOrWhiteSpace(objective["HudShortDescription"]?.ToString())
				)
			)
			{
				objectiveEntries[i].Visible = false;
				continue;
			}
			int currentProgress = quest.isUnlocked ? (quest.questItem.attributes["completion_" + objective["BackendName"].ToString().ToLower()]?.GetValue<int>() ?? 0) : 0;
			objectiveEntries[i].SetupObjective(objective, currentProgress);
			objectiveEntries[i].Visible = true;
		}
		for (int i = objectives.Count; i < objectiveEntries.Count; i++)
		{
			objectiveEntries[i].Visible = false;
		}
	}
}
