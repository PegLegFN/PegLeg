using Godot;

public partial class CurrencyHighlight : GameItemEntry
{
	public static CurrencyHighlight Instance { get; private set; }

	[Export]
	string fixedCurrencyType;

	public override void _Ready()
	{
		base._Ready();
		var defaultCurrency = "AccountResource:eventcurrency_scaling";
		if (string.IsNullOrWhiteSpace(fixedCurrencyType))
			Instance = this;
		else
			defaultCurrency = fixedCurrencyType;
		Visible = false;
		GameAccount.ActiveAccountChanged += OnAccountChanged;
		SetCurrencyTemplate(GameItemTemplate.Get(defaultCurrency));
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		GameAccount.ActiveAccountChanged -= OnAccountChanged;
	}

	void OnAccountChanged() => SetCurrencyTemplate(currentTemplate);

	GameItemTemplate currentTemplate;

	public async void SetCurrencyTemplate(GameItemTemplate currencyTemplate)
	{
		currentTemplate = currencyTemplate;

		if (currencyTemplate is null)
		{
			ClearItem();
			Visible = false;
			return;
		}
		Visible = true;

		if (currentItem?.template != currencyTemplate)
			SetItem(currencyTemplate.CreateInstance(0));

		var account = GameAccount.ActiveAccount;
		var profileItem = (await account.GetProfile(FnProfileTypes.AccountItems).Query()).GetFirstTemplateItem(currencyTemplate.TemplateId);

		SetItem(profileItem ?? currencyTemplate.CreateInstance(0));
	}
}
