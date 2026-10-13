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
	static Dictionary<string, List<GameItem.ItemReward>> itemInbox => field ??= TryLoadInbox();
	static Dictionary<string, List<GameItem.ItemReward>> TryLoadInbox()
	{
		if (!FileAccess.FileExists(inboxFilePath))
			return [];
		using var inboxFile = FileAccess.Open(inboxFilePath, FileAccess.ModeFlags.Read);
		return JsonSerializer.Deserialize<Dictionary<string, List<GameItem.ItemReward>>>(inboxFile.GetAsText());
	}

	static Dictionary<string, int> missionCooldowns = [];
	static Dictionary<string, string> inMission = [];
	static Dictionary<string, string> claimedMission = [];
	static Dictionary<string, bool> isClaimingMission = [];
	const int disabledCooldown = 300;
	const int missionInactiveCooldown = 90;
	const int missionActiveCooldown = 5;
	static bool firstBoot = true;

	const string inboxFilePath = "user://inboxes.json";

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
		RefreshTimerController.OnSecondChanged += OnSecondChanged;
		RefreshTimerController.OnHourChanged += CheckForFreeLlamas;
		RefreshTimerController.OnDayChanged += CheckForShopResources;
		GameAccount.ActiveAccountChanged += CheckAccount;
		InboxUpdated += CheckAccount;
		CheckAccount();
		if (firstBoot)
		{
			firstBoot = false;
			CheckForFreeLlamas();
			CheckForShopResources();
		}
	}

	public override void _ExitTree()
	{
		RefreshTimerController.OnSecondChanged -= OnSecondChanged;
		RefreshTimerController.OnHourChanged -= CheckForFreeLlamas;
		RefreshTimerController.OnDayChanged -= CheckForShopResources;
		GameAccount.ActiveAccountChanged -= CheckAccount;
		InboxUpdated -= CheckAccount;
	}

	public override void _Input(InputEvent @event)
	{
		if(Bootstrap.IsEditor && @event.DevTextKeybindPressed(Key.G))
			CheckForShopResources();
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
		List<GameItem> inbox = [
			..inboxRewards
			.GroupBy(r => $"{r.itemType}:{r.quantity}")
			.Select(r => r.FirstOrDefault().FindOrCreateReward(GameAccount.ActiveAccount, new() { ["rewardStack"] = r.Count() }))
			.OrderBy(i => i.sortingTemplate?.Type switch {
				"AccountResource" when i.template.VBucksOrXRayTickets => -1,
				"Accolades" => 2,
				"AccountResource" => 1,
				_ => 0,
			})
			.ThenBy(i => i.sortingTemplate?.Type)
			.ThenByDescending(i => i.sortingTemplate?.RarityLevel ?? 0)
			.ThenBy(i => i.sortingTemplate?.SortingName)
		];
		InboxUpdated?.Invoke();
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
		if (!GameAccount.ActiveAccount.isOwned)
			return;
		foreach (var account in GameAccount.OwnedAccounts)
		{
			var cooldown = missionCooldowns.GetValueOrDefault(account.accountId);
			if (cooldown < 0)
			{
				bool checkMissions = account.GetLocalData("autoClaimMissions")?.GetValue<bool>() ?? false;
				if (!checkMissions)
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

	static readonly HashSet<string> freeLlamaExceptions = ["B9B0CE758A5049F898773C1A47A69ED4"]; //one time free llama
	static async void CheckForFreeLlamas()
	{
		if (!GameAccount.ActiveAccount.isOwned)
			return;
		var xrayStorefront = await GameStorefront.XRayLlamas.Fetch();
		var freeXrayOffers = xrayStorefront.Offers.Where(o => o.IsFree && !freeLlamaExceptions.Contains(o.OfferId)).ToArray();
		if (freeXrayOffers.Length == 0)
			return;
		foreach (var account in GameAccount.OwnedAccounts)
		{
			if (account.GetLocalData("autoClaimFreeLlamas")?.GetValue<bool>() != true)
				continue;
			var profile = account.GetProfile(FnProfileTypes.AccountItems);
			foreach (var offer in freeXrayOffers)
			{
				var limit = await account.GetStockLimit(offer);
				if (limit == 0)
					continue;

				List<GameItem.ItemReward> rewards = [];
				for (var i = 0; i < limit; i++)
				{
					await profile.PerformOperation("PopulatePrerolledOffers");
					var shopNotif = await account.PurchaseOffer(offer, 1);
					if (shopNotif is null)
						continue;
					rewards.AddRange(shopNotif
						.First(val => val["type"].ToString() == "CatalogPurchase")
						["lootResult"]["items"]
						.Deserialize<GameItem.ItemReward[]>()
					);
				}

				if (rewards.Count == 0)
					continue;

				GD.Print($"Claimed {limit} free Llamas with {rewards.Count} items");
				if (!itemInbox.TryGetValue(account.accountId, out var inbox))
					itemInbox[account.accountId] = inbox = [];
				inbox.AddRange(rewards);
				SaveInboxes();
				GD.Print($"Inbox of {account.DisplayName} now at {inbox.Count} items");
				InboxUpdated?.Invoke();
			}
		}
	}

	static async void CheckForShopResources()
	{
		if (!GameAccount.ActiveAccount.isOwned)
			return;
		GD.Print("Checking for auto-redeemable shop resources...");
		var weeklyStorefront = await GameStorefront.CampaignWeekly.Fetch();
		foreach (var account in GameAccount.OwnedAccounts)
		{
			if (account.GetLocalData("autoClaimWeeklyItems")?.GetValue<bool>() != true)
				continue;
			string autoClaimWeeklyFilter = account.GetLocalData("autoClaimWeeklyFilter")?.GetValue<string>() ?? "";
			var autoClaimSearchInstructions = PLSearch.GenerateSearchInstructions(autoClaimWeeklyFilter);
			var filteredOffers = weeklyStorefront.Offers
				.Where(o => 
					o.itemGrants.Length > 0 && o.itemGrants.All(i => 
						PLSearch.EvaluateInstructions(autoClaimSearchInstructions, i.RawData)
					)
				)
				.ToArray();

			List<GameItem.ItemReward> rewards = [];
			foreach (var offer in filteredOffers)
			{
				var limit = await account.GetPurchaseLimit(offer);
				if (limit == 0)
					continue;
				var item = offer.itemGrants[0];
				GD.Print($"Attempted to purchase {limit}x{item.quantity} {item.template.ItemName}");
				//continue;

				var shopNotif = await account.PurchaseOffer(offer, limit);
				if (shopNotif is null)
					continue;
				var shopResultItems = shopNotif
					.First(val => val["type"].ToString() == "CatalogPurchase")
					["lootResult"]["items"]
					.Deserialize<GameItem.ItemReward[]>();
				rewards.AddRange(shopResultItems);
			}

			if (rewards.Count == 0)
				continue;

			var remainingGold = account.GetProfile(FnProfileTypes.AccountItems).GetFirstTemplateItem("AccountResource:eventcurrency_scaling")?.quantity ?? 0;
			GD.Print($"Redeemed {filteredOffers.Length} offers with {rewards.Count} items, gold balance at {remainingGold}");
			if (!itemInbox.TryGetValue(account.accountId, out var inbox))
				itemInbox[account.accountId] = inbox = [];
			inbox.AddRange(rewards);
			SaveInboxes();
			GD.Print($"Inbox of {account.DisplayName} now at {inbox.Count} items");
			InboxUpdated?.Invoke();
		}
	}

	static async void CheckMissionState(GameAccount account)
	{
		if (
			account.GetLocalData("autoClaimMissions")?.GetValue<bool>() == true &&
			inMission.GetValueOrDefault(account.accountId) is not null &&
			!isClaimingMission.GetValueOrDefault(account.accountId)
		)
		{
			isClaimingMission[account.accountId] = true;
			var gotRewards = await ClaimMissionAndQuestRewards(account);
			isClaimingMission[account.accountId] = false;
			if (gotRewards)
			{
				claimedMission[account.accountId] = inMission[account.accountId];
				inMission[account.accountId] = null;
				missionCooldowns[account.accountId] = missionInactiveCooldown;
				OnMissionEnded();
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
			GD.Print($"Detected {account.DisplayName} in Hestia, ending reward check");
			if (inMission.GetValueOrDefault(account.accountId) is not null)
			{
				inMission[account.accountId] = null;
				OnMissionEnded();
			}
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

	static async void OnMissionEnded()
	{
		//auto dismantle new
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
		await profile.Query(ignoreCache: true);
		await Helpers.WaitForFrame();

		var questsToClaim = profile.GetItems("Quest", QuestPredicate);
		var packsToClaim = profile.GetItems("CardPack", PackPredicate);
		if (false && (questsToClaim.Length > 0 || packsToClaim.Length > 0))
		{
			//GD.Print($"Last Query Result = {profile.lastOp}".FixNewlines());
			using var debugFile = FileAccess.Open("user://debugOp.json", FileAccess.ModeFlags.Write);
			debugFile.StoreString(JsonSerializer.Serialize(profile.lastOp, Helpers.JsonOptions.Indented));
			await Helpers.WaitForFrame();
			await profile.Query(true, true);//queries with revisions dont seem to update stats in these circumstances? either that or my profile system is broken...
		}
		bool unclaimedAlert = profile.statAttributes?["mission_alert_redemption_record"]?.AsObject().ContainsKey("pendingMissionAlertRewards") == true;

		if (questsToClaim.Length == 0 && packsToClaim.Length == 0 && unclaimedAlert)
			GD.Print("Claiming alert only");
		else if (questsToClaim.Length > 0 || packsToClaim.Length > 0)
			GD.Print($"Claiming {questsToClaim.Length} quests and {packsToClaim.Length} packs{(unclaimedAlert ? " plus alert rewards" : "")}");
		else
			return false;


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
				var notifs = (await profile.PerformOperation("OpenCardPack", new JsonObject() { ["cardPackItemId"] = cardpackId }));
				var resultNotification = notifs?.FirstOrDefault();
				Llamalytics.TryAddCardpack(original, resultNotification?.AsObject());
				if (resultNotification is null)
					continue;
				var packRewardData = resultNotification["lootGranted"]["items"].Deserialize<GameItem.ItemReward[]>();
				var grouped = packRewardData.GroupBy(r => r.itemType).Select(g => g.FirstOrDefault() with { quantity = g.Sum(r => r.quantity) });
				rewards.AddRange(grouped);
				var ventureLevelUps = notifs.Where(n => n["type"]?.ToString() == "phoenixLevelUp").ToArray();
				foreach (var levelUp in ventureLevelUps)
				{
					var rewardData = levelUp["loot"]["lootGranted"]["items"].Deserialize<GameItem.ItemReward[]>();
					rewards.AddRange(rewardData);
				}
			}
		}

		for (int i = 0; i < 5; i++)
		{
			if (unclaimedAlert)
				break;
			await Helpers.WaitForTimer(1);
			await profile.Query(true, true); //Todo: fix weirdness with alerts
			unclaimedAlert = profile.statAttributes?["mission_alert_redemption_record"]?.AsObject().ContainsKey("pendingMissionAlertRewards") == true;
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
		GD.Print($"Inbox of {account.DisplayName} now at {inbox.Count} items");
		InboxUpdated?.Invoke();
		return true;
	}
}
