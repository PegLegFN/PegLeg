using Godot;
using System;
using System.Linq;

public partial class MissionRewardEntry : Control, IRecyclableEntry
{
	[Signal]
	public delegate void MissionCompleteIfAlertEventHandler(bool complete);
	[Signal]
	public delegate void IsToDoEventHandler(bool todo);
	[Export]
	MissionEntry missionEntry;
	[Export]
	GameItemEntry itemEntry;
	public Control node => this;

	public override void _Ready()
	{
		MissionToDoListController.OnToDoListChanged += UpdateTodoState;
		missionEntry.MissionComplete += TryEmitComplete;
	}

	public override void _ExitTree()
	{
		MissionToDoListController.OnToDoListChanged -= UpdateTodoState;
	}

	private void UpdateTodoState()
	{
		EmitSignalIsToDo(MissionToDoListController.IsOnToDoList(itemEntry.currentItem));
	}

	bool knownCompleteState = false;

	private void TryEmitComplete(bool complete)
	{
		knownCompleteState = complete;
		EmitSignalMissionCompleteIfAlert(complete && itemEntry.currentItem?.template.Name.StartsWith("zcp_", StringComparison.OrdinalIgnoreCase) == false);
	}

	IRecyclableElementProvider<MissionRewardPair> provider;
	public void SetRecyclableElementProvider(IRecyclableElementProvider provider)
	{
		if (provider is IRecyclableElementProvider<MissionRewardPair> rewardProvider)
			this.provider = rewardProvider;
	}

	public void SetRecycleIndex(int index)
	{
		if (provider is null)
			return;
		var pair = provider.GetRecycleElement(index);
		missionEntry.SetMission(pair.mission);
		pair.item.SetRewardNotification();
		itemEntry.SetItem(pair.item);
		EmitSignalIsToDo(MissionToDoListController.IsOnToDoList(itemEntry.currentItem));
		TryEmitComplete(knownCompleteState);
	}

	public void AddToList()
	{
		bool test = missionEntry.currentMission.allItems.Contains(itemEntry.currentItem);
		MissionToDoListController.AddToList(missionEntry.currentMission, itemEntry.currentItem);
	}

	public void MoveToTop() => MissionToDoListController.MoveToTop(itemEntry.currentItem);

	public void MoveToBottom() => MissionToDoListController.MoveToBottom(itemEntry.currentItem);

	public void RemoveFromList()
	{
		MissionToDoListController.RemoveFromList(itemEntry.currentItem);
	}
}
