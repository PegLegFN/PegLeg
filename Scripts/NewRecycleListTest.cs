using Godot;
using System;

public partial class NewRecycleListTest : Node
{
	[Export]
	NewRecycleListContainer recycleList;
	EntryList<GameMission> missionList = [];
	public override void _Ready()
	{
		missionList.Clear();
		missionList.AddRange(GameMission.MissionList);
		recycleList.LinkListProvider(missionList);
		missionList.OnItemSelectedEvt += OnMissionSelected;
	}

	private void OnMissionSelected(GameMission mission, string context)
	{
		GD.Print("Selected: " + mission.DisplayName);
	}
}
