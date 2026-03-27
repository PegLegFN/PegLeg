using Godot;
using System;
using System.Linq;

public partial class DashboardSuperchargerController : Control
{
	[Export]
	Control content;
	[Export]
	GameItemEntry entry;
	[Export]
	Control noSuperchargerMessage;
	[Export]
	Control checkmark;
	[Export]
	Control loading;
	[Export]
	ProgressBar progressBar;
	[Export]
	bool onlyShowOnResetDay = false;
	[Export]
	bool tryUseSummaryFallback = false;

	public override void _Ready()
	{
		GameAccount.ActiveAccountChanged += AccountChanged;
		RefreshTimerController.OnDayChanged += AccountChanged;
		entry.ClearItem();
		loading.Visible = true;
		AccountChanged();
	}

	public override void _ExitTree()
	{
		GameAccount.ActiveAccountChanged -= AccountChanged;
		RefreshTimerController.OnDayChanged -= AccountChanged;
	}

	private async void AccountChanged()
	{
		bool isRefreshDay = (Timeline.EndOfCurrentWeek - DateTime.UtcNow).TotalDays > 6.1;
		if (onlyShowOnResetDay)
		{
			Visible = false;
			if (!isRefreshDay)
				return;
		}
		entry.ClearItem();
		content.Visible = false;
		checkmark.Visible = false;
		loading.Visible = true;
		noSuperchargerMessage.Visible = false;

		var targetAccount = GameAccount.ActiveAccount;
		if (tryUseSummaryFallback && AppConfig.TryGet("automation", "summary_160_fallback", out string accountId))
		{
			targetAccount = GameAccount.GetOrCreateAccount(accountId);
			await Helpers.WaitForTimer(5);
		}

		await Helpers.WaitForFrame();
		await Helpers.WaitForFrame();
		var profile = await GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems).Query();
		var possibleQuest = profile.GetFirstItem("Quest", q => q.templateId.StartsWith("Quest:weekly_elder"));
		GD.Print(possibleQuest?.template?.DisplayName ?? "NoSupercharger");


		if (onlyShowOnResetDay)
		{
			Visible = isRefreshDay && possibleQuest is not null;
		}

		loading.Visible = false;
		checkmark.Visible = possibleQuest?.QuestComplete ?? false;
		content.Visible = possibleQuest is not null;
		noSuperchargerMessage.Visible = possibleQuest is null;
		entry.SetItem(possibleQuest?.template?.GetVisibleQuestRewards()?.FirstOrDefault());
		if (possibleQuest is not null && progressBar is not null)
		{
			var objective = possibleQuest.template["Objectives"][0].AsObject();
			progressBar.Value = (possibleQuest.attributes[objective["BackendName"].ToString()]?.GetValue<int>() ?? 0) / (float)objective["Count"].GetValue<int>();
		}
	}
}
