using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;

public partial class GameAccountEntry : Control
{
	[Signal]
	public delegate void NameChangedEventHandler(string name);

	[Signal]
	public delegate void TooltipChangedEventHandler(string tooltip);

	[Signal]
	public delegate void StatusChangedEventHandler(string tooltip);

	[Signal]
	public delegate void IconChangedEventHandler(Texture2D icon);

	[Signal]
	public delegate void AuthenticatedChangedEventHandler(bool isAuthed);

	[Signal]
	public delegate void InboxChangedEventHandler(bool isAuthed);

	[Signal]
	public delegate void AnyInboxChangedEventHandler(bool isAuthed);

	[Signal]
	public delegate void PressedEventHandler(string accountId);
	[Signal]
	public delegate void DeletedEventHandler(string accountId);

	[Export]
	Texture2D defaultIcon;
	[Export]
	string namePrefix = "";
	[Export]
	bool useActiveAccount = false;
	[Export]
	bool tooltip = true;
	[Export]
	Control launchContent;
	[Export]
	Control launchContentInverse;
	public GameAccount currentAccount { get; protected set; }

	public override void _Ready()
	{
		GameAccount.ActiveAccountChanged += SetActiveAccount;
		ItemInbox.InboxUpdated += UpdateInbox;
		SetActiveAccount();
	}

	public override void _ExitTree()
	{
		GameAccount.ActiveAccountChanged -= SetActiveAccount;
		currentAccount?.OnAccountUpdated -= UpdateAccount;
		ItemInbox.InboxUpdated -= UpdateInbox;
	}

	public override void _PhysicsProcess(double delta)
	{
#if GODOT_WINDOWS
		if (launchContent is null)
			return;
		launchContent.Visible = Input.IsKeyPressed(Key.Shift);
		launchContentInverse?.Visible = !launchContent.Visible;
#endif
	}

	public static async void LaunchGamePrompt() => GameLauncher.LaunchGamePrompt();


	private void OnUserStatus(string account, string status)
	{
		if (currentAccount?.accountId != account)
			return;
		CallDeferred(nameof(SetStatus), status);
	}

	public void SetStatus(string status)
	{
		EmitSignalStatusChanged(status);
	}

	public void SetAccount(GameAccount account)
	{
		if (useActiveAccount)
			return;
		SetAccountInternal(account);
	}

	void SetActiveAccount()
	{
		if (!useActiveAccount)
			return;
		SetAccountInternal(GameAccount.ActiveAccount);
	}

	void SetAccountInternal(GameAccount account)
	{
		if (account == currentAccount)
		{
			UpdateAccount();
			currentAccount.UpdateIcon();
			return;
		}
		currentAccount?.OnAccountUpdated -= UpdateAccount;
		currentAccount = account;
		currentAccount?.OnAccountUpdated += UpdateAccount;
		UpdateAccount();
		currentAccount.UpdateIcon();
	}

	void UpdateAccount()
	{
		var displayName = currentAccount.DisplayName;
		if (AppConfig.Get("interface", "obscure_names", false))
		{
			var idx = Array.IndexOf(GameAccount.OwnedAccounts, currentAccount);
			if (idx >= 0)
				displayName = $"Account #{idx + 1}";
		}
		EmitSignal(SignalName.NameChanged, $"{namePrefix}{displayName}");
		EmitSignal(SignalName.IconChanged, currentAccount.ProfileIcon ?? defaultIcon);
		EmitSignal(SignalName.AuthenticatedChanged, currentAccount.isAuthed);
		UpdateInbox();

		if (!tooltip)
			return;

		string tooltipText = CustomTooltip.GenerateSimpleTooltip(
			$" {displayName}   ",
			null,
			[
				currentAccount.isAuthed ? "Logged In" : (currentAccount.isOwned ? $"Login Failure:\n\"{currentAccount.loginFailureMessage}\"" : "External")
			],
			Colors.Blue.ToHtml()
		);
		EmitSignal(SignalName.TooltipChanged, tooltipText);
	}

	void UpdateInbox()
	{
		EmitSignal(SignalName.InboxChanged, ItemInbox.HasInbox(currentAccount));
		if (useActiveAccount)
			EmitSignal(SignalName.AnyInboxChanged, GameAccount.OwnedAccounts.Except([currentAccount]).Any(ItemInbox.HasInbox));
	}

	public void Press()
	{
		if (currentAccount is null)
			return;
		EmitSignal(SignalName.Pressed, currentAccount.accountId);
	}

	public async void Exchange()
	{
		if (currentAccount is null)
			return;
		GD.Print(await currentAccount.GenerateExchangeCode());
	}

	public void Delete()
	{
		if (currentAccount is null)
			return;
		EmitSignal(SignalName.Deleted, currentAccount.accountId);
	}
}
