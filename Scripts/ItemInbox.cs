using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

public partial class ItemInbox : Control
{
	static Dictionary<string, List<GameItem.ItemReward>> itemInbox = [];

	static Dictionary<string, int> missionCooldowns = [];
	static Dictionary<string, string> inMission = [];
	static Dictionary<string, string> claimedMission = [];
	static Dictionary<string, bool> isClaimingMission = [];
	const int disabledCooldown = 300;
	const int missionInactiveCooldown = 90;
	const int missionActiveCooldown = 5;

	const string inboxFilePath = "user://inboxes.json";

	static ItemInbox()
	{
		RefreshTimerController.OnSecondChanged += OnSecondChanged;
		RefreshTimerController.OnHourChanged += OnHourChanged;
		if (FileAccess.FileExists(inboxFilePath))
		{
			using var inboxFile = FileAccess.Open(inboxFilePath, FileAccess.ModeFlags.Read);
			itemInbox = JsonSerializer.Deserialize<Dictionary<string, List<GameItem.ItemReward>>>(inboxFile.GetAsText());
		}
	}

	static void SaveInboxes()
	{
		using var inboxFile = FileAccess.Open(inboxFilePath, FileAccess.ModeFlags.Write);
		inboxFile.StoreString(JsonSerializer.Serialize(itemInbox));
	}

	public static bool HasInbox(GameAccount account) => account?.accountId is not null && itemInbox.ContainsKey(account.accountId);

	public static event Action InboxUpdated;
	[Export]
	Control inboxContainer;

	public override void _Ready()
	{
		GameAccount.ActiveAccountChanged += CheckAccount;
		InboxUpdated += CheckAccount;
		CheckAccount();
	}

	public override void _ExitTree()
	{
		GameAccount.ActiveAccountChanged -= CheckAccount;
		InboxUpdated -= CheckAccount;
	}

	private void CheckAccount()
	{
		inboxContainer?.Visible = itemInbox.ContainsKey(GameAccount.ActiveAccount.accountId);
	}

