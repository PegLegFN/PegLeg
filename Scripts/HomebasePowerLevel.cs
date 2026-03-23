using Godot;
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
	Color tooltipColor = Colors.Aquamarine;

	public override void _Ready()
	{
		if (useCurrent)
		{
			GameAccount.ActiveAccountChanged += OnActiveAccountChanged;
			OnActiveAccountChanged();
		}
	}

	//todo: move fort stat change detection logic to GameAccount and GameProfile, and subscribe to OnFortStatChanged
	CancellationTokenSource accountChangeCts = new();
	async void OnActiveAccountChanged()
	{
		accountChangeCts = accountChangeCts.CancelAndRegenerate(out var ct);

		if (currentProfile is not null)
		{
			currentProfile.OnItemAdded -= OnProfileItemChanged;
			currentProfile.OnItemUpdated -= OnProfileItemChanged;
			currentProfile.OnItemRemoved -= OnProfileItemChanged;

			currentProfile.OnStatsChanged -= OnProfileStatChanged;

			currentProfile = null;
		}
		if (GameAccount.ActiveAccount.accountId == null)
		{
			return;
		}
		var newProfile = await GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems).Query();
		if (ct.IsCancellationRequested)
			return;

		currentProfile = newProfile;

		currentProfile.OnItemAdded += OnProfileItemChanged;
		currentProfile.OnItemUpdated += OnProfileItemChanged;
		currentProfile.OnItemRemoved += OnProfileItemChanged;

		currentProfile.OnStatsChanged += OnProfileStatChanged;

		UpdateStatsVisuals();
	}

	public async void SetAccountManual(GameAccount account)
	{
		currentProfile = await account.GetProfile(FnProfileTypes.AccountItems).Query();
		UpdateStatsVisuals();
	}

	GameProfile currentProfile;

	void OnProfileStatChanged()
	{
		UpdateStatsVisuals();
	}

	void OnProfileItemChanged(GameItem item)
	{
		if (ventures && item?.template?.Type == "Worker")
			UpdateStatsVisuals();
	}

	private void UpdateStatsVisuals()
	{
		if (currentProfile?.account is null)
			return;
		RatingData stats = ventures ? currentProfile.account.GetVentureRatingData() : currentProfile.account.GetRatingData();
		var powerLevel = stats.PowerLevel;
		homebaseNumberLabel.Text = Mathf.Floor(powerLevel).ToString();
		homebaseNumberProgressBar.Value = powerLevel % 1;
		TooltipText = CustomTooltip.GenerateSimpleTooltip(
			"Power Level",
			homebaseNumberLabel.Text,
			[
				$"{(ventures? "Venture" : "Homebase")} Power: {Mathf.Floor(powerLevel)}\n({Mathf.Floor((powerLevel % 1) * 100)}% progress to {Mathf.Floor(powerLevel) + 1})"
			],
			tooltipColor.ToHtml()
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
