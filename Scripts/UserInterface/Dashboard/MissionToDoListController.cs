using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class MissionToDoListController : Control, IRecyclableElementProvider<MissionRewardPair>, IListProvider<MissionRewardPair>
{

	#region Static Stuff

	static DateTime lastKnownReset;
	static List<MissionRewardDataPair> targetMissionRewardData = [];
	static List<MissionRewardPair> targetMissionRewards = [];

	public static event Action OnToDoListChanged;
	static event Action OnToDoListUpdated; //this is different, responds to both standard changes and sorting changes
	static bool isInitialised;

	public static void InitialiseToDoList()
	{
		if (isInitialised)
			return;
		isInitialised = true;
		GameMission.OnMissionsUpdated += GenerateRewards;
		GameMission.OnMissionsInvalidated += ClearRewards;
		GameAccount.ActiveAccountChanged += LoadMissions;
		AppConfig.OnConfigChanged += OnConfigChanged;
		LoadMissions();
	}

	static void OnConfigChanged(string section, string key, JsonNode value)
	{
		if (section != "missions")
			return;
		if (key == "todo_sort_mode" || key == "sort_mode")
			UpdateList();
	}

	static bool CheckForNewDay()
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

	static bool TrimMissions()
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

	static void SaveMissions()
	{
		GameAccount.ActiveAccount.SetLocalData("missionToDoList", JsonSerializer.SerializeToNode(targetMissionRewardData));
		//TODO: save lite missions
	}

	static void LoadMissions()
	{
		//load and deserialise data list
		var localData = GameAccount.ActiveAccount.GetLocalData("missionToDoList")?.AsArray() ?? [];
		//TODO: load lite missions
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

	static void ClearRewards()
	{
		targetMissionRewards = [];
		UpdateList();
		OnToDoListChanged?.Invoke();
	}

	static void GenerateRewards()
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

	static int prevSort = 0;
	static void UpdateList()
	{
		CheckForNewDay();

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
		OnToDoListUpdated?.Invoke();
	}

	#endregion

	#region Public Static Stuff

	public static void MoveToTop(GameItem item) => Reorder(item, true);
	public static void MoveToBottom(GameItem item) => Reorder(item, false);
	static void Reorder(GameItem item, bool atTop)
	{
		var target = targetMissionRewards.FirstOrDefault(r => r.item == item);
		if (target.item == null)
			return;
		RemoveFromList(item);
		AddToList(target.mission, target.item, atTop);
	}

	public static void AddToList(GameMission mission, int rewardIndex, bool atTop = false)
	{
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
		if (CheckForNewDay())
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
			SaveMissions();
		}
		UpdateList();
		OnToDoListChanged?.Invoke();
	}

	public static void BulkAddToList(MissionRewardPair[] pairs)
	{
		bool update = false;
		if (CheckForNewDay())
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
			SaveMissions();
		}
		UpdateList();
		OnToDoListChanged?.Invoke();
	}

	static bool AddToListInternal(MissionRewardPair pair, bool atTop)
	{
		if (targetMissionRewards.Contains(pair))
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
			targetMissionRewards.Insert(0, pair);
			targetMissionRewardData.Insert(0, new(pair.mission.Guid, rewardIndex));
		}
		else
		{
			targetMissionRewards.Add(pair);
			targetMissionRewardData.Add(new(pair.mission.Guid, rewardIndex));
		}
		return true;
	}

	public static bool IsOnToDoList(GameItem item) =>
		item is not null && targetMissionRewards.Any(r => r.item == item) == true;

	public static bool IsOnToDoList(GameMission mission) =>
		mission is not null && targetMissionRewards.Any(r => r.mission == mission) == true;

	public static void RemoveFromList(GameMission mission)
	{
		if (mission is null)
			return;
		var targets = targetMissionRewards.RemoveAll(r => r.mission == mission);
		if (targets == 0)
			return;
		targetMissionRewardData.RemoveAll(r => r.missionGUID == mission.Guid);
		SaveMissions();
		UpdateList();
		OnToDoListChanged?.Invoke();
	}

	public static void RemoveFromList(GameItem item)
	{
		if (item is null)
		{
			GD.Print("item is null");
			return;
		}
		var target = targetMissionRewards.FirstOrDefault(r => r.item == item);
		if (target.item == null)
		{
			GD.Print("could not find pair");
			return;
		}
		var idx = Array.IndexOf(target.mission.allItems, target.item);
		targetMissionRewards.Remove(target);
		targetMissionRewardData.RemoveAll(r => r.missionGUID == target.mission.Guid && r.indexOfReward == idx);
		SaveMissions();
		UpdateList();
		OnToDoListChanged?.Invoke();
	}

	#endregion

	[Export]
	RecycleListContainer missionList;

	[Export]
	Node newMissionListNode;
	IListHandler newMissionList;

	[Export]
	VirtualTab tab;

	[Export]
	Control rootPanel;

	record struct MissionRewardDataPair(string missionGUID, int indexOfReward);
	public IList<MissionRewardPair> List => targetMissionRewards;

	public MissionRewardPair GetRecycleElement(int index) => index >= 0 && index < targetMissionRewards.Count ? targetMissionRewards[index] : default;

	public int GetRecycleElementCount() => targetMissionRewards.Count;

	public override void _Ready()
	{
		rootPanel?.Visible = false;
		missionList?.SetProvider(this);
		if (newMissionListNode is IListHandler listHandler)
		{
			newMissionList = listHandler;
			newMissionList?.LinkListProvider(this);
		}
		InitialiseToDoList();
		UpdateInstanceList();
		OnToDoListUpdated += UpdateInstanceList;
	}

	public override void _ExitTree()
	{
		OnToDoListUpdated -= UpdateInstanceList;
	}

	void UpdateInstanceList()
	{
		tab?.Text = $"To-Do List ({targetMissionRewards.Count})";
		rootPanel?.Visible = targetMissionRewards.Count > 0;
		missionList?.UpdateList(true);
		newMissionList?.UpdateList();
	}
}
