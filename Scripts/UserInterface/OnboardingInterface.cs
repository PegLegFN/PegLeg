using Godot;

public partial class OnboardingInterface : Control
{
    [Export(PropertyHint.File, "*.tscn")]
    string mainInterfacePath;
    [Export]
    float curtainOpenDuration = 0.25f;
    [Export]
    ShaderHook curtain;
    [Export]
    AudioStreamPlayer music;
    [Export]
    Control loadingWheel;
    [Export]
    string bootScenePath = "res://Scenes/boot_scene.tscn";

    [ExportGroup("Login Code")]
    [Export]
    Control loginCodeContent;
    [Export]
    CodeLoginLabel loginLabel;
    [Export]
    Button retryLoginButton;
    [Export]
    Button continueButton;
    [Export]
    Button importButton;

    [ExportGroup("Account Selection")]
    [Export]
    Control accountSelectionPanel;

    public override async void _Ready()
    {
        retryLoginButton.Visible = false;
        continueButton.Disabled = true;
        continueButton.Text = "";
        curtain.SetShaderFloat(0, "RevealScale");
        curtain.Visible = true;

        importButton.Visible = DirAccess.DirExistsAbsolute("user://../accounts");
        importButton.Text = AppConfig.PegLegVersion.prerelease > 0 ? "Import Accounts from PegLeg (Release)" : "Import Accounts from PegLeg Beta Branch";

        MusicController.StopMusic();
        music.VolumeDb = -80;
        var musicFadeout = GetTree().CreateTween().SetParallel();
        musicFadeout.TweenProperty(music, "volume_db", 0, 1)
            .SetTrans(Tween.TransitionType.Expo)
            .SetEase(Tween.EaseType.Out);
        music.Play();

        TweenCurtain(true);
        await Helpers.WaitForTimer(curtainOpenDuration);
        curtain.Visible = false;

        StartLogin();
    }

    void SwitchToLite()
    {
        AppConfig.Set("core", "litemode", true);
        GetTree().ChangeSceneToFile(bootScenePath);
    }

    async void ImportAccounts()
    {
        loginCodeContent.Visible = false;
        loadingWheel.Visible = true;
        bool hasAccount = false;
        bool isBeta = AppConfig.PegLegVersion.prerelease > 0;
        string fromPath = isBeta ? "user://../accounts" : "user://Beta/accounts";

        try
        {
            foreach (var file in DirAccess.GetFilesAt(fromPath))
            {
                DirAccess.CopyAbsolute($"{fromPath}/{file}", $"user://accounts/{file}");
            }
            GameAccount.UpdateAccountCache();

            if (!hasAccount)
            {
                foreach (var a in GameAccount.OwnedAccounts)
                {
                    if (!await a.SetAsActiveAccount())
                        continue;
                    hasAccount = true;
                    break;
                }
            }
            if (hasAccount)
            {
                ContinueToMainScene();
            }
        }
        finally
        {
            if (!hasAccount)
            {
                loginCodeContent.Visible = true;
                loadingWheel.Visible = false;
                importButton.Disabled = true;
            }
        }
    }

    void TweenCurtain(bool open)
    {
        //var iconStart = panelIcon.GlobalPosition;
        //panelIcon.AnchorTop = panelIcon.AnchorBottom = open ? 0 : 0.5f;
        //panelIcon.ResetOffsets();
        //var iconEnd = panelIcon.GlobalPosition;
        //panelIcon.GlobalPosition = iconStart;

        var tween = GetTree().CreateTween().SetParallel();
        tween.TweenProperty(curtain, "SH_RevealScale", open ? 1 : 0, curtainOpenDuration);
        //tween.TweenProperty(panelIcon, "global_position", iconEnd, curtainOpenDuration);
    }

    public void StartLogin()
    {
        codeAccountId = "";
        retryLoginButton.Visible = false;
        loginLabel.GenerateCode();
        continueButton.Text = "Waiting for approval...";
        continueButton.Disabled = true;
    }

    public void LoginCodeFail()
    {
        retryLoginButton.Visible = true;
        continueButton.Text = "Approval Failed";
    }

    public void LoginCodeSuccess(string accountId)
    {
        codeAccountId = accountId;
        continueButton.Text = "Login";
        continueButton.Disabled = false;
    }

    string codeAccountId;
    public async void ComplateCodeLogin()
    {
        if (string.IsNullOrEmpty(codeAccountId))
            return;
        var account = GameAccount.GetOrCreateAccount(codeAccountId);
        curtain.Visible = true;
        TweenCurtain(false);
        var timer = Helpers.WaitForTimer(curtainOpenDuration);
        await account.SaveDeviceDetails();
        await account.SetAsActiveAccount();
        await timer;
        LoadMainScene();
    }

    public async void ContinueToMainScene()
    {
        curtain.Visible = true;
        TweenCurtain(false);
        await Helpers.WaitForTimer(curtainOpenDuration);
    }

    void LoadMainScene()
    {
        GetTree().ChangeSceneToFile(mainInterfacePath);
        MusicController.ResumeMusic();
    }
}
