using Godot;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public partial class XpLimitController : Control
{
	[Export]
	XpLimitDisplay stwXpDisplay;
	[Export]
	Label playtimeXpLabel;
	[Export]
	XpLimitDisplay creativeXpDisplay;
	[Export]
	XpLimitDisplay superchargedXpDisplay;
	[Export]
	Godot.Range xpProgress;
	[Export]
	Label levelLabel;
	[Export]
	Label xpAmount;
	[Export]
	Label xpUntilMax;
	[Export]
	Control loading;
	[Export]
	Control content;
	[Export]
	Control superchargedContent;

	GameProfile stwProfile;
	GameProfile brProfile;

	public override void _Ready()
	{
		content.Visible = false;
		loading.Visible = true;
		GD.Print(GetPath());
		RefreshTimerController.OnHourChanged += UpdateProfiles;
		GameAccount.ActiveAccountChanged += UpdateAccount;
		UpdateAccount();
	}

	public override void _ExitTree()
	{
		RefreshTimerController.OnHourChanged -= UpdateProfiles;
		GameAccount.ActiveAccountChanged -= UpdateAccount;
	}

	private async void UpdateAccount()
	{
		loading.Visible = true;
		content.Visible = false;
		try
		{
			var acc = GameAccount.ActiveAccount;
			stwProfile = acc.GetProfile(FnProfileTypes.AccountItems);
			brProfile = acc.GetProfile(FnProfileTypes.CosmeticInventory);
			await acc.ClientQuestLoginAthena();


			await UpdateProfileTask();
		}
		finally
		{
			loading.Visible = false;
		}
	}

	private async void UpdateProfiles() => await UpdateProfileTask();

	private async Task FetchProfileTask()
	{
		//for some reason, XP stat changes don't increment the profile revision, so we need to force fetch the entire profile
		await Task.WhenAll(
			stwProfile.Query(ignoreCache: true),
			brProfile.Query(ignoreRevision: true),
			GameCalender.Check()
		);
		//temporary addon to handle daily valentines logins
		var questItems = brProfile.GetItems(item =>
			item.templateId.StartsWith("Quest:quest_s39_valentines_dailylogin_p0", StringComparison.OrdinalIgnoreCase) &&
			item.QuestComplete &&
			!item.QuestClaimed
		);
		foreach (var item in questItems)
		{
			GD.Print("Claimed valentines quest");
			await item.ClaimQuest();
		}
	}

	SemaphoreSlim updateProfileSemaphore = new(1);
	private async Task UpdateProfileTask()
	{
		loading.Visible = true;
		content.Visible = false;
		try
		{
			//if multiple XPLimits try updating at the same time, only the first will update the profiles
			if (updateProfileSemaphore.CurrentCount > 0)
			{
				using var _ = await updateProfileSemaphore.AwaitToken();
				await FetchProfileTask();
			}
			else
			{
				using var _ = await updateProfileSemaphore.AwaitToken();
			}

			UpdateXP();
			content.Visible = true;
		}
		finally
		{
			loading.Visible = false;
		}
	}

	void CheckForNewWeek()
	{
		if (stwReset < DateTime.Now || creativeReset < DateTime.Now)
			UpdateProfiles();
	}

	DateTime stwReset;
	DateTime creativeReset;

	void UpdateXP()
	{
		stwReset = DateTime.UtcNow.BRWeeklyRefresh();
		creativeReset = GameCalender.BRStartDate.AddDays((GameCalender.BRSeasonWeek + 1) * 7);
		//var playtimeLimit = PegLegResourceManager.MagicNumbers["playtimeXPLimit"].GetValue<int>();

		var stwXpItem = stwProfile.GetFirstTemplateItem("Token:stw_accolade_tracker");

		bool ignoreStwXp = (stwXpItem?.attributes["last_reset"]?.Deserialize<DateTime>() ?? default) < stwReset.AddDays(-7);
		//int? brWeek = GameCalender.BRSeasonWeek;
		//bool ignorePlaytimeXp = brWeek != brProfile.statAttributes["playtime_xp"]?["currentWeek"]?.GetValue<int?>();
		bool ignoreCreativeXp = GameCalender.BRSeasonWeek != (brProfile.statAttributes["creative_dynamic_xp"]?["currentWeek"]?.GetValue<int>() ?? 0);
		bool creativeUncapped = brProfile.statAttributes["creative_dynamic_xp"]?["weeklyExcessXpMult"]?.GetValue<double>() == 1.0;

		stwXpDisplay?.SetXpProgress(
			ignoreStwXp ? 0 : (stwXpItem?.attributes["weekly_xp"]?.GetValue<int?>() ?? 0),
			stwXpItem?.template["SoftWeeklyXPCap"].GetValue<int>() ?? 1,
			stwReset
		);
		//playtimeXpDisplay.SetXpProgress(
		//    ignorePlaytimeXp ? 0: (brProfile.statAttributes["playtime_xp"]?["currentWeekXp"]?.GetValue<int?>() ?? 0), 
		//    PegLegResourceManager.MagicNumbers["playtimeXPLimit"].GetValue<int>(),
		//    playtimeReset
		//);
		playtimeXpLabel.Text = (brProfile.statAttributes["playtime_xp"]?["currentWeekXp"]?.GetValue<int?>() ?? 0).Notate();
		creativeXpDisplay?.SetXpProgress(
			ignoreCreativeXp ? 0 : (brProfile.statAttributes?["creative_dynamic_xp"]?["currentWeekXp"]?.GetValue<int?>() ?? 0),
			creativeUncapped ? 0 : PegLegResourceManager.MagicNumbers["playtimeXPLimit"]?.GetValue<int>() ?? 0,
			creativeReset
		);

		int rested = brProfile.statAttributes["rested_xp"]?.GetValue<int?>() ?? 0;
		if (rested > 0 && superchargedXpDisplay is not null)
		{
			int restedMax = brProfile.statAttributes["rested_xp_cumulative"]?.GetValue<int?>() ?? 0;
			double restedMult = brProfile.statAttributes["rested_xp_mult"]?.GetValue<double?>() ?? 2; //i assume this defaults to 2 when not listed
			superchargedXpDisplay.SetXpProgress(
				rested,
				restedMax,
				null
			);
			//GD.Print($"Mult: {restedMult}");
			superchargedXpDisplay.TooltipText = $"The next {rested.Notate()} XP will be earned {restedMult:0.#}x faster than usual";
			superchargedContent.Visible = true;
		}
		else
		{
			if (superchargedContent is not null)
				superchargedContent.Visible = false;
		}

		var currentXP = brProfile.statAttributes["xp"]?.GetValue<int>() ?? 0;
		var currentLV = brProfile.statAttributes["level"]?.GetValue<int>() ?? 0;

		levelLabel.Text = currentLV.Notate();
		xpAmount.Text = currentXP.Notate();
		xpProgress.Value = (float)currentXP / 80000;

		var requiredXP = Mathf.Max(((200 - currentLV) * 80000) - currentXP, 0);

		xpUntilMax.Text = requiredXP.Compactify();
	}
}
