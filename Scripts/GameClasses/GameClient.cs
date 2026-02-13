using Godot;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XmppDotNet.Xmpp.Sasl;

public class GameClient
{
    static string ClientAuthHeaderFromKeys(string clientID, string clientSecret) =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientID}:{clientSecret}"));

    const string fortPCClientId = "ec684b8c687f479fadea3cb2ad83f5c6";
    const string fortPCSecret = "e1f31c211f28413186262d37a13fc84d";
    public static GameClient PCClient { get; private set; } = new(fortPCClientId, fortPCSecret);

    const string fortIOSClientId = "3446cd72694c4a4485d81b77adbb2141";
    const string fortIOSSecret = "9209d4a5e25a457fb9b07489d313b41a";
    public static GameClient IOSClient { get; private set; } = new(fortIOSClientId, fortIOSSecret);

    const string fortAndroidClientId = "3f69e56c7649492c8cc29f1af08a8a12";
    const string fortAndroidSecret = "b51ee9cb12234f50a69efa67ef53812e";
    public static GameClient AndroidClient { get; private set; } = new(fortAndroidClientId, fortAndroidSecret);

    const string fortNewSwitchClientId = "98f7e42c2e3a4f86a74eb43fbb41ed39";
    const string fortNewSwitchSecret = "0a2449a2-001a-451e-afec-3e812901c4d7";
    public static GameClient NewSwitchClient { get; private set; } = new(fortNewSwitchClientId, fortNewSwitchSecret);

    public static readonly Dictionary<string, GameClient> clients = new()
    {
        [fortPCClientId] = PCClient,
        [fortIOSClientId] = IOSClient,
        [fortAndroidClientId] = AndroidClient,
        [fortNewSwitchClientId] = NewSwitchClient,
    };

    public static GameClient PreferredClient { get; private set; } = AndroidClient;


    public string ClientID { get; private set; }
    public AuthenticationHeaderValue ClientHeader { get; private set; }
    GameClient(string clientId, string secret)
    {
        ClientID = clientId;
        ClientHeader = new("Basic", ClientAuthHeaderFromKeys(clientId, secret));
    }

    AuthenticationHeaderValue clientTokenHeader;
    int clientExpiresAt = -999;
    bool ClientTokenExpired => clientExpiresAt <= (Time.GetTicksMsec() * 0.001) - 600;

    public async Task<AuthenticationHeaderValue> GetClientTokenHeader()
    {
        if (!ClientTokenExpired)
            return clientTokenHeader;

        var response = await RequestToken("grant_type=client_credentials");
        if (await response.CheckForError(true))
            return null;

        var tokenData = await response.ReadJson();

        GD.Print("client token success");
        clientExpiresAt = Mathf.FloorToInt(Time.GetTicksMsec() * 0.001) + tokenData["expires_in"].GetValue<int>();
        return clientTokenHeader = new("Bearer", tokenData["access_token"].ToString());
    }

    private Task<HttpResponseMessage> RequestToken(string formContent) =>
        FnWebAddresses.EpicAccount
            .MakeRequest("account/api/oauth/token", HttpMethod.Post)
            .SetAuthorisation(ClientHeader)
            .SetFormContent(formContent)
            .Send();

    public Task<HttpResponseMessage> LoginWithOneTimeCode(string oneTimeCode)
    {
        if(string.IsNullOrWhiteSpace(oneTimeCode))
            return Task.FromResult(new HttpResponseMessage()
            {
                StatusCode = System.Net.HttpStatusCode.BadRequest,
                ReasonPhrase = "No authorization code provided"
            });
        return RequestToken(
            $"grant_type=authorization_code&" +
            $"code={oneTimeCode}&" +
            $"token_type=eg1"
        );
    }

    public Task<HttpResponseMessage> LoginWithRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Task.FromResult(new HttpResponseMessage()
            {
                StatusCode = System.Net.HttpStatusCode.BadRequest,
                ReasonPhrase = "No refresh token provided"
            });
        return RequestToken(
            $"grant_type=refresh_token&" +
            $"refresh_token={refreshToken}&" +
            $"token_type=eg1"
        );
    }

    public Task<HttpResponseMessage> LoginWithExchangeCode(string exchangeCode)
    {
        if (string.IsNullOrWhiteSpace(exchangeCode))
            return Task.FromResult(new HttpResponseMessage()
            {
                StatusCode = System.Net.HttpStatusCode.BadRequest,
                ReasonPhrase = "No exchange code provided"
            });
        return RequestToken(
            $"grant_type=exchange_code&" +
            $"exchange_code={exchangeCode}&" +
            $"token_type=eg1"
        );
    }

    public Task<HttpResponseMessage> LoginWithDeviceAuth(JsonObject deviceDetails)=> 
        LoginWithDeviceAuth(
            deviceDetails?["accountId"]?.ToString(), 
            deviceDetails?["deviceId"]?.ToString(), 
            deviceDetails?["secret"]?.ToString()
        );

    public Task<HttpResponseMessage> LoginWithDeviceAuth(string accountId, string deviceId, string deviceSecret)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return Task.FromResult(new HttpResponseMessage()
            {
                StatusCode = System.Net.HttpStatusCode.BadRequest,
                ReasonPhrase = "No account ID provided"
            });
        if (string.IsNullOrWhiteSpace(deviceId))
            return Task.FromResult(new HttpResponseMessage()
            {
                StatusCode = System.Net.HttpStatusCode.BadRequest,
                ReasonPhrase = "No device ID provided"
            });
        if (string.IsNullOrWhiteSpace(deviceSecret))
            return Task.FromResult(new HttpResponseMessage()
            {
                StatusCode = System.Net.HttpStatusCode.BadRequest,
                ReasonPhrase = "No device secret provided"
            });
        return RequestToken(
            $"grant_type=device_auth&" +
            $"account_id={accountId}&" +
            $"device_id={deviceId}&" +
            $"secret={deviceSecret}&" +
            $"token_type=eg1"
        );
    }

    JsonObject activeLinkData;
    int linkCodeExpiresAt = -999;
    string deviceCode;
    bool LinkCodeHalfExpired => linkCodeExpiresAt <= Mathf.Max((Time.GetTicksMsec() * 0.001) - 300, 0);
    bool LinkCodeExpired => linkCodeExpiresAt <= Mathf.Max((Time.GetTicksMsec() * 0.001) - 10, 0);
    public async Task<JsonObject> GetLoginLinkData(bool force = false)
    {
        if (ClientID!=fortNewSwitchClientId)
            return await NewSwitchClient.GetLoginLinkData(force);

        if (!LinkCodeHalfExpired && !force)
            return activeLinkData;
        if(await GetClientTokenHeader() is not AuthenticationHeaderValue clientTokenHeader)
            return null;

        var linkGetResponse = await FnWebAddresses.EpicAccount
            .MakeRequest("/account/api/oauth/deviceAuthorization", HttpMethod.Post)
            .SetAuthorisation(clientTokenHeader)
            .Send();
        if (await linkGetResponse.CheckForError(true))
            return null;

        activeLinkData = await linkGetResponse.ReadJson<JsonObject>();
        linkCodeExpiresAt = Mathf.FloorToInt(Time.GetTicksMsec() * 0.001) + activeLinkData["expires_in"].GetValue<int>();
        activeLinkData["expires_at"] = linkCodeExpiresAt;
        deviceCode = activeLinkData["device_code"].ToString();

        return activeLinkData;
    }

    DateTime lastChecked = DateTime.MinValue;
    public async Task<HttpResponseMessage> CheckLoginLinkCode()
    {
        if (ClientID != fortNewSwitchClientId)
        {
            var checkResponse = await NewSwitchClient.CheckLoginLinkCode();
            if (!checkResponse.IsSuccessStatusCode)
                return checkResponse;

            GD.Print("temporary auth recieved, preparing to exchange client type...");

            var tempData = await checkResponse.ReadJson();
            var exchangeResponse = await FnWebAddresses.EpicAccount
                .MakeRequest("account/api/oauth/exchange")
                .SetAuthorisation(new("Bearer", tempData["access_token"].ToString()))
                .Send();
            if(!exchangeResponse.IsSuccessStatusCode)
                return exchangeResponse;

            GD.Print("exchanging client types...");

            var exchangeData = await exchangeResponse.ReadJson();
            return await LoginWithExchangeCode(exchangeData?["code"]?.ToString());
        }

        if (LinkCodeExpired)
            return new HttpResponseMessage()
            {
                StatusCode = System.Net.HttpStatusCode.GatewayTimeout,
                ReasonPhrase = "Link code timed out"
            };
        var timeSinceLastCheck = (DateTime.Now - lastChecked).TotalSeconds;
        if (timeSinceLastCheck < 10)
            return new HttpResponseMessage()
            {
                StatusCode = System.Net.HttpStatusCode.TooManyRequests,
                ReasonPhrase = $"Link code can be checked again in {10-timeSinceLastCheck:0.#} seconds"
            };
        lastChecked = DateTime.Now;
        var tokenResponse = await RequestToken(
            $"grant_type=device_code&" +
            $"device_code={deviceCode}&" +
            $"token_type=eg1"
        );
        if (tokenResponse.IsSuccessStatusCode)
            linkCodeExpiresAt = -999;
        return tokenResponse;
    }
}
