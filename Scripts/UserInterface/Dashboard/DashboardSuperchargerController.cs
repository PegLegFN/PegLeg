using Godot;
using System;
using System.Linq;

public partial class DashboardSuperchargerController : Control
{
    [Export]
    GameItemEntry entry;
    [Export]
    Control noSuperchargerMessage;
    [Export]
    Control checkmark;
    [Export]
    Control loading;
    [Export]
    bool onlyShowOnResetDay = false;

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
        bool isRefreshDay = (DateTime.UtcNow.WeeklyRefresh(DayOfWeek.Thursday) - DateTime.UtcNow).TotalDays > 6.1;
        if (onlyShowOnResetDay)
        {
            Visible = false;
            if (!isRefreshDay)
                return;
        }
        entry.ClearItem();
        entry.Visible = false;
        checkmark.Visible = false;
        loading.Visible = true;
        noSuperchargerMessage.Visible = false;

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
        entry.Visible = possibleQuest is not null;
        noSuperchargerMessage.Visible = possibleQuest is null;
        entry.SetItem(possibleQuest?.template?.GetVisibleQuestRewards()?.FirstOrDefault());
    }
}
