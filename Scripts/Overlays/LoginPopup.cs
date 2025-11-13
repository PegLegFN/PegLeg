using Godot;
using System.Threading.Tasks;

public partial class LoginPopup : ModalWindow
{
    [Export]
    CodeLoginLabel loginLabel;
    [Export]
    LineEdit exchangeCodeBox;
    [Export]
    Button confirmLoginButton;

    static LoginPopup instance;

    public override void _Ready()
    {
        base._Ready();
        instance = this;
        confirmLoginButton.Pressed += Confirm;
    }

    public static async Task<GameAccount> OpenLoginPopup()=>
        await instance.OpenLoginPopupInst();

    bool isActive = false;
    GameAccount loggedInAccount;
    async Task<GameAccount> OpenLoginPopupInst()
    {
        exchangeCodeBox.Text = string.Empty;
        if (isActive)
            return null;
        isActive = true;
        loggedInAccount = null;
        SetWindowOpen(true);
        loginLabel.GenerateCode();
        confirmLoginButton.Text = "Waiting for approval...";
        confirmLoginButton.Disabled = true;
        while (isActive)
            await Helpers.WaitForFrame();
        loginLabel.Cancel();
        SetWindowOpen(false);
        return loggedInAccount;
    }

    public void OnFail()
    {
        confirmLoginButton.Text = "Approval Failed";
    }

    public void RecieveAccount(string accountId)
    {
        loggedInAccount = GameAccount.GetOrCreateAccount(accountId);
        confirmLoginButton.Text = "Continue";
        confirmLoginButton.Disabled = false;
    }

    protected override void CloseWindowViaInput() => Cancel();
    public async void Cancel()
    {
        if (loggedInAccount is not null)
        {
            using var _ = LoadingOverlay.CreateToken();
            await GameAccount.RemoveAccount(loggedInAccount.accountId);
        }
        isActive = false;
    }

    public async void Confirm()
    {
        if (loggedInAccount is null)
            return;
        using var _ = LoadingOverlay.CreateToken();
        await loggedInAccount.SaveDeviceDetails();
        isActive = false;
    }

    public async void AttemptExchange()
    {
        var exchangeCodeResponse = await GameClient.PreferredClient.LoginWithExchangeCode(exchangeCodeBox.Text);
        if (exchangeCodeResponse?["access_token"] is not null)
            return;
        GameAccount.LoginToAccount(exchangeCodeResponse);
        SetWindowOpen(false);
    }
}
