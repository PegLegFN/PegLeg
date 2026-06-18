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
	bool animate = true;
	[Export]
	Color tooltipColor = Colors.Aquamarine;

	public override async void _Ready()
	{
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
				currentAccount.OnRatingDataChanged -= OnRatingChanged;

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
			currentAccount.OnRatingDataChanged += OnRatingChanged;

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

	public override void _ExitTree()
	{
		if (currentAccount is not null)
		{
			if (ventures)
				currentAccount.OnVentureRatingDataChanged -= OnRatingChanged;
			else
				currentAccount.OnRatingDataChanged -= OnRatingChanged;
		}
		GameAccount.ActiveAccountChanged -= OnActiveAccountChanged;
	}
}