	public async void ShowInbox()
	{
		if (!itemInbox.Remove(GameAccount.ActiveAccount.accountId, out var inboxRewards))
			return;
		SaveInboxes();
		List<GameItem> inbox = [.. inboxRewards.Select(r => r.FindOrCreateReward(GameAccount.ActiveAccount))];
		inboxContainer?.Visible = false;
		foreach (var item in inbox)
		{
			item.GetSearchTags();
			item.GenerateRawData();
		}
		var toRecycle = await SimpleItemSelector.OpenMultiSelector(inbox, SimpleItemSelector.RecycleConfig with
		{
			title = "Automated Item Inbox",
			allowCancel = false,
			allowEmptySelection = true,
			unselectableMarkerTex = null,
			unselectableTintColor = Colors.Transparent,
		});
		var recycleIds = toRecycle.Select(item => item.uuid).Where(id => id is not null).ToArray();
		if (toRecycle.Length == 0)
			return;

		JsonObject recycleContent = new()
		{
			["targetItemIds"] = new JsonArray([.. recycleIds])
		};
		GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems).PerformOperation("RecycleItemBatch", recycleContent).StartTask();
		GD.Print($"Recycled {recycleIds.Length} rewards");
	}

	static void OnSecondChanged()
	{
		foreach (var account in GameAccount.OwnedAccounts)
		{
			var cooldown = missionCooldowns.GetValueOrDefault(account.accountId);
			if (cooldown < 0)
			{
				if (account.GetLocalData("autoClaimMissions")?.GetValue<bool>() != true)
				{
					cooldown = disabledCooldown;
				}
				else
				{
					cooldown = inMission.GetValueOrDefault(account.accountId) is not null ? missionActiveCooldown : missionInactiveCooldown;
					CheckMissionState(account);
				}
			}
			missionCooldowns[account.accountId] = cooldown - 1;
		}
	}

	static void OnHourChanged()
	{
		//check if free llamas are available
		//if so, claim on participating accounts
	}

	static async void CheckMissionState(GameAccount account)
	{
		if (isClaimingMission.GetValueOrDefault(account.accountId))
			return;
		if (inMission.GetValueOrDefault(account.accountId) is not null)
		{
			isClaimingMission[account.accountId] = true;
			var gotRewards = await ClaimMissionAndQuestRewards(account);
			isClaimingMission[account.accountId] = false;
			if (gotRewards)
			{
				claimedMission[account.accountId] = inMission[account.accountId];
				inMission[account.accountId] = null;
				missionCooldowns[account.accountId] = missionInactiveCooldown;
			}
		}
		//if (inMission.GetValueOrDefault(account.accountId) is not null)
		//	return;
		var matchResponse = await FnWebAddresses.FortGame
			.MakeRequest($"/fortnite/api/matchmaking/session/findPlayer/{account.accountId}")
			.SetAccount(account)
			.Send();
		if (await matchResponse.CheckForError())
			return;
		var matchDataList = await matchResponse.ReadJson<MatchData[]>();
		if (matchDataList.Length == 0 || matchDataList[0].attributes.Theater is null)
			return;
		var match = matchDataList.FirstOrDefault(m => m.attributes.Theater != "C4C2925C466832846A042E97F00FE5CF", matchDataList[0]);
		if (match.attributes.Theater == "C4C2925C466832846A042E97F00FE5CF")
		{
			inMission[account.accountId] = null;
			return;
		}
		if (claimedMission.GetValueOrDefault(account.accountId) == match.id)
			return;
		if(inMission.GetValueOrDefault(account.accountId) is null)
		{
			GD.Print($"Detected {account.DisplayName} in mission session {match.id}, starting reward check");
		}
		inMission[account.accountId] = match.id;
		missionCooldowns[account.accountId] = missionActiveCooldown;
	}

	struct MatchData
	{
		public string id { get; init; }
		public MatchAttributes attributes { get; init; }

		public struct MatchAttributes
		{
			[JsonPropertyName("THEATERID_s")]
			public string Theater { get; init; }
		}
	}

	static bool QuestPredicate(GameItem q) => q.QuestComplete && !q.QuestClaimed && !q.template.GetQuestRewards().Any(r => r.attributes?.ContainsKey("options") == true);
	static bool PackPredicate(GameItem p) => p.templateId.StartsWith("CardPack:zcp_") || p.templateId.StartsWith("CardPack:cardpack_cache_");

	static async Task<bool> ClaimMissionAndQuestRewards(GameAccount account)
	{
		var profile = account.GetProfile("campaign");
		await profile.Query(true);
		await Helpers.WaitForFrame();

		var questsToClaim = profile.GetItems("Quest", QuestPredicate);
		var packsToClaim = profile.GetItems("CardPack", PackPredicate);
		if (questsToClaim.Length > 0 || packsToClaim.Length > 0)
		{
			//GD.Print($"Last Query Result = {profile.lastOp}".FixNewlines());
			using var debugFile = FileAccess.Open("user://debugOp.json", FileAccess.ModeFlags.Write);
			debugFile.StoreString(JsonSerializer.Serialize(profile.lastOp));
			await Helpers.WaitForFrame();
			await profile.Query(true, true);//queries with revisions dont seem to update stats in these circumstances? either that or my profile system is broken...
		}
		bool unclaimedAlert = profile.statAttributes?["mission_alert_redemption_record"]?.AsObject().ContainsKey("pendingMissionAlertRewards") == true;

		if (questsToClaim.Length == 0 && packsToClaim.Length == 0 && unclaimedAlert)
			GD.Print("Claiming alert only");
		else if (questsToClaim.Length > 0 || packsToClaim.Length > 0)
			GD.Print($"Claiming {questsToClaim.Length} quests and {packsToClaim.Length} packs{(unclaimedAlert ? " plus alert rewards" : "")}");
		else
		{
			return false;
		}


		List<GameItem.ItemReward> rewards = [];

		for (int i = 0; i < questsToClaim.Length; i++)
		{
			rewards.AddRange((await questsToClaim[i].ClaimQuest() ?? []).Select(i => i.GameItemReward));
		}

		if (packsToClaim.Length > 0)
		{
			string[] cardpacksToOpen = [.. packsToClaim.Select(item => item.uuid)];
			foreach (var cardpackId in cardpacksToOpen)
			{
				var original = profile.GetItem(cardpackId.ToString());
				var resultNotification = (await profile.PerformOperation("OpenCardPack", new JsonObject() { ["cardPackItemId"] = cardpackId }))?.FirstOrDefault();
				Llamalytics.TryAddCardpack(original, resultNotification?.AsObject());
				if (resultNotification is null)
					continue;
				var rewardData = resultNotification["lootGranted"]["items"].Deserialize<GameItem.ItemReward[]>();
				var grouped = rewardData.GroupBy(r => r.itemType).Select(g => g.FirstOrDefault() with { quantity = g.Sum(r => r.quantity) });
				rewards.AddRange(grouped);
				//todo: add ventures level-up rewards 
			}
		}

		if (unclaimedAlert)
		{
			var notifs = await profile.PerformOperation("ClaimMissionAlertRewards");
			var alertRewards = notifs.FirstOrDefault(n => n["type"]?.ToString() == "missionAlertComplete");
			if (alertRewards is not null)
			{
				var rewardData = alertRewards["lootGranted"]["items"].Deserialize<GameItem.ItemReward[]>();
				rewards.AddRange(rewardData);
			}
			var ventureLevelUps = notifs.Where(n => n["type"]?.ToString() == "phoenixLevelUp").ToArray();
			foreach (var levelUp in ventureLevelUps)
			{
				var rewardData = levelUp["loot"]["lootGranted"]["items"].Deserialize<GameItem.ItemReward[]>();
				rewards.AddRange(rewardData);
				//todo: add ventures level-up rewards 
			}
		}

		if (rewards.Count == 0)
		{
			GD.Print("No rewards claimed");
			return false;
		}

		GD.Print($"Claimed {rewards.Count} rewards");
		if (!itemInbox.TryGetValue(account.accountId, out var inbox))
			itemInbox[account.accountId] = inbox = [];
		inbox.AddRange(rewards);
		SaveInboxes();
		GD.Print($"Inbox now at {inbox.Count} items");
		InboxUpdated?.Invoke();
		return true;
	}
}
