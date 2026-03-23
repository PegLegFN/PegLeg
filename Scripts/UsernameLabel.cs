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
        Text = GameAccount.ActiveAccount?.DisplayName;
    }
}
