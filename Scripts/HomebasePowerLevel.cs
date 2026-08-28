using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

public partial class HomebasePowerLevel : Control
{
	[Export]
	bool useCurrent = true;
	[Export]
	Label homebaseNumberLabel;
	[Export]
	Range homebaseNumberProgressBar;
	[Export]
	bool ventures;
	[Export]
	bool animate = true;
	[Export]
	Color tooltipColor = Colors.Aquamarine;
	[Export]
	Control tempClaimContent;

	public override async void _Ready()
	{
		tempClaimContent?.Visible = false;
		ClearStats();
		homebaseNumberLabel.Text = "";
		if (useCurrent)
			TooltipText = "Waiting for data...";

		await Helpers.WaitForFrame();
		await Helpers.WaitForFrame();
		Size = Vector2.Zero;
		await Helpers.WaitForFrame();
		await Helpers.WaitForFrame();

		ClearStats();
		if (useCurrent)
		{
			TooltipText = "Waiting for data...";
			GameAccount.ActiveAccountChanged += OnActiveAccountChanged;
			OnActiveAccountChanged();
		}
	}

	void OnActiveAccountChanged() => SetAccount(GameAccount.ActiveAccount);

	public void SetAccount(GameAccount account)
	{
		if (currentAccount is not null)
		{
			if (ventures)
				currentAccount.OnVentureRatingDataChanged -= OnRatingChanged;
			else
			{
				currentAccount.OnRatingDataChanged -= OnRatingChanged;
				currentAccount.GetProfile("campaign").OnProfileChanged -= TempCheckForClaim;
			}

			currentAccount = null;
		}

		if (account.accountId == null)
		{
			ClearStats();
			return;
		}

		currentAccount = account;

		if (ventures)
			currentAccount.OnVentureRatingDataChanged += OnRatingChanged;
		else
		{
			currentAccount.OnRatingDataChanged += OnRatingChanged;
			currentAccount.GetProfile("campaign").OnProfileChanged += TempCheckForClaim;
			TempCheckForClaim();
		}

		UpdateStatsVisuals();
	}

	GameAccount currentAccount;

	void OnRatingChanged(GameAccount account) => UpdateStatsVisuals();

	Tween tintTween;
	private void UpdateStatsVisuals()
	{
		if (currentAccount is null)
			return;
		RatingData stats = ventures ? currentAccount.GetVentureRatingData() : currentAccount.GetRatingData();
		var newPowerLevel = stats.PowerLevel;
		TooltipText = CustomTooltip.GenerateSimpleTooltip(
			"Power Level",
			homebaseNumberLabel.Text,
			[
				$"{(ventures? "Venture" : "Homebase")} Power: {Mathf.Floor(newPowerLevel)}\n({Mathf.Floor((newPowerLevel % 1) * 100)}% progress to {Mathf.Floor(newPowerLevel) + 1})"
			],
			tooltipColor.ToHtml()
		);

		if (!animate)
		{
			AnimatedPowerLevel = newPowerLevel;
			return;
		}

		var targetColor = AnimatedPowerLevel < newPowerLevel ? Colors.Green : Colors.Red;
		homebaseNumberLabel.SelfModulate = targetColor;
		homebaseNumberProgressBar.SelfModulate = targetColor;
		if (tintTween?.IsValid() == true)
			tintTween.Kill();
		tintTween = CreateTween().SetParallel();
		tintTween.TweenProperty(homebaseNumberLabel, "self_modulate", Colors.White, 0.75);
		tintTween.TweenProperty(homebaseNumberProgressBar, "self_modulate", Colors.White, 0.75);
		tintTween.TweenProperty(this, "AnimatedPowerLevel", newPowerLevel, 0.75).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
	}

	float latestPowerLevel = 0;
	float AnimatedPowerLevel
	{
		get => latestPowerLevel;
		set
		{
			latestPowerLevel = value;
			homebaseNumberLabel.Text = Mathf.Floor(value).ToString();
			homebaseNumberProgressBar.Value = value % 1;
		}
	}

	void ClearStats()
	{
		if (animate)
		{
			if (tintTween?.IsValid() == true)
				tintTween.Kill();
			homebaseNumberLabel.SelfModulate = Colors.White;
			homebaseNumberProgressBar.SelfModulate = Colors.White;
		}

		latestPowerLevel = 0;
		homebaseNumberLabel.Text = "???";
		homebaseNumberProgressBar.Value = 0;
		TooltipText = "No Account";
	}

	public void TempCheckForClaim()
	{
		if (tempClaimContent is null)
			return;
		tempClaimContent.Visible = false;
		if (!currentAccount.isOwned)
			return;
		var profile = currentAccount.GetProfile("campaign");
		tempClaimContent.Visible = profile.GetFirstItem("Quest", QuestPredicate) is not null;
		tempClaimContent.Visible |= profile.GetFirstItem("CardPack", PackPredicate) is not null;
		tempClaimContent.Visible |= profile.statAttributes?["mission_alert_redemption_record"]?.AsObject().ContainsKey("pendingMissionAlertRewards") == true;
	}

