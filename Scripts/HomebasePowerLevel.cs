using Godot;
using System.Threading;

public partial class HomebasePowerLevel : Control
{

    [Export]
    Label homebaseNumberLabel;
    [Export]
    Range homebaseNumberProgressBar;
    [Export]
    bool ventures;

    public override void _Ready()
    {
        GameAccount.ActiveAccountChanged += OnActiveAccountChanged;
        OnActiveAccountChanged();
    }
    //todo: move fort stat change detection logic to GameAccount and GameProfile, and subscribe to OnFortStatChanged
    CancellationTokenSource accountChangeCts = new();
    async void OnActiveAccountChanged()
    {
        accountChangeCts.CancelAndRegenerate(out var ct);

        if (currentProfile is not null)
        {
            currentProfile.OnItemAdded -= OnProfileItemChanged;
            currentProfile.OnItemUpdated -= OnProfileItemChanged;
            currentProfile.OnItemRemoved -= OnProfileItemChanged;

            currentProfile.OnStatsChanged -= OnProfileStatChanged;

            currentProfile = null;
        }

        var account = GameAccount.activeAccount;
        if (!await account.Authenticate() || ct.IsCancellationRequested)
            return;

        var newProfile = await account.GetProfile(FnProfileTypes.AccountItems).Query();
        if (ct.IsCancellationRequested)
            return;

        currentProfile = newProfile;

        currentProfile.OnItemAdded += OnProfileItemChanged;
        currentProfile.OnItemUpdated += OnProfileItemChanged;
        currentProfile.OnItemRemoved += OnProfileItemChanged;

        currentProfile.OnStatsChanged += OnProfileStatChanged;

        UpdateStatsVisuals(LatestStats);
    }

    FORTStats LatestStats => ventures ? currentProfile.account.GetVentureFortStats(true) : currentProfile.account.GetFORTStats(true);

    GameProfile currentProfile;

    void OnProfileStatChanged()
    {
        UpdateStatsVisuals(LatestStats);
    }

    void OnProfileItemChanged(GameItem item)
    {
        if (item?.template?.Type == "Worker")
            UpdateStatsVisuals(LatestStats);
    }

    private void UpdateStatsVisuals(FORTStats stats)
    {
        var powerLevel = stats.PowerLevel;
        homebaseNumberLabel.Text = Mathf.Floor(powerLevel).ToString();
        homebaseNumberProgressBar.Value = powerLevel % 1;
        TooltipText = CustomTooltip.GenerateSimpleTooltip(
                "Power Level",
                homebaseNumberLabel.Text,
                [
                    $"Homebase Power: {Mathf.Floor(powerLevel)}\n({Mathf.Floor((powerLevel % 1) * 100)}% progress to {Mathf.Floor(powerLevel) + 1})"
                ],
                Colors.AliceBlue.ToHtml()
            );
    }

    public override void _ExitTree()
    {
        if (currentProfile is not null)
        {
            currentProfile.OnItemAdded -= OnProfileItemChanged;
            currentProfile.OnItemUpdated -= OnProfileItemChanged;
            currentProfile.OnItemRemoved -= OnProfileItemChanged;

            currentProfile.OnStatsChanged -= OnProfileStatChanged;

            currentProfile = null;
        }
        GameAccount.ActiveAccountChanged -= OnActiveAccountChanged;
    }
}
