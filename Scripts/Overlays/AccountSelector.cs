using Godot;
using System.Collections.Generic;

public partial class AccountSelector : ModalWindow
{
    [Export]
    Control selectorButtonLabel;
    [Export]
    Control selectorButtonLabelTarget;
    [Export]
    Control selectorButtonIcons;
    [Export]
    Foldout foldout;
    [Export]
    Button foldoutBtn;
    [Export]
    Control accountEntryParent;
    [Export]
    PackedScene accountEntryScene;
    List<GameAccountEntry> pooledAccounts = [];
    [Export(PropertyHint.File)]
    string onboardingScene;

    protected override string OpenSound => "WipeAppear";
    protected override string CloseSound => "WipeDisappear";

    public override void _Ready()
    {
        for (int i = 0; i < GameAccount.OwnedAccounts.Length; i++)
        {
            GenerateAccountEntry();
        }
    }

    public async void ReloadAccount()
    {
        using var _ = LoadingOverlay.CreateToken();
        await GameAccount.RefreshActiveAccount();
    }

    public async void OpenLogin()
    {
        var account = await LoginPopup.OpenLoginPopup();
        if (account?.isOwned ?? false)
            PopulateAccounts();
    }

    void PopulateAccounts()
    {
        var accounts = GameAccount.OwnedAccounts;
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

    protected override Tween BuildTween(bool openState, double duration)
    {
        var tween = CreateTween();
        tween.Stop();
        duration *= 2;
        if (openState)
        {
            PopulateAccounts();
            selectorButtonIcons.Modulate = Colors.White;
            selectorButtonLabel.Modulate = Colors.Transparent;
            foldout.SetFoldoutStateImmediate(false);
        }
        else
        {
            foldout.SetFoldoutState(false);
        }
        foldoutBtn.Disabled = true;

        tween.TweenInterval(openState ? 0 : 0.1f);
        tween.SetParallel();
        tween.TweenSubtween(base.BuildTween(openState, openState ? duration : duration * 0.5))
            .SetDelay(openState ? 0 : duration * 0.5);

        tween.TweenProperty(selectorButtonIcons, "modulate", openState ? Colors.Transparent : Colors.White, duration*0.5)
            .SetDelay(openState ? duration * 0.25 : duration * 0.5);
        tween.TweenProperty(selectorButtonLabel, "modulate", openState ? Colors.White : Colors.Transparent, duration*0.5)
            .SetDelay(openState ? duration * 0.25 : duration * 0.5);

        tween.TweenProperty(selectorButtonLabel, "custom_minimum_size:x", openState ? selectorButtonLabelTarget.GetCombinedMinimumSize().X : 0, duration * 0.5)
            .SetDelay(duration * 0.5);

        tween.TweenInterval(0.1f);

        return tween;
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

    async void SelectAccount(string accountId)
    {
        bool loggedIn = false;

        using (LoadingOverlay.CreateToken())
        {
            loggedIn = await GameAccount.SetActiveAccount(accountId);
        }
        SetWindowOpen(false);
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
        GetTree().ChangeSceneToFile(onboardingScene);
    }

    protected override void OnTweenFinished(bool openState)
    {
        base.OnTweenFinished(openState);
        if (openState)
        {
            foldoutBtn.Disabled = false;
            foldout.SetFoldoutState(true);
        }
    }
}
