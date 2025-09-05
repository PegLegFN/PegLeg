using Godot;
using System;
using System.Text.Json;
using System.Threading.Tasks;

public partial class XpLimitController : Control
{
    [Export]
    XpLimitDisplay stwXpDisplay;
    [Export]
    XpLimitDisplay playtimeXpDisplay;
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
        RefreshTimerController.OnHourChanged += ForceUpdateProfiles;
        GameAccount.ActiveAccountChanged += UpdateAccount;
        UpdateAccount();
    }

    public override void _ExitTree()
    {
        RefreshTimerController.OnHourChanged -= ForceUpdateProfiles;
        GameAccount.ActiveAccountChanged -= UpdateAccount;
    }

    private async void UpdateAccount()
    {
        loading.Visible = true;
        content.Visible = false;
        try
        {
            var account = GameAccount.activeAccount;
            if (!await account.Authenticate())
                return;

            stwProfile = account.GetProfile(FnProfileTypes.AccountItems);
            brProfile = account.GetProfile(FnProfileTypes.CosmeticInventory);
            await GameAccount.activeAccount.ClientQuestLoginAthena();

            await UpdateProfiles(false);
        }
        finally
        {
            loading.Visible = false;
        }
    }

    private async void UpdateProfiles() => await UpdateProfiles(false);
    private async void ForceUpdateProfiles() => await UpdateProfiles(true);
    private async Task UpdateProfiles(bool force)
    {
        loading.Visible = true;
        content.Visible = false;
        try
        {
            //for some reason, XP stat changes don't increment the profile revision, so we need to completely re-fetch the BR profile
            await Task.WhenAll(
                stwProfile.Query(force),
                brProfile.Query(force, force),
                CalenderRequests.CheckCalender()
            );
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
        if (stwReset < DateTime.Now || playtimeReset < DateTime.Now)
            ForceUpdateProfiles();
    }

    DateTime stwReset;
    DateTime playtimeReset;

    void UpdateXP()
    {
        stwReset = DateTime.UtcNow.WeeklyRefresh(DayOfWeek.Tuesday, 14);
        playtimeReset = DateTime.UtcNow.WeeklyRefresh(DayOfWeek.Thursday, 14);
        var playtimeLimit = PegLegResourceManager.MagicNumbers["playtimeXPLimit"].GetValue<int>();

        var stwXpItem = stwProfile.GetFirstTemplateItem("Token:stw_accolade_tracker");

        bool ignoreStwXp = (stwXpItem?.attributes["last_reset"]?.Deserialize<DateTime>() ?? default) < stwReset.AddDays(-7);
        int? brWeek = CalenderRequests.BRSeasonWeek;
        bool ignorePlaytimeXp = brWeek != brProfile.statAttributes["playtime_xp"]?["currentWeek"]?.GetValue<int?>();
        bool ignoreCreativeXp = brWeek != brProfile.statAttributes["creative_dynamic_xp"]?["currentWeek"]?.GetValue<int?>();

        stwXpDisplay.SetXpProgress(
            ignoreStwXp ? 0 : (stwXpItem?.attributes["weekly_xp"]?.GetValue<int?>() ?? 0), 
            stwXpItem?.template["SoftWeeklyXPCap"].GetValue<int>() ?? 1,
            stwReset
        );
        playtimeXpDisplay.SetXpProgress(
            ignorePlaytimeXp ? 0: (brProfile.statAttributes["playtime_xp"]?["currentWeekXp"]?.GetValue<int?>() ?? 0), 
            PegLegResourceManager.MagicNumbers["playtimeXPLimit"].GetValue<int>(),
            playtimeReset
        );
        creativeXpDisplay.SetXpProgress(
            ignoreCreativeXp ? 0: (brProfile.statAttributes["creative_dynamic_xp"]?["currentWeekXp"]?.GetValue<int?>() ?? 0), 
            PegLegResourceManager.MagicNumbers["playtimeXPLimit"].GetValue<int>(),
            playtimeReset
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
            GD.Print($"Mult: {restedMult}");
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
