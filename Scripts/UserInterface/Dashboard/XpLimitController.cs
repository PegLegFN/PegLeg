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

    GameProfile stwProfile;
    GameProfile brProfile;

    public override void _Ready()
    {
        content.Visible = false;
        loading.Visible = true;
        GameAccount.ActiveAccountChanged += UpdateAccount;
        UpdateAccount();
    }

    public override void _ExitTree()
    {
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
            if (stwProfile is not null)
                stwProfile.OnProfileChanged -= UpdateProfiles;
            stwProfile = account.GetProfile(FnProfileTypes.AccountItems);
            stwProfile.OnProfileChanged += UpdateProfiles;

            if (brProfile is not null)
                brProfile.OnProfileChanged -= UpdateProfiles;
            brProfile = account.GetProfile(FnProfileTypes.CosmeticInventory);
            brProfile.OnProfileChanged += UpdateProfiles;

            UpdateProfiles();
        }
        finally
        {
            loading.Visible = false;
        }
    }

    private void UpdateProfiles() => UpdateProfiles(false);
    private void ForceUpdateProfiles() => UpdateProfiles(true);
    private async void UpdateProfiles(bool force)
    {
        loading.Visible = true;
        content.Visible = false;
        try
        {
            await Task.WhenAll(
                stwProfile.Query(force),
                brProfile.Query(force)
            );
            UpdateXP();
            content.Visible = true;
        }
        finally
        {
            loading.Visible = false;
        }
    }

    void UpdateXP()
    {
        var stwReset = DateTime.UtcNow.WeeklyRefresh(DayOfWeek.Tuesday, 14);
        var playtimeReset = DateTime.UtcNow.WeeklyRefresh(DayOfWeek.Thursday, 14);
        var playtimeLimit = PegLegResourceManager.MagicNumbers["playtimeXPLimit"].GetValue<int>();

        var stwXpItem = stwProfile.GetFirstTemplateItem("Token:stw_accolade_tracker");
        stwXpDisplay.SetXpProgress(
            stwXpItem?.attributes["weekly_xp"]?.GetValue<int?>() ?? 0, 
            stwXpItem?.template["SoftWeeklyXPCap"].GetValue<int>() ?? 1,
            stwReset
        );
        playtimeXpDisplay.SetXpProgress(
            brProfile.statAttributes["playtime_xp"]?["currentWeekXp"]?.GetValue<int?>() ?? 0, 
            PegLegResourceManager.MagicNumbers["playtimeXPLimit"].GetValue<int>(),
            playtimeReset
        );
        creativeXpDisplay.SetXpProgress(
            brProfile.statAttributes["creative_dynamic_xp"]?["currentWeekXp"]?.GetValue<int?>() ?? 0, 
            PegLegResourceManager.MagicNumbers["playtimeXPLimit"].GetValue<int>(),
            playtimeReset
        );

        var currentXP = brProfile.statAttributes["xp"]?.GetValue<int>() ?? 0;
        var currentLV = brProfile.statAttributes["level"]?.GetValue<int>() ?? 0;

        levelLabel.Text = currentLV.Notate();
        xpAmount.Text = currentXP.Notate();
        xpProgress.Value = (float)currentXP / 80000;

        var requiredXP = ((200 - currentLV) * 80000) - currentXP;

        xpUntilMax.Text = requiredXP.Compactify();
    }
}
