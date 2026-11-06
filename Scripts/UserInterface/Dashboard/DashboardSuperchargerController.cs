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

	GameItem currentQuest;

	private async void AccountChanged()
	{
		entry.ClearItem();
		content.Visible = false;
		checkmark.Visible = false;
		loading.Visible = true;
		noSuperchargerMessage.Visible = false;

		if (!GameAccount.ActiveAccount.isOwned)
		{
			loading.Visible = false;
			SetQuest(null);
			return;
		}

		var profile = await GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems).Query();
		var possibleQuest = profile.GetFirstItem("Quest", q => q.templateId.StartsWith("Quest:weekly_elder"));
		SetQuest(possibleQuest);

		bool isRefreshDay = (Timeline.EndOfCurrentWeek - DateTime.UtcNow).TotalDays > 6.1;
		Visible = !onlyShowOnResetDay || (isRefreshDay && possibleQuest is not null);

		loading.Visible = false;
	}

	private void SetQuest(GameItem newQuest)
	{
		if (currentQuest != newQuest)
		{
			currentQuest?.OnChanged -= UpdateQuest;
			currentQuest = newQuest;
			currentQuest?.OnChanged += UpdateQuest;
		}
		UpdateQuest();
	}

	private void UpdateQuest()
	{
		checkmark.Visible = currentQuest?.QuestComplete ?? false;
		content.Visible = currentQuest is not null;
		noSuperchargerMessage.Visible = currentQuest is null; //show different message if user has qualified for 160 quest
		entry.SetItem(currentQuest?.template?.GetVisibleQuestRewards()?.FirstOrDefault());

		if (currentQuest is null || progressBar is null)
			return;
		var objective = currentQuest.template["Objectives"][0];
		progressBar.Value = (currentQuest.attributes["completion_" + objective["BackendName"].ToString()]?.GetValue<int>() ?? 0) / (float)objective["Count"].GetValue<int>();
	}
}
