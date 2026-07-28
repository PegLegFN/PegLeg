using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class MissionToDoListController : Control, IRecyclableElementProvider<MissionRewardPair>, IListProvider<MissionRewardPair>
{
	static MissionToDoListController inst;
	public static event Action OnToDoListChanged;

	[Export]
	RecycleListContainer missionList;

	[Export]
	Node newMissionListNode;
	IListHandler newMissionList;

	[Export]
	VirtualTab tab;

	[Export]
	Control rootPanel;

	DateTime lastKnownReset;
	List<MissionRewardDataPair> targetMissionRewardData = [];
	List<MissionRewardPair> targetMissionRewards = [];

	public IList<MissionRewardPair> List => targetMissionRewards;

	record struct MissionRewardDataPair(string missionGUID, int indexOfReward);

	public MissionRewardPair GetRecycleElement(int index) => index >= 0 && index < targetMissionRewards.Count ? targetMissionRewards[index] : default;

	public int GetRecycleElementCount() => targetMissionRewards.Count;

	public override void _Ready()
	{
		inst = this;
		rootPanel?.Visible = false;
		missionList?.SetProvider(this);
		if(newMissionListNode is IListHandler listHandler)
		{
			newMissionList = listHandler;
			newMissionList?.LinkListProvider(this);
		}
		GameMission.OnMissionsUpdated += GenerateRewards;
		GameMission.OnMissionsInvalidated += ClearRewards;
		GameAccount.ActiveAccountChanged += LoadMissions;
		AppConfig.OnConfigChanged += OnConfigChanged;
		LoadMissions();
	}

	public override void _ExitTree()
	{
		GameMission.OnMissionsUpdated -= GenerateRewards;
		GameMission.OnMissionsInvalidated -= ClearRewards;
		GameAccount.ActiveAccountChanged -= LoadMissions;
		AppConfig.OnConfigChanged -= OnConfigChanged;
		inst = null;
	}

	private void OnConfigChanged(string section, string key, JsonNode value)
	{
		if (section != "missions")
			return;
		if (key == "todo_sort_mode" || key == "sort_mode")
			UpdateList();
	}

	bool TrimMissions()
	{
		if (targetMissionRewards.Count == 0)
			return false;
		var toRemove = targetMissionRewards
			.Where(r =>
				r.mission.alertRewardItems.Contains(r.item) &&
				r.mission.AlertIsCompleteFor(GameAccount.ActiveAccount)
			);
		if (AppConfig.Get("missions", "todo_trim_complete", false))
		{
			toRemove = toRemove
				.Union(targetMissionRewards
					.Where(r =>
						r.mission.alertRewardItems.Contains(r.item) &&
						r.mission.AlertIsCompleteFor(GameAccount.ActiveAccount)
					)
				)
				.Distinct();
		}
		var toRemoveArray = toRemove.ToArray();
		foreach (var pair in toRemoveArray)
		{
			var idx = Array.IndexOf(pair.mission.allItems, pair.item);
			targetMissionRewards.Remove(pair);
			targetMissionRewardData.RemoveAll(r => r.missionGUID == pair.mission.Guid && r.indexOfReward == idx);
		}
		return toRemoveArray.Length > 0;
	}

	void SaveMissions()
	{
		GameAccount.ActiveAccount.SetLocalData("missionToDoList", JsonSerializer.SerializeToNode(targetMissionRewardData));
	}

	void LoadMissions()
	{
		//load and deserialise data list
		var localData = GameAccount.ActiveAccount.GetLocalData("missionToDoList")?.AsArray() ?? [];
		try
		{
			targetMissionRewardData = localData.Deserialize<List<MissionRewardDataPair>>();
		}
		catch (Exception ex)
		{
			GD.PushWarning($"ToDo List error: \n{ex}");
		}
		GenerateRewards();
	}

	void ClearRewards()
	{
		targetMissionRewards = [];
		UpdateList();
		OnToDoListChanged?.Invoke();
	}

	void GenerateRewards()
	{
		var lookup = GameMission.MissionDict ?? [];
		targetMissionRewards = [.. targetMissionRewardData.Select(data =>
		{
			if (data.indexOfReward < 0 || !lookup.TryGetValue(data.missionGUID, out var mission) || mission.allItems.Length <= data.indexOfReward)
				return default;
			return new MissionRewardPair(mission, mission.allItems[data.indexOfReward]);
		}).Where(r => r.item is not null)];
		if (TrimMissions())
			SaveMissions();
		UpdateList();
		OnToDoListChanged?.Invoke();
	}


	int prevSort = 0;
	void UpdateList()
	{
		CheckForNewDay();
		tab?.Text = $"To-Do List ({targetMissionRewards.Count})";
		rootPanel?.Visible = targetMissionRewards.Count > 0;

		var todoSort = AppConfig.Get("missions", "todo_sort_mode", 0);
		if (todoSort == 1)
			todoSort = AppConfig.Get("missions", "sort_mode", 0);

		if (prevSort != 0 && todoSort == 0)
		{
			prevSort = 0;
			GenerateRewards();
			return;
		}
		prevSort = todoSort;

		if (todoSort != 0)
			targetMissionRewards = [.. MissionRewardsController.OrderByMode(targetMissionRewards, todoSort)];

		missionList?.UpdateList(true);
		newMissionList?.UpdateList();
	}

	bool CheckForNewDay()
	{
		try
		{
			if (GameMission.MissionList is null || lastKnownReset == GameMission.missionReset || lastKnownReset == default)
				return false;
			if (TrimMissions())
				SaveMissions();
			return true;
		}
		finally
		{
			if (GameMission.MissionList is not null)
				lastKnownReset = GameMission.missionReset;
		}
	}

	public static void MoveToTop(GameItem item) => Reorder(item, true);
	public static void MoveToBottom(GameItem item) => Reorder(item, false);
	static void Reorder(GameItem item, bool atTop)
	{
		var target = inst.targetMissionRewards.FirstOrDefault(r => r.item == item);
		if (target.item == null)
			return;
		RemoveFromList(item);
		AddToList(target.mission, target.item, atTop);
	}

	public static void AddToList(GameMission mission, int rewardIndex, bool atTop = false)
	{
		if (inst is null)
			return;
		if (mission?.Guid is not string guid)
		{
			//GD.Print("mission or GUID is null");
			return;
		}
		if (mission.allItems.Length <= rewardIndex || rewardIndex < 0)
		{
			//GD.Print("item index out of range: " + rewardIndex);
			return;
		}
		AddToList(new(mission, mission?.allItems[rewardIndex]), atTop);
	}
	public static void AddToList(GameMission mission, GameItem reward, bool atTop = false) => AddToList(new(mission, reward), atTop);
	public static void AddToList(MissionRewardPair pair, bool atTop = false)
	{
		bool update = false;
		if (inst.CheckForNewDay())
		{
			GD.Print("todo list addition aborted, new day");
			update = true;
		}
		if (!update)
		{
			update = AddToListInternal(pair, atTop);
			if (!update)
			{
				//GD.Print("list already contains pair");
				return;
			}
			inst.SaveMissions();
		}
		inst.UpdateList();
		OnToDoListChanged?.Invoke();
	}

	public static void BulkAddToList(MissionRewardPair[] pairs)
	{
		bool update = false;
		if (inst.CheckForNewDay())
		{
			GD.Print("todo list addition aborted, new day");
			update = true;
		}
		if (!update)
		{
			foreach (var item in pairs)
			{
				update |= AddToListInternal(item, true);
			}
			if (!update)
			{
				//GD.Print("list already contains pair");
				return;
			}
			inst.SaveMissions();
		}
		inst.UpdateList();
		OnToDoListChanged?.Invoke();
	}

	static bool AddToListInternal(MissionRewardPair pair, bool atTop)
	{
		if (inst.targetMissionRewards.Contains(pair))
			return false;
		if (pair.mission?.Guid is not string guid)
		{
			//GD.Print("mission or GUID is null");
			return false;
		}
		if (
			AppConfig.Get("missions", "todo_trim_complete", false) &&
			pair.mission.alertRewardItems.Contains(pair.item) &&
			pair.mission.AlertIsCompleteFor(GameAccount.ActiveAccount)
		)
		{
			//GD.Print("mission alert already claimed");
			return false;
		}
		int rewardIndex = Array.IndexOf(pair.mission.allItems, pair.item);
		if (rewardIndex == -1)
		{
			//GD.Print("item not in mission");
			return false;
		}
		if (atTop)
		{
			inst.targetMissionRewards.Insert(0, pair);
			inst.targetMissionRewardData.Insert(0, new(pair.mission.Guid, rewardIndex));
		}
		else
		{
			inst.targetMissionRewards.Add(pair);
			inst.targetMissionRewardData.Add(new(pair.mission.Guid, rewardIndex));
		}
		return true;
	}

	public static bool IsOnToDoList(GameItem item) =>
		item is not null && inst?.targetMissionRewards.Any(r => r.item == item) == true;

	public static bool IsOnToDoList(GameMission mission) =>
		mission is not null && inst?.targetMissionRewards.Any(r => r.mission == mission) == true;

	public static void RemoveFromList(GameMission mission)
	{
		if (inst is null || mission is null)
			return;
		var targets = inst.targetMissionRewards.RemoveAll(r => r.mission == mission);
		if (targets == 0)
			return;
		inst.targetMissionRewardData.RemoveAll(r => r.missionGUID == mission.Guid);
		inst.SaveMissions();
		inst.UpdateList();
		OnToDoListChanged?.Invoke();
	}

	public static void RemoveFromList(GameItem item)
	{
		if (inst is null)
			return;
		if (item is null)
		{
			GD.Print("item is null");
			return;
		}
		var target = inst.targetMissionRewards.FirstOrDefault(r => r.item == item);
		if (target.item == null)
		{
			GD.Print("could not find pair");
			return;
		}
		var idx = Array.IndexOf(target.mission.allItems, target.item);
		inst.targetMissionRewards.Remove(target);
		inst.targetMissionRewardData.RemoveAll(r => r.missionGUID == target.mission.Guid && r.indexOfReward == idx);
		inst.SaveMissions();
		inst.UpdateList();
		OnToDoListChanged?.Invoke();
	}
}