	static bool QuestPredicate(GameItem q) => q.QuestComplete && !q.QuestClaimed && !q.template.GetQuestRewards().Any(r => r.attributes?.ContainsKey("options") == true);
	static bool PackPredicate(GameItem p) => p.templateId.StartsWith("CardPack:zcp_") || p.templateId.StartsWith("CardPack:cardpack_cache_");

	public async void TempClaimRewards()
	{
		var confirmation = await GenericConfirmationWindow.ShowConfirmation(
			"Claim Rewards?",
			contextText: "Claim Mission Alert Rewards and all non-choice quests?",
			warningText: "Quests with Choice Rewards can only be claimed from the Quests tab",
			postiveText: "Claim",
			negativeText: "Alert Only"
		);
		if (confirmation is null)
			return;
		using var loadingToken = LoadingOverlay.CreateToken();
		var profile = currentAccount.GetProfile("campaign");
		await profile.Query(true);

		var questsToClaim = confirmation == true ? profile.GetItems("Quest", QuestPredicate) : [];
		var packsToClaim = confirmation == true ? profile.GetItems("CardPack", PackPredicate) : [];
		bool unclaimedAlert = profile.statAttributes?["mission_alert_redemption_record"]?.AsObject().ContainsKey("pendingMissionAlertRewards") == true;
		var total = questsToClaim.Length + packsToClaim.Length + (unclaimedAlert ? 1 : 0);
		int progress = 0;

		if (questsToClaim.Length == 0 && packsToClaim.Length == 0 && unclaimedAlert)
			GD.Print("Claiming alert only");
		else if(questsToClaim.Length > 0 || packsToClaim.Length > 0)
			GD.Print($"Claiming {questsToClaim.Length} quests and {packsToClaim.Length} packs{(unclaimedAlert ? " plus alert rewards" : "")}");

		List<GameItem> rewards = [];

		for (int i = 0; i < questsToClaim.Length; i++)
		{
			rewards.AddRange(await questsToClaim[i].ClaimQuest() ?? []);
			progress++;
			loadingToken.SetLoadingProgress(progress, total);
		}

		if (packsToClaim.Length > 0)
		{
			string[] cardpacksToOpen = [.. packsToClaim.Select(item => item.uuid)];
			foreach (var cardpackId in cardpacksToOpen)
			{
				var resultNotification = (await profile.PerformOperation("OpenCardPack", new JsonObject() { ["cardPackItemId"] = cardpackId })).FirstOrDefault();
				//record in Llamalytics
				if (resultNotification is not null)
				{
					var rewardData = resultNotification["lootGranted"]["items"].Deserialize<GameItem.ItemReward[]>();
					var grouped = rewardData.GroupBy(r => r.itemType).Select(g => g.FirstOrDefault() with { quantity = g.Sum(r => r.quantity) });
					rewards.AddRange(grouped.Select(r => r.FindOrCreateReward(currentAccount)));
				}
				progress++;
			}
			loadingToken.SetLoadingProgress(progress, total);
		}

		if (unclaimedAlert)
		{
			var notifs = await profile.PerformOperation("ClaimMissionAlertRewards");
			if (notifs.FirstOrDefault() is JsonObject alertRewards)
			{
				var rewardData = alertRewards["lootGranted"]["items"].Deserialize<GameItem.ItemReward[]>();
				rewards.AddRange(rewardData.Select(r => r.FindOrCreateReward(currentAccount)));
			}
			progress++;
			loadingToken.SetLoadingProgress(progress, total);
		}
		loadingToken.Dispose();

		if (rewards.Count == 0)
		{
			GD.Print("No rewards claimed");
			return;
		}

		GD.Print($"Claimed {rewards.Count} rewards");
		foreach (var item in rewards)
		{
			item.GetSearchTags();
			item.GenerateRawData();
		}
		var toRecycle = await SimpleItemSelector.OpenMultiSelector(rewards, SimpleItemSelector.RecycleConfig with
		{
			title = "Mission/Quest Rewards",
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
			["targetItemIds"] = new JsonArray([..recycleIds])
		};
		GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems).PerformOperation("RecycleItemBatch", recycleContent).StartTask();
		GD.Print($"Recycled {recycleIds.Length} rewards");
	}

	public override void _ExitTree()
	{
		if (currentAccount is not null)
		{
			if (ventures)
				currentAccount.OnVentureRatingDataChanged -= OnRatingChanged;
			else
			{
				currentAccount.OnRatingDataChanged -= OnRatingChanged;
				currentAccount.GetProfile("campaign").OnProfileChanged -= TempCheckForClaim;
			}
		}
		if (useCurrent)
			GameAccount.ActiveAccountChanged -= OnActiveAccountChanged;
	}
}
