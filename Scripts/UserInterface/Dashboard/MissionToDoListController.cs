using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class MissionToDoListController : Control, IRecyclableElementProvider<GameMission>
{
    static MissionToDoListController inst;
    public static event Action OnToDoListUpdated;

    [Export]
	RecycleListContainer missionList;

    DateTime lastKnownReset;
    HashSet<string> targetGuids = [];
    List<GameMission> targetMissions = [];

    public GameMission GetRecycleElement(int index) => index >= 0 && index < targetMissions.Count ? targetMissions[index] : null;

    public int GetRecycleElementCount() => targetMissions.Count;

    public override void _Ready()
    {
        inst = this;
        missionList.SetProvider(this);
        GameMission.OnMissionsUpdated += UpdateMissions;
        GameMission.OnMissionsInvalidated += UpdateMissions;
        UpdateMissions();
    }

    public override void _ExitTree()
    {
        GameMission.OnMissionsUpdated -= UpdateMissions;
        GameMission.OnMissionsInvalidated -= UpdateMissions;
    }

	void UpdateMissions()
    {
        CheckForNewDay();
        targetMissions = [.. (GameMission.currentMissions ?? []).Where(m => targetGuids.Contains(m.missionData.missionGuid))];
        Name = $"To-Do List ({targetMissions.Count})";
        missionList.UpdateList(true);
    }

    bool CheckForNewDay()
    {
        bool result = false;
        if (lastKnownReset != GameMission.missionReset)
        {
            targetGuids.Clear();
            result = true;
        }
        lastKnownReset = GameMission.missionReset;
        return result;
    }

    public static void AddToList(GameMission mission)
    {
        if (mission?.missionData.missionGuid is null)
            return;
        if (inst.CheckForNewDay() || inst.targetGuids.Add(mission?.missionData.missionGuid))
        {
            inst.UpdateMissions();
            OnToDoListUpdated?.Invoke();
        }
    }

    public static bool IsOnToDoList(GameMission mission) =>
        inst?.targetGuids.Contains(mission?.missionData.missionGuid) == true;

    public static void RemoveFromList(GameMission mission)
    {
        if (inst.targetGuids.Remove(mission?.missionData.missionGuid))
        {
            inst.UpdateMissions();
            OnToDoListUpdated?.Invoke();
        }
    }
}
