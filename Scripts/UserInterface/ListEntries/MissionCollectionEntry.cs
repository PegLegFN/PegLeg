using Godot;

public partial class MissionCollectionEntry : Control, IListEntry<MissionRewardSet>
{
	[Signal]
	public delegate void IsToDoEventHandler(bool todo);
	[Export]
	MissionEntry missionEntry;
	[Export]
	GameItemEntry[] itemEntries;

	MissionRewardSet currentSet;

	public override void _Ready()
	{
		MissionToDoListController.OnToDoListChanged += ToDoListChanged;
	}

	public override void _ExitTree()
	{
		MissionToDoListController.OnToDoListChanged -= ToDoListChanged;
	}

	private void ToDoListChanged()
	{
		EmitSignalIsToDo(IsFullyOnToDo());
	}

	public void AddToList()
	{
		foreach (var item in currentSet.items ?? [])
		{
			MissionToDoListController.AddToList(currentSet.mission, item);
		}
	}

	bool IsFullyOnToDo()
	{
		foreach (var item in currentSet.items ?? [])
		{
			if (!MissionToDoListController.IsOnToDoList(item))
				return false;
		}
		return true;
	}

	public void RemoveFromList()
	{
		foreach (var item in currentSet.items ?? [])
		{
			MissionToDoListController.RemoveFromList(item);
		}
	}

	void IListEntry.ClearListEntry()
	{
		currentSet = default;
		missionEntry.ClearMission();
		for (int i = 0; i < itemEntries.Length; i++)
		{
			itemEntries[i].Visible = false;
			itemEntries[i].ClearItem();
		}
	}

	int IListEntry<MissionRewardSet>.CurrentIndexTarget { get; set; }
	IListProvider<MissionRewardSet> IListEntry<MissionRewardSet>.CurrentListProvider { get; set; }
	void IListEntry<MissionRewardSet>.SetListEntryValue(MissionRewardSet newValue)
	{
		currentSet = newValue;
		int count = Mathf.Min(newValue.items.Length, itemEntries.Length);
		for (int i = 0; i < count; i++)
		{
			itemEntries[i].Visible = true;
			itemEntries[i].SetItem(newValue.items[i]);
		}
		for (int i = count; i < itemEntries.Length; i++)
		{
			itemEntries[i].Visible = false;
			itemEntries[i].ClearItem();
		}
		missionEntry.SetMission(newValue.mission);
		EmitSignalIsToDo(IsFullyOnToDo());
	}
}
