using Godot;
using System;

public partial class UsernameLabel : Label
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		GameAccount.ActiveAccountChanged += UpdateAccount;
		UpdateAccount();
	}

	public override void _ExitTree()
	{
		GameAccount.ActiveAccountChanged -= UpdateAccount;
	}

	private void UpdateAccount()
	{
		string displayName = GameAccount.ActiveAccount?.DisplayName;
		if (AppConfig.Get("interface", "obscure_names", false))
		{
			var idx = Array.IndexOf(GameAccount.OwnedAccounts, GameAccount.ActiveAccount);
			if (idx >= 0)
				displayName = $"Account #{idx + 1}";
		}
		Text = displayName;
	}
}
