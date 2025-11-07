using Godot;

public partial class CodeLoginLabel : Node
{
    [Signal]
    public delegate void UserCodeChangedEventHandler(string userCode);

    [Signal]
    public delegate void LoginStartedEventHandler();
    [Signal]
    public delegate void LoginEndedEventHandler();
    [Signal]
    public delegate void LoginSucceededEventHandler();
    [Signal]
    public delegate void LoginSuccessEventHandler(string accountId);
    [Signal]
    public delegate void LoginFailedEventHandler();

    [Signal]
    public delegate void LoginActiveChangedEventHandler(bool started);
    [Signal]
    public delegate void LoginResultChangedEventHandler(bool success);

    int codeExpiresAt = -99;
    bool CodeExpired => codeExpiresAt <= (Time.GetTicksMsec() * 0.001) - 10;
    bool gettingClient = false;
    bool started = false;

    public async void GenerateCode(bool force = false)
    {
        if (gettingClient)
            return;
        if (started)
        {
            if (!force)
                return;
            started = false;
            EmitSignal(SignalName.LoginEnded);
            EmitSignal(SignalName.LoginActiveChanged, false);
            EmitSignal(SignalName.UserCodeChanged, "");
        }

        gettingClient=true;
        var linkData = await GameClient.GetLoginLinkData();
        gettingClient = false;

        if (linkData is not null && linkData["errorMessage"] is null)
        {
            cooldown = 0;
            started = true;
            codeExpiresAt = linkData["expires_at"].GetValue<int>();

            EmitSignal(SignalName.LoginStarted);
            EmitSignal(SignalName.LoginActiveChanged, true);

            EmitSignal(SignalName.UserCodeChanged, linkData["user_code"].ToString());
            return;
        }

        GD.Print(linkData?["errorMessage"]);
    }

    public void Cancel()
    {
        if (!started)
            return;
        codeExpiresAt = -99;
    }

    float cooldown = 0;
    public override void _Process(double delta)
    {
        if (!started)
            return;

        if (CodeExpired)
        {
            started = false;
            
            EmitSignal(SignalName.LoginFailed);
            EmitSignal(SignalName.LoginResultChanged, false);

            EmitSignal(SignalName.LoginEnded);
            EmitSignal(SignalName.LoginActiveChanged, false);

            EmitSignal(SignalName.UserCodeChanged, "");
            return;
        }

        cooldown -= (float)delta;
        if (cooldown >= 0)
            return;
        cooldown = 11;
        CheckForCode();
    }

    async void CheckForCode()
    {
        var linkCheckRequest = await GameClient.CheckLoginLinkCode();
        if (linkCheckRequest?["access_token"] is null)
            return;

        started = false;

        GD.Print("APPROVE: " + linkCheckRequest);

        EmitSignal(SignalName.LoginSuccess, linkCheckRequest["account_id"].ToString());

        EmitSignal(SignalName.LoginSucceeded);
        EmitSignal(SignalName.LoginResultChanged, true);

        EmitSignal(SignalName.LoginEnded);
        EmitSignal(SignalName.LoginActiveChanged, false);

        EmitSignal(SignalName.UserCodeChanged, "");

        GameAccount.LoginToAccount(linkCheckRequest?.AsObject());
        return;
    }
}
