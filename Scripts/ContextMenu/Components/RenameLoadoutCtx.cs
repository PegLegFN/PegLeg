using System.Text.RegularExpressions;

public partial class RenameLoadoutCtx : AbstractContextComponent
{
	public override string Id => "RenameLoadout";
	GameItem currentLoadout;
	public override void Update(ContextMenuHook hook)
	{
		currentLoadout = hook?.itemSource?.currentItem;
		var disabled = currentLoadout?.template?.Type != "CampaignHeroLoadout" && currentLoadout?.templateId != GameAccount.HeroLoadoutBlueprintTID;
		SetDisabled(disabled);
	}

	[GeneratedRegex(@"^[\w /-]+$")]
	private static partial Regex NameCharValidator();

	static string Validator(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
			return null;
		if (input.Length <= 2)
			return "Minimum 2 characters";
		if (input.Length >= 32)
			return "Maximum 32 characters";
		if (!NameCharValidator().Match(input).Success)
			return "Must only contain:\n[A-Z, a-z, 0-9, -, /, space]";
		return null;
	}

	public async void Inspect()
	{
		if (currentLoadout is null)
			return;
		var loadout = currentLoadout;

		string current = "";
		if (loadout.profile is not null)
		{
			current = loadout.profile.account.GetCustomNameForLoadoutSlot(loadout);
		}
		else if (loadout.templateId == GameAccount.HeroLoadoutBlueprintTID)
		{
			current = loadout.attributes["displayName"]?.ToString();
		}

		menu.CloseMenu();
		var newName = await GenericLineEditWindow.ShowLineEdit("Rename Loadout", "", current, "Loadout", Validator);
		if (newName is null)
			return;
		if (loadout.profile is not null)
		{
			loadout.profile.account.SetCustomNameForLoadoutSlot(loadout, newName);
		}
		else if (loadout.templateId == GameAccount.HeroLoadoutBlueprintTID)
		{
			GameAccount.ActiveAccount.RenameHeroLoadoutBlueprint(loadout, newName);
		}
	}
}
