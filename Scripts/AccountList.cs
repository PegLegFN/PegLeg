using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class AccountList : Control
{
    [Signal]
    public delegate void AccountSelectedEventHandler(string accountId);
    [Export]
    PackedScene accountEntryScene;
    List<GameAccountEntry> pooledAccounts = [];
    [Export]
    Control accountEntryParent;
    [Export]
    bool repopulateWhenMadeVisible;
    [Export]
    bool excludeActive;
    [Export]
    string bootScenePath = "res://Scenes/boot_scene.tscn";

    public override void _Ready()
    {
        if (repopulateWhenMadeVisible)
        {
            VisibilityChanged += () =>
            {
                if(IsVisibleInTree())
                    PopulateAccounts();
            };
        }
        for (int i = 0; i < GameAccount.OwnedAccounts.Length; i++)
        {
            GenerateAccountEntry();
        }
    }

    void GenerateAccountEntry()
    {
        var accountEntry = accountEntryScene.Instantiate<GameAccountEntry>();
        accountEntry.Visible = false;
        accountEntry.Pressed += SelectAccount;
        accountEntry.Deleted += RemoveAccount;
        accountEntryParent.AddChild(accountEntry);
        pooledAccounts.Add(accountEntry);
    }

    public async void OpenLogin()
    {
        var account = await LoginPopup.OpenLoginPopup();
        if (account?.isOwned ?? false)
            PopulateAccounts();
    }

    public void PopulateAccounts()
    {
        var accounts = GameAccount.OwnedAccounts.Where(a => GameAccount.ActiveAccount != a).ToArray();
        for (int i = 0; i < accounts.Length; i++)
        {
            if (pooledAccounts.Count <= i)
                GenerateAccountEntry();
            pooledAccounts[i].Visible = true;
            pooledAccounts[i].SetAccount(accounts[i]);
            accounts[i].Authenticate().StartTask();
        }
        for (int i = accounts.Length; i < pooledAccounts.Count; i++)
        {
            pooledAccounts[i].Visible = false;
        }
    }

    async void SelectAccount(string accountId)
    {
        bool loggedIn = false;

        using (LoadingOverlay.CreateToken())
        {
            loggedIn = await GameAccount.SetActiveAccount(accountId);
        }
        PopulateAccounts();
        EmitSignalAccountSelected(accountId);
    }

    async void RemoveAccount(string accountId)
    {
        bool hasAuth = false;
        using (LoadingOverlay.CreateToken())
        {
            var account = GameAccount.GetOrCreateAccount(accountId);
            hasAuth = await account.Authenticate();
        }

        //show confirmation menu
        if (await GenericConfirmationWindow.ShowConfirmation(
                "Remove Account?",
                "Remove",
                null,
                hasAuth ? "This account will be signed out and it's persistant login token will be forgotten" : "This account will be removed from PegLeg",
                hasAuth ? "" : "PegLeg couldn't log into this account to sign out. Once you remove this account you should probably Sign Out Everywhere from epicgames.com/account/password"
        ) != true)
            return;

        using (LoadingOverlay.CreateToken())
        {
            if (await GameAccount.RemoveAccount(accountId, true))
            {
                bool hasNextAccount = false;
                foreach (var account in GameAccount.OwnedAccounts)
                {
                    if (await account.SetAsActiveAccount())
                    {
                        hasNextAccount = true;
                        break;
                    }
                }
                if (!hasNextAccount)
                    ReturnToLogin();
                else
                    PopulateAccounts();
                return;
            }
        }

        await GenericConfirmationWindow.ShowError("Could not remove account, Please report this to the developer");
    }

    async void ReturnToLogin()
    {
        await GenericConfirmationWindow.ShowError("There are no more authenticated accounts, returning to the login screen.", "Notice");
        GetTree().ChangeSceneToFile(bootScenePath);
    }
}
