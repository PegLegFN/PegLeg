using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using static System.Collections.Specialized.BitVector32;

public partial class MissionRewardEntry : Control, IRecyclableEntry, IListEntry<MissionRewardPair>
{
	[Signal]
	public delegate void MissionCompleteIfAlertEventHandler(bool complete);
	[Signal]
	public delegate void IsToDoEventHandler(bool todo);

	[Export]
	public bool ignoreLevelCap = false;
	[Export]
	MissionEntry missionEntry;
	[Export]
	GameItemEntry itemEntry;
	[Export]
	Label levelLabel;
	[Export]
	Label missionPowerLabel;
	[Export]
	Control todoReorderContent;
	public Control node => this;

	int _ListEntryIndex;
	IListProvider<MissionRewardPair> _ListEntryItemProvider;
	int IListEntry<MissionRewardPair>.CurrentIndexTarget { get => _ListEntryIndex; set => _ListEntryIndex = value; }
	IListProvider<MissionRewardPair> IListEntry<MissionRewardPair>.CurrentListProvider { get => _ListEntryItemProvider; set => _ListEntryItemProvider = value; }

	public override void _Ready()
	{
		todoReorderContent?.Visible = AppConfig.Get("missions", "todo_sort_mode", 0) == 0;
		AppConfig.OnConfigChanged += OnConfigChanged;
		MissionToDoListController.OnToDoListChanged += UpdateTodoState;
		missionEntry.MissionComplete += TryEmitComplete;
	}

	public override void _ExitTree()
	{
		AppConfig.OnConfigChanged -= OnConfigChanged;
		MissionToDoListController.OnToDoListChanged -= UpdateTodoState;
	}

	private void OnConfigChanged(string section, string key, JsonNode value)
	{
		if (section == "missions" && key == "todo_sort_mode" && todoReorderContent is not null)
			todoReorderContent.Visible = AppConfig.Get("missions", "todo_sort_mode", 0) == 0;
	}

	private void UpdateTodoState()
	{
		EmitSignalIsToDo(currentItems.All(MissionToDoListController.IsOnToDoList));
	}

	bool knownCompleteState = false;

	private void TryEmitComplete(bool complete)
	{
		knownCompleteState = complete;
		EmitSignalMissionCompleteIfAlert(complete && itemEntry?.currentItem?.template?.Name.StartsWith("zcp_", StringComparison.OrdinalIgnoreCase) == false);
	}

	IRecyclableElementProvider<MissionRewardPair> provider;
	public void SetRecyclableElementProvider(IRecyclableElementProvider provider)
	{
		if (provider is IRecyclableElementProvider<MissionRewardPair> rewardProvider)
			this.provider = rewardProvider;
	}

	public void ClearReward()
	{
		missionEntry?.ClearMission();
		itemEntry?.ClearItem();
		currentItems = [];
		if (levelLabel is null || missionPowerLabel is null)
			return;
		levelLabel.Visible = false;
		missionPowerLabel.Visible = false;
	}

	GameItem[] currentItems = [];
	//assumes items are of the same type. expects 1-2 items, can minimally support 3+
	public void SetRewardInfoManually(GameMission mission, GameItem[] items)
	{
		currentItems = items;
		GameItem mainItem = items.FirstOrDefault();
		missionEntry.SetMission(mission);
		mainItem.SetRewardNotification();
		itemEntry?.SetItem(mainItem);
		if (levelLabel is null || missionPowerLabel is null)
			return;
		if (items.Length <= 1 || items.Any(i => i.customData.ContainsKey("fools")))
		{
			//single level
			missionPowerLabel.Text = mission.PowerLevel.ToString();
			levelLabel.Text = mainItem?.template?.CanBeLeveled == true ? $"Lv {(ignoreLevelCap ? mainItem.DesiredLevel : mainItem.ResolveDesiredLevel())}" : (items[0].quantity > 1 ? $"x{items[0].quantity}" : "");
		}
		else
		{
			//double level
			missionPowerLabel.Text = mission.PowerLevel.ToString()+" x2";
			levelLabel.Text = mainItem?.template?.CanBeLeveled == true ? $"Lv {(ignoreLevelCap ? mainItem.DesiredLevel : mainItem.ResolveDesiredLevel())}\nLv {(ignoreLevelCap ? items[1].DesiredLevel : items[1].ResolveDesiredLevel())}" : "";
		}
		missionPowerLabel.Visible = true;
		levelLabel.Visible = !string.IsNullOrWhiteSpace(levelLabel.Text);
	}

	void IListEntry<MissionRewardPair>.SetListEntryValue(MissionRewardPair newValue) => SetPair(newValue);

	public void SetRecycleIndex(int index)
	{
		if (provider is null)
			return;
		SetPair(provider.GetRecycleElement(index));
	}

	void SetPair(MissionRewardPair pair)
	{
		missionEntry.SetMission(pair.mission);
		pair.item.SetRewardNotification();
		itemEntry.SetItem(pair.item);
		currentItems = [pair.item];
		EmitSignalIsToDo(MissionToDoListController.IsOnToDoList(itemEntry.currentItem));
		TryEmitComplete(knownCompleteState);
	}

	public void AddToList()
	{
		foreach (var item in currentItems)
		{
			MissionToDoListController.AddToList(missionEntry.currentMission, item);
		}
	}

	public void MoveToTop() => MissionToDoListController.MoveToTop(itemEntry.currentItem);

	public void MoveToBottom() => MissionToDoListController.MoveToBottom(itemEntry.currentItem);

	public void RemoveFromList()
	{
		foreach (var item in currentItems)
		{
			MissionToDoListController.RemoveFromList(itemEntry.currentItem);
		}
	}
}
