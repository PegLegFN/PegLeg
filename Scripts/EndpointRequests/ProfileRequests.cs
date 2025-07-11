using Godot;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text.Json;


public enum OrderRange
{
    Daily,
    Weekly,
    Monthly
}

public readonly struct FORTStats
{
    //todo: export this via BanjoBotAssets
    static DataTableCurve homebaseRatingCurve;
    static DataTableCurve HomebaseRatingCurve => homebaseRatingCurve ??= new("HomebaseRatingMapping.json", "UIMonsterRating");

    public readonly float fortitude;
    public readonly float offense;
    public readonly float resistance;
    public readonly float technology;
    public FORTStats(float fortitude, float offense, float resistance, float technology)
    {
        this.fortitude = fortitude;
        this.offense = offense;
        this.resistance = resistance;
        this.technology = technology;
    }

    public float PowerLevel => HomebaseRatingCurve.Sample(4 * (fortitude + offense + resistance + technology));
}

static class FnProfileTypes
{
    public const string AccountItems = "campaign";
    public const string Backpack = "theater0";
    public const string Storage = "outpost0";
    //const string VentureBackpack = "theater2";
    public const string PeopleCollection = "collection_book_people0";
    public const string SchematicCollection = "collection_book_schematics0";
    public const string CosmeticInventory = "athena";
    public const string Common = "common_core";
}

public partial class GameAccount
{
    public const string accountDataPath = "user://accounts";
    static readonly AesContext deviceDetailEncryptor = new();

    static string GetDeviceDetailsKey()
    {
        string deviceDetailKey = System.Environment.MachineName + "custard";
        int baseLength = deviceDetailKey.Length;
        for (int i = 0; i < 32 - baseLength; i++)
        {
            deviceDetailKey += "custard"[i % 7];
        }
        return deviceDetailKey[..32];
    }

    static byte[] EncryptDeviceDetails(JsonObject fromDetails)
    {
        //stringify and add padding
        string deviceDetalsString = fromDetails.ToString();
        int remainder = deviceDetalsString.Length % 16;

        for (int i = 0; i < 16 - remainder; i++)
        {
            deviceDetalsString += "^";
        }

        string deviceDetailKey = GetDeviceDetailsKey();

        //encrypt
        deviceDetailEncryptor.Start(AesContext.Mode.EcbEncrypt, deviceDetailKey.ToUtf8Buffer());
        byte[] encryptedDetails = deviceDetailEncryptor.Update(deviceDetalsString.ToUtf8Buffer());
        deviceDetailEncryptor.Finish();
        return encryptedDetails;
    }

    static JsonObject DecryptDeviceDetails(byte[] encryptedDetails)
    {
        if (encryptedDetails is null)
            return null;
        if (encryptedDetails.Length % 16 != 0)
            return null;

        string deviceDetailKey = GetDeviceDetailsKey();
        if (deviceDetailKey.Length != 32)
            return null;

        //decrypt
        deviceDetailEncryptor.Start(AesContext.Mode.EcbDecrypt, deviceDetailKey.ToUtf8Buffer());
        byte[] decryptedDetails = deviceDetailEncryptor.Update(encryptedDetails);
        deviceDetailEncryptor.Finish();
        string deviceDetalsString = Encoding.UTF8.GetString(decryptedDetails, 0, decryptedDetails.Length);


        //remove padding and convert to json
        while (deviceDetalsString.EndsWith('^'))
        {
            deviceDetalsString = deviceDetalsString[..^1];
        }

        JsonObject resultDetails = null;
        try
        {
            resultDetails = JsonNode.Parse(deviceDetalsString).AsObject();
        }
        catch (Exception) { }
        return resultDetails;
    }

    static GameAccount[] LoadStoredAccounts()
    {
        if (!DirAccess.DirExistsAbsolute(accountDataPath))
            return [];

        using var accountDir = DirAccess.Open(accountDataPath);
        return accountDir.GetFiles().Where(f => !f.Contains('.') && gameAccountCache?.ContainsKey(f) != true).Select(f => new GameAccount(f)).ToArray();
    }

    static Dictionary<string, GameAccount> gameAccountCache = LoadStoredAccounts().ToDictionary(a => a.accountId);
    public static void UpdateAccountCache()
    {
        var newCache = LoadStoredAccounts();
        foreach (var item in newCache)
        {
            gameAccountCache.TryAdd(item.accountId, item);
        }
        foreach (var item in gameAccountCache)
        {
            item.Value.localData = null;
        }
    }

    public static GameAccount[] OwnedAccounts => gameAccountCache.Values.Where(a => a.isOwned).ToArray();
    public static GameAccount GetOrCreateAccount(string accountId) => gameAccountCache.ContainsKey(accountId) ? gameAccountCache[accountId] : gameAccountCache[accountId] = new(accountId);
    public static async Task<bool> RemoveAccount(string accountId, bool force = false)
    {
        if (!gameAccountCache.ContainsKey(accountId))
            return true;
        var account = gameAccountCache[accountId];
        if (!await account.RemoveDeviceDetails(force))
            return false;
        account.DeleteLocalData();
        gameAccountCache.Remove(accountId);
        account.accountId = null;
        if (_activeAccount == account)
            _activeAccount = null;
        return true;
    }

    public static async Task<GameAccount> SearchForAccount(string username)
    {
        var activeAccount = GameAccount.activeAccount;
        if(!await activeAccount.Authenticate())
            return null;
        var searchResult = await Helpers.MakeRequest(
            HttpMethod.Get,
            FnWebAddresses.userSearch,
            $"/api/v1/search/{activeAccount.accountId}?platform=epic&prefix={username}",
            "{}",
            activeAccount.AuthHeader
        );
        if (searchResult is not JsonArray accountArray || accountArray.Count == 0)
            return null;
        var resultAccount = GetOrCreateAccount(accountArray[0]["accountId"].ToString());
        if (resultAccount is not null && accountArray[0]["matches"]?[0]?["value"]?.ToString() is string displayName)
            resultAccount.SetLocalData("DisplayName", displayName);
        return resultAccount;
    }

    static GameAccount _activeAccount;
    public static GameAccount activeAccount => _activeAccount ??= new(null);
    public static event Action ActiveAccountChangedEarly;
    public static event Action ActiveAccountChanged;
    public static event Action BookmarksChanged;

    public static async Task<bool> SetActiveAccount(string accountId)
    {
        if (!gameAccountCache.ContainsKey(accountId))
            return false;
        var account = gameAccountCache[accountId];
        if (await account.Authenticate())
        {
            var profile = await account.GetProfile(FnProfileTypes.AccountItems).Query();
            if (!profile.hasProfile)
                return false;
            await account.QueryAllProfiles();
            await account.CheckCalender();
            _activeAccount = account;
            ActiveAccountChangedEarly?.Invoke();
            ActiveAccountChanged?.Invoke();
            BookmarksChanged?.Invoke();
            AppConfig.Set("account", "lastUsed", accountId);
            return true;
        }
        return false;
    }

    public static async Task RefreshActiveAccount()
    {
        if (!await activeAccount.Authenticate())
            return;
        _activeAccount.fortStatsDirty = true;
        _activeAccount.localData = null;
        _activeAccount.localPinnedQuests = null;
        foreach (var profile in _activeAccount.profiles.Values)
        {
            profile.InvalidateProfile();
        }
        await _activeAccount.GetProfile(FnProfileTypes.AccountItems).Query();
        await SetActiveAccount(_activeAccount.accountId);
    }

    public static void LoginToAccount(JsonObject accountAuthResponse)
    {
        if (accountAuthResponse["account_id"]?.ToString() is not string accountId)
            return;
        var account = GetOrCreateAccount(accountId);
        account.SetAuthentication(accountAuthResponse);
    }

    public GameAccount(string accountId)
    {
        this.accountId = accountId;
    }

    public Action OnAccountUpdated;

    bool isBeta;
    public string accountId { get; private set; }
    bool isValid => !string.IsNullOrWhiteSpace(accountId);

    public bool loginFailure { get; private set; }
    public string loginFailureMessage { get; private set; }
    public bool isAuthed => isValid && !loginFailure && !AuthTokenExpired;
    public bool isOwned => isValid && (isAuthed || GetLocalData("DeviceDetails") is not null);

    public string DisplayName => GetLocalData("DisplayName")?.ToString() ?? $"<{accountId}>";
    public Texture2D ProfileIcon => GetLocalData("IconPath")?.ToString() is string iconPath ? CatalogRequests.GetLocalCosmeticResource(iconPath) : null;

    Dictionary<string, GameProfile> profiles = [];

    public GameProfile this[string profileId] => GetProfile(profileId);
    public GameProfile GetProfile(string profileId) => profiles.ContainsKey(profileId ?? "") ? profiles[profileId ?? ""] : profiles[profileId ?? ""] = new(this, profileId ?? "");
    public bool HasProfile(string profileId) => profiles.ContainsKey(profileId);

    public async Task<GameAccount> EnsureProfile(string profileId, bool force = false)
    {
        await GetProfile(profileId).Query(force);
        return this;
    }

    public async Task QueryAllProfiles(bool force = false)
    {
        await profileOperationSemaphore.WaitAsync();
        try
        {
            if (!isOwned)
            {
                if (activeAccount is null)
                    return;
                await Task.WhenAll(
                    GetProfile(FnProfileTypes.AccountItems).QueryUnsafe(force),
                    GetProfile(FnProfileTypes.Common).QueryUnsafe(force)
                );
            }
            else
            {
                await Task.WhenAll(
                    GetProfile(FnProfileTypes.AccountItems).QueryUnsafe(force),
                    GetProfile(FnProfileTypes.Common).QueryUnsafe(force),
                    GetProfile(FnProfileTypes.PeopleCollection).QueryUnsafe(force),
                    GetProfile(FnProfileTypes.SchematicCollection).QueryUnsafe(force),
                    GetProfile(FnProfileTypes.Backpack).QueryUnsafe(force),
                    GetProfile(FnProfileTypes.Storage).QueryUnsafe(force)
                );
            }
                
        }
        finally
        {
            profileOperationSemaphore.Release();
        }
    }

    string authToken;
    int authExpiresAt = -999;
    string refreshToken;
    int refreshExpiresAt = -999;
    AuthenticationHeaderValue accountAuthHeader;
    //fails 60 seconds before it would actualy expire
    public bool AuthTokenExpired => authExpiresAt <= (Time.GetTicksMsec() * 0.001) - 60;
    bool RefreshTokenExpired => refreshExpiresAt <= (Time.GetTicksMsec() * 0.001) - 10;
    public AuthenticationHeaderValue AuthHeader => accountAuthHeader;
    public async Task<bool> Authenticate(bool loadingOverlay = false)
    {
        if (isAuthed)
            return true;
        if (!isOwned)
            return false;

        using var loadToken = LoadingOverlay.CreateToken("authentication");
        if (!loadingOverlay)
            loadToken.Dispose();

        if (!RefreshTokenExpired)
        {
            var refreshAuth = await GameClient.LoginWithRefreshToken(refreshToken);

            if (refreshAuth is not null && refreshAuth["errorMessage"] is null)
            {
                SetAuthentication(refreshAuth);
                return true;
            }

            GD.Print(refreshAuth?["errorMessage"].ToString());
        }

        var dd = GetLocalData("DeviceDetails")?.AsArray().Select(n => n.GetValue<byte>()).ToArray();
        JsonNode deviceAuth = await GameClient.LoginWithDeviceAuth(DecryptDeviceDetails(dd));

        if (deviceAuth is not null && deviceAuth["errorMessage"] is null)
        {
            SetAuthentication(deviceAuth);
            return true;
        }
        bool offline = deviceAuth?["offline"] is JsonValue val && val.GetValueKind() == System.Text.Json.JsonValueKind.True;
        string failMsg = offline ? "Offline" : deviceAuth?["errorMessage"].ToString();
        GD.Print(failMsg);
        if (!loginFailure)
        {
            loginFailure = true;
            loginFailureMessage = failMsg;
            OnAccountUpdated?.Invoke();

            if (offline)
            {
                //dont send login failure notif when offline
                return false;
            }

            NotificationManager.Push([new()
            {
                header = "Login Failure",
                icon = ProfileIcon,
                itemColor = Color.FromHtml("#aa0000"),
                body=$"""
                Could not Login to {DisplayName}, please Login again from the Account Selector
                Err: {loginFailureMessage}
                """
            }]);
        }

        return false;
    }

    public void ForceExpireToken() => authExpiresAt = 0;

    public async Task<string> GenerateExchangeCode()
    {
        var result = await Helpers.MakeRequest(
            HttpMethod.Get,
            FnWebAddresses.account,
            "account/api/oauth/exchange",
            $"consumingClientId=launcherAppClient2",
            accountAuthHeader
        );
        return result["code"].ToString();
    }

    public SemaphoreSlim profileOperationSemaphore { get; private set; } = new(1);

    public async Task<JsonArray> PurchaseOffer(GameOffer offer, int purchaseQuantity = 1)
    {
        JsonObject shopRequestBody = new()
        {
            ["offerId"] = offer.OfferId,
            ["purchaseQuantity"] = purchaseQuantity,
            ["currency"] = offer["prices"][0]["currencyType"].ToString(),
            ["currencySubType"] = offer["prices"][0]["currencySubType"].ToString(),
            ["expectedTotalPrice"] = (await offer.GetPersonalPrice(true, true)).quantity * purchaseQuantity,
            ["gameContext"] = "Pegleg",
        };
        var profile = GetProfile(FnProfileTypes.Common);
        var result = await profile.PerformOperation("PurchaseCatalogEntry", shopRequestBody.ToString());
        offer.NotifyChanged();
        return result;
    }

    public async Task<bool> SetAsActiveAccount() => await SetActiveAccount(accountId);

    void SetAuthentication(JsonNode accountAuthResponse)
    {
        if (accountId is null)
            return;
        authToken = accountAuthResponse["access_token"].ToString();
        SetLocalData("DisplayName", accountAuthResponse["displayName"].ToString());
        accountAuthHeader = new("Bearer", authToken);
        authExpiresAt = Mathf.FloorToInt(Time.GetTicksMsec() * 0.001) + accountAuthResponse["expires_in"].GetValue<int>();
        if (accountAuthResponse["refresh_expires"]?.GetValue<int>() is int refreshExpires)
        {
            refreshExpiresAt = Mathf.FloorToInt(Time.GetTicksMsec() * 0.001) + refreshExpires;
            refreshToken = accountAuthResponse["refresh_token"].ToString();
        }
        loginFailure = false;
        OnAccountUpdated?.Invoke();
    }

    SemaphoreSlim iconSemaphore = new(1);
    public async void UpdateIcon()
    {
        if (iconSemaphore.CurrentCount <= 0)
            return;
        await iconSemaphore.WaitAsync();
        try
        {
            if (!await Authenticate())
                return;

            var avatarData = await Helpers.MakeRequest(
                    HttpMethod.Get,
                    FnWebAddresses.avatar,
                    $"/v1/avatar/fortnite/ids?accountIds={accountId}",
                    "{}",
                    AuthHeader
                );

            string skinId = avatarData[0]?["avatarId"]?.ToString() is string avId ? avId.Split(":")[^1] : null;
            GD.Print($"skinID: {skinId}");
            if (string.IsNullOrWhiteSpace(skinId))
                return;

            var skinData = await Helpers.MakeRequest(
                HttpMethod.Get,
                ExternalWebAddresses.fnApi,
                $"/v2/cosmetics/br/{skinId}",
                "{}",
                null,
                addCosmeticHeader: true
            );

            string skinIconServerPath = null;
            try
            {
                skinIconServerPath =
                    skinData["data"]?["images"]?["icon"]?.ToString() ??
                    skinData["data"]?["images"]?["smallIcon"]?.ToString();
            }
            catch { }
            if (skinIconServerPath is null)
                return;

            var skinIcon = await CatalogRequests.GetCosmeticResource(skinIconServerPath);

            if (skinIcon is null)
                return;
            SetLocalData("IconPath", skinIconServerPath);

            OnAccountUpdated?.Invoke();
        }
        finally
        {
            iconSemaphore.Release();
        }
        
    }

    public async Task SaveDeviceDetails()
    {
        if (!await Authenticate() || GetLocalData("DeviceDetails") is not null)
            return;

        //generate device details
        JsonObject deviceDetails = (await Helpers.MakeRequest(
            HttpMethod.Post,
            FnWebAddresses.account,
            $"account/api/public/account/{accountId}/deviceAuth",
            "",
            AuthHeader,
            ""
        ))?.AsObject();

        SetLocalData("DeviceDetails", new JsonArray(EncryptDeviceDetails(deviceDetails).Select(b => (JsonNode)b).ToArray()));
    }

    public async Task<bool> RemoveDeviceDetails(bool force = false)
    {
        if(!force && !await Authenticate())
        {
            GD.Print("Authentication failed, aborting device detail deletion");
            return false;
        }
        var dd = GetLocalData("DeviceDetails")?.AsArray().Select(n => n.GetValue<byte>()).ToArray();
        if (DecryptDeviceDetails(dd) is JsonObject deviceDetails)
        {
            //tell epic we're not using the device any more. probably unneccecary, but its common courtesy
            var result = await Helpers.MakeRequest(
                HttpMethod.Delete,
                FnWebAddresses.account,
                $"account/api/public/account/{accountId}/deviceAuth/{deviceDetails["deviceId"]}",
                "",
                AuthHeader,
                ""
            );

            if (!force && (result is null || result["errorMessage"] is not null))
            {
                GD.Print($"Could not delete device details: {result["errorMessage"]}");
                return false;
            }

            ClearLocalData("DeviceDetails");
            return true;
        }
        return false;
    }

    JsonObject localData;
    void LoadLocalData()
    {
        if (!isValid || !DirAccess.DirExistsAbsolute(accountDataPath))
        {
            GD.Print("invalid or no folder");
            localData = [];
            return;
        }
        using FileAccess localDataFile = FileAccess.Open($"{accountDataPath}/{accountId}", FileAccess.ModeFlags.Read);
        if (localDataFile is null)
        {
            GD.Print("no file");
            localData = [];
            return;
        }
        var localDataString = localDataFile.GetAsText();

        try
        {
            localData = JsonNode.Parse(localDataString).AsObject();
            return;
        }
        catch (Exception) 
        {
            GD.Print("Warning: Failed to load local data, data may be overwritten");
        }
        localData = [];
    }

    public JsonNode GetLocalData(string key)
    {
        if (localData is null)
            LoadLocalData();
        return localData[key];
    }

    public void ClearLocalData(string key) => SetLocalData(key, null);
    public void SetLocalData(string key, JsonNode value)
    {
        if (localData is null)
            LoadLocalData();
        localData[key] = value.SafeDeepClone();

        if (!isValid || !localData.ContainsKey("DeviceDetails"))
            return;

        if (!DirAccess.DirExistsAbsolute(accountDataPath))
            DirAccess.MakeDirAbsolute(accountDataPath);
        using FileAccess localDataFile = FileAccess.Open($"{accountDataPath}/{accountId}", FileAccess.ModeFlags.Write);
        localDataFile?.StoreString(localData.ToString());
    }

    void DeleteLocalData()
    {
        if (!DirAccess.DirExistsAbsolute(accountDataPath) || !FileAccess.FileExists($"{accountDataPath}/{accountId}"))
            return;
        using DirAccess dir = DirAccess.Open(accountDataPath);
        dir.Remove(accountId);
    }


    public event Action<GameAccount> OnFortStatsChanged;
    FORTStats? fortStats;
    bool fortStatsDirty = true;
    public FORTStats FortStats => GetFORTStats();
    public FORTStats GetFORTStats(bool force = false) => (force || fortStatsDirty) ? CalculateFORTStats() : fortStats ?? CalculateFORTStats();

    FORTStats CalculateFORTStats()
    {
        var accountItems = GetProfile(FnProfileTypes.AccountItems);
        var researchStats = accountItems.statAttributes["research_levels"];
        var statItems = accountItems.GetItems("Stat");
        var equippedWorkerItems = accountItems.GetItems("Worker", item => item.attributes.ContainsKey("squad_id"));

        int LookupStatItem(string statId) => statItems.FirstOrDefault(item => item.templateId == statId)?.quantity ?? 0;

        float LookupWorkers(string squadId)
        {
            var matchingWorkers = equippedWorkerItems
                .Where(item => item.attributes["squad_id"].ToString() == squadId);
            return matchingWorkers.Select(item => item.CalculateSurvivorRating()).Sum();
        }

        //+ profileStats["fortitude"].GetValue<int>()
        float fortitude = LookupStatItem("Stat:fortitude") + LookupStatItem("Stat:fortitude_team") + LookupWorkers("squad_attribute_medicine_trainingteam") + LookupWorkers("squad_attribute_medicine_emtsquad");
        float offense = LookupStatItem("Stat:offense") + LookupStatItem("Stat:offense_team") + LookupWorkers("squad_attribute_arms_fireteamalpha") + LookupWorkers("squad_attribute_arms_closeassaultsquad");
        float resistance = LookupStatItem("Stat:resistance") + LookupStatItem("Stat:resistance_team") + LookupWorkers("squad_attribute_scavenging_scoutingparty") + LookupWorkers("squad_attribute_scavenging_gadgeteers");
        float technology = LookupStatItem("Stat:technology") + LookupStatItem("Stat:technology_team") + LookupWorkers("squad_attribute_synthesis_corpsofengineering") + LookupWorkers("squad_attribute_synthesis_thethinktank");


        GD.Print($"FORTITUDE: {fortitude}");
        GD.Print($"OFFENCE: {offense}");
        GD.Print($"RESISTANCE: {resistance}");
        GD.Print($"TECH: {technology}");

        fortStats = new(fortitude, offense, resistance, technology);
        fortStatsDirty = false;
        OnFortStatsChanged?.Invoke(this);
        return fortStats.Value;
    }

    public async Task GenerateXRayLlamaResults() => await GetProfile(FnProfileTypes.AccountItems).PerformOperation("PopulatePrerolledOffers");

    public async Task<float> GetSurvivorBonus(string bonusID, int perSquadRequirement = 2, float boostBase = 5)
    {
        if(!await Authenticate())
            return 0f;
        var matchingSurvivors = (await GetProfile(FnProfileTypes.AccountItems).Query()).GetItems("Worker", gameItem =>
        {
            if (gameItem.attributes["squad_id"] is null || gameItem.attributes["set_bonus"] is null)
                return false;
            var thisBonus = gameItem.attributes["set_bonus"].ToString().Split(".")[^1];
            return thisBonus == bonusID;
        })
        .GroupBy(gameItem => gameItem.attributes["squad_id"].ToString());

        int boostMatchCount = matchingSurvivors.Select(g => g.Count() / perSquadRequirement).Sum();

        return boostBase * boostMatchCount;
    }

    public async Task<JsonObject> GetOrderCounts(OrderRange range)
    {
        var commonData = await GetProfile(FnProfileTypes.Common).Query();

        var orderRange = commonData.statAttributes[range.ToAttribute()];
        var lastInterval = orderRange?["lastInterval"]?.ToString();
        if (lastInterval is null)
            return null;

        var lastIntervalTime = DateTime.Parse(orderRange["lastInterval"].ToString(), null, DateTimeStyles.RoundtripKind);
        if (lastIntervalTime != range.ToInterval())
            return null;

        return orderRange["purchaseList"].AsObject();
    }

    public async Task<int> GetPurchaseLimit(GameOffer offer)
    {
        int totalLimit = 999;

        if (offer.DailyLimit != -1)
        {
            int purchaseAmount = (await GetOrderCounts(OrderRange.Daily))?[offer.OfferId]?.GetValue<int>() ?? 0;
            //GD.Print($"Daily Limit: {purchaseAmount}/{dailyLimit}");
            totalLimit = Mathf.Min(totalLimit, offer.DailyLimit - purchaseAmount);
        }

        if (totalLimit > 0 && offer.WeeklyLimit != -1)
        {
            int purchaseAmount = (await GetOrderCounts(OrderRange.Weekly))?[offer.OfferId]?.GetValue<int>() ?? 0;
            //GD.Print($"Weekly Limit: {purchaseAmount}/{weeklyLimit}");
            totalLimit = Mathf.Min(totalLimit, offer.WeeklyLimit - purchaseAmount);
        }

        if (totalLimit > 0 && offer.MonthlyLimit != -1)
        {
            int purchaseAmount = (await GetOrderCounts(OrderRange.Monthly))?[offer.OfferId]?.GetValue<int>() ?? 0;
            //GD.Print($"Monthly Limit: {purchaseAmount}/{monthlyLimit}");
            totalLimit = Mathf.Min(totalLimit, offer.MonthlyLimit - purchaseAmount);
        }

        if (totalLimit > 0 && offer.EventLimit != -1)
        {
            var commonData = await GetProfile(FnProfileTypes.Common).Query();
            GameItem eventTracker = commonData.GetItems("EventPurchaseTracker", item =>
                    item.attributes?["event_instance_id"]?.ToString() == offer.EventId
                ).FirstOrDefault();

            int purchaseAmount = eventTracker?.attributes?["event_purchases"]?[offer.OfferId]?.GetValue<int>() ?? 0;
            //GD.Print($"Event Limit: {purchaseAmount}/{eventLimit}");
            totalLimit = Mathf.Min(totalLimit, offer.EventLimit - purchaseAmount);
        }

        //TODO: export and check the items internal purchase limit instead of hardcoding it
        if (offer.itemGrants[0].templateId == "Token:accountinventorybonus")
        {
            var accountItemData = await GetProfile(FnProfileTypes.AccountItems).Query();
            totalLimit = Mathf.Min(totalLimit, 3000 - accountItemData.GetFirstTemplateItem("Token:accountinventorybonus")?.quantity ?? 0);
        }

        if (offer.itemGrants[0].templateId == "CampaignHeroLoadout:purchaseabledefaultloadout")
        {
            var accountItemData = await GetProfile(FnProfileTypes.AccountItems).Query();
            totalLimit = Mathf.Min(totalLimit, 11 - accountItemData.GetTemplateItems("CampaignHeroLoadout:purchaseabledefaultloadout").Length);
        }

        return totalLimit;
    }

    public async Task<int> GetAffordableLimit(GameOffer offer, bool cosmetic = false)
    {
        var pricePerPurchase = cosmetic ? await offer.GetPersonalPrice() : offer.Price;
        if ((pricePerPurchase?.quantity ?? 0) == 0)
            return 999;
        if (cosmetic)
        {
            int vbucks = 0;//put vbucks here
            return Mathf.FloorToInt((float)vbucks / pricePerPurchase.quantity);
        }
        var inInventory = (await GetProfile(FnProfileTypes.AccountItems).Query()).GetFirstTemplateItem(pricePerPurchase.templateId);
        return Mathf.FloorToInt((float)(inInventory?.quantity ?? 0) / pricePerPurchase.quantity);
    }

    public async Task<bool> MatchesFulfillmentRequirements(GameOffer offer)
    {
        if (offer.FulfillmentDenyList.Count > 0)
        {
            var commonData = await GetProfile(FnProfileTypes.Common).Query();
            var fulfillments = commonData.statAttributes["in_app_purchases"]?["fulfillmentCounts"];
            if (offer.FulfillmentDenyList.Any(check => (fulfillments[check.Key]?.GetValue<int>() ?? 0) >= check.Value))
                return false;
        }

        if (offer.FulfillmentRequireList.Count > 0)
        {
            var commonData = await GetProfile(FnProfileTypes.Common).Query();
            var fulfillments = commonData.statAttributes["in_app_purchases"]?["fulfillmentCounts"];
            if (offer.FulfillmentRequireList.Any(check => (fulfillments[check.Key]?.GetValue<int>() ?? 0) < check.Value))
                return false;
        }

        return true;
    }

    public bool MatchesItemRequirements(GameOffer offer) =>
        offer.ItemDenyList.Count == 0 ||
        !offer
        .ItemDenyList
        .Any(check =>
            profiles
            .Select(p =>
                p.Value
                .GetItem(check.Key)?
                .quantity ?? 0
            ).Sum() >= check.Value
        );

    public async Task<string> GetSACCode(bool addExpiredText = true)
    {
        var commonData = await GetProfile(FnProfileTypes.Common).Query();
        var lastSetTime = DateTime.TryParse(commonData.statAttributes["mtx_affiliate_set_time"]?.ToString(), null, DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.UtcNow.AddDays(-15);
        bool isExpired = (DateTime.UtcNow - lastSetTime).Days > 13;
        return commonData.statAttributes["mtx_affiliate"] + (isExpired && addExpiredText ? " (Expired)" : "");
    }

    public async Task<bool> IsSACExpired() => Mathf.FloorToInt(await GetSACTime()) > 13;

    public async Task<double> GetSACTime()
    {
        var commonData = await GetProfile(FnProfileTypes.Common).Query();
        var lastSetTime = DateTime.Parse(commonData.statAttributes["mtx_affiliate_set_time"].ToString(), null, DateTimeStyles.RoundtripKind);
        return (DateTime.UtcNow - lastSetTime).TotalDays;
    }

    public async Task<bool> SetSACCode(string newName)
    {
        await GetProfile(FnProfileTypes.Common).PerformOperation("SetAffiliateName", "{\"affiliateName\":\"" + newName + "\"}");
        //TODO: return false if creator code not found
        return true;
    }

    HashSet<string> bookmarkedItems;

    public void ToggleBookmarked(GameItemTemplate template)
    {
        if (!isOwned || template?.CanBeFavourited != true)
            return;
        if (template.Tier > 1)
        {
            template = GameItemTemplate.Get(TierSubstitute().Replace(template.TemplateId, "_t01"));
        }
        bookmarkedItems ??= GetLocalData("bookmarkedItems")?.Deserialize<HashSet<string>>() ?? [];
        var tid = template.TemplateId.ToLower();
        bool isBookmarked = bookmarkedItems.Contains(tid);
        //GD.Print($"already: {isBookmarked} ({tid})");
        if (isBookmarked)
        {
            var tidSearch = RaritySubstitute().Replace(tid, "_..?_");
            tidSearch = TierSubstitute().Replace(tidSearch, "_t0\\d");
            //GD.Print("reminder removal search " + tidSearch);
            var toRemove = bookmarkedItems.Where(x => Regex.IsMatch(x, tidSearch));
            foreach (var item in toRemove)
            {
                bookmarkedItems.Remove(item);
                GD.Print("reminder removed " + item);
            }
        }
        else
        {
            bookmarkedItems.Add(tid);
            GD.Print("reminder added " + tid);
            while (template.TryGetNextRarity() is GameItemTemplate upgradedTemplate)
            {
                tid = upgradedTemplate.TemplateId.ToLower();
                if (!bookmarkedItems.Contains(tid))
                {
                    bookmarkedItems.Add(tid);
                    GD.Print("reminder added " + tid);
                }
                else
                {
                    break;
                }
                template = upgradedTemplate;
            }
        }
        SetLocalData("bookmarkedItems", JsonSerializer.SerializeToNode(bookmarkedItems));
        BookmarksChanged?.Invoke();
    }

    public bool IsBookmarked(GameItemTemplate template)
    {
        if (template?.CanBeFavourited != true)
            return false;
        bookmarkedItems ??= GetLocalData("bookmarkedItems")?.Deserialize<HashSet<string>>() ?? [];
        var result = bookmarkedItems.Contains(TierSubstitute().Replace(template.TemplateId, "_t01").ToLower());
        return result;
    }

    [GeneratedRegex("_(?:(?:c)|(?:uc)|(?:r)|(?:vr)|(?:sr)|(?:ur))_")]
    private static partial Regex RaritySubstitute();
    [GeneratedRegex("_t0\\d")]
    private static partial Regex TierSubstitute();

    HashSet<string> localPinnedQuests = [];
    DateTime questsLastRefreshedAt = DateTime.MinValue;
    async Task<GameProfile> CheckLocalPinnedQuests()
    {
        var accountItems = GetProfile(FnProfileTypes.AccountItems);
        bool outOfDate = (questsLastRefreshedAt - DateTime.UtcNow).TotalMinutes > 5;
        if (localPinnedQuests != null && !outOfDate)
            return accountItems;

        await accountItems.Query();
        localPinnedQuests = accountItems.statAttributes["client_settings"]["pinnedQuestInstances"].Deserialize<HashSet<string>>();
        return accountItems;
    }

    public async Task ClientQuestLogin()
    {
        await GetProfile(FnProfileTypes.AccountItems)
            .PerformOperation("ClientQuestLogin", @"{""streamingAppKey"": """"}");
    }

    public async Task AddPinnedQuest(GameItem item)
    {
        var accountItems = await CheckLocalPinnedQuests();
        if (item.profile == accountItems && !localPinnedQuests.Contains(item.uuid))
        {
            localPinnedQuests.Add(item.uuid);
            accountItems.SendItemUpdate(item);
            SendLocalPinnedQuests(accountItems);
        }
    }

    public async Task RemovePinnedQuest(GameItem item)
    {
        var accountItems = await CheckLocalPinnedQuests();
        if (localPinnedQuests.Contains(item.uuid))
        {
            localPinnedQuests.Remove(item.uuid);
            accountItems.SendItemUpdate(item);
            SendLocalPinnedQuests(accountItems);
        }
    }

    public void ClearPinnedQuests()
    {
        GD.Print("clearing all pinned");
        var unpinnedQuests = localPinnedQuests?.ToArray() ?? [];
        localPinnedQuests?.Clear();
        var accountItems = GetProfile(FnProfileTypes.AccountItems);
        foreach (var item in unpinnedQuests)
        {
            accountItems.SendItemUpdate(accountItems.GetItem(item));
        }
        SendLocalPinnedQuests(accountItems);
    }

    async void SendLocalPinnedQuests(GameProfile accountItems)
    {
        JsonObject content = new()
        {
            ["pinnedQuestIds"] = new JsonArray(localPinnedQuests.Select(q => (JsonValue)q).ToArray())
        };
        await accountItems.PerformOperation("SetPinnedQuests", content.ToString());
    }

    public bool HasPinnedQuest(GameItem item) => localPinnedQuests.Contains(item.uuid);

    public async Task<GameItem> RerollQuest(GameItem item)
    {
        var accountItems = GetProfile(FnProfileTypes.AccountItems);
        if (item.profile != accountItems)
            return null;
        GD.Print("rerolling quest " + item.uuid);
        JsonObject content = new()
        {
            ["questId"] = item.uuid
        };
        var notif = (await accountItems.PerformOperation("FortRerollDailyQuest", content.ToString()))
            .FirstOrDefault(n => n["type"].ToString() == "dailyQuestReroll");
        if (notif is null)
            return null;
        return accountItems.GetItems("Quest", item => item.templateId==notif["newQuestId"].ToString()).FirstOrDefault();
    }

    public bool CanRerollQuest() => (GetProfile(FnProfileTypes.AccountItems).statAttributes?["quest_manager"]?["dailyQuestRerolls"]?.GetValue<int>() ?? 0) > 0;
}

public class GameProfile
{
    public GameProfile(GameAccount account, string profileId)
    {
        this.account = account;
        this.profileId = profileId;
    }

    public void InvalidateProfile()
    {
        rvn = -1;
        foreach (var item in items.Values)
        {
            item.DisconnectFromProfile();
        }
        items.Clear();
        groupedItems.Clear();
        statAttributes = null;
    }

    int rvn = -1;
    public bool hasProfile => rvn >= 0;
    public GameAccount account { get; private set; }
    public string profileId { get; private set; }
    public JsonObject statAttributes { get; private set; }

    Dictionary<string, GameItem> items = [];
    Dictionary<string, List<GameItem>> groupedItems = [];

    public event Action OnStatsChanged;
    public event Action<string> OnStatChanged;
    public event Action<GameItem> OnItemAdded;
    public event Action<GameItem> OnItemUpdated;
    public event Action<GameItem> OnItemRemoved;
    public event Action<string, GameItem> OnItemReassociated;

    IEnumerable<GameItem> GetItemSubset(string type)
    {
        if (type is null)
            return items.Values;
        if (!groupedItems?.ContainsKey(type) ?? false)
            return Array.Empty<GameItem>();
        return groupedItems[type];
    }

    public GameItem GetItem(string uuid) => items.ContainsKey(uuid) ? items[uuid] : null;
    public GameItem[] GetItems(Predicate<GameItem> predicate) => GetItems(null, predicate);
    public GameItem[] GetItems(string type = null, Predicate<GameItem> predicate = null)
    {
        var typedItems = GetItemSubset(type);
        if (predicate is null)
            return typedItems.ToArray();
        return typedItems.Where(predicate.ToFunc()).ToArray();
    }

    public GameItem[] GetTemplateItems(string templateId = null, Predicate<GameItem> predicate = null)
    {
        string type = templateId.Split(":")[0];
        return GetItems(type, item => item.templateId == templateId && predicate.Try(item));
    }

    public GameItem GetFirstItem(Predicate<GameItem> predicate) => GetFirstItem(null, predicate);
    public GameItem GetFirstItem(string type=null, Predicate<GameItem> predicate = null)
    {
        var typedItems = GetItemSubset(type);
        if (predicate is null)
            return typedItems.FirstOrDefault();
        return typedItems.FirstOrDefault(predicate.ToFunc());
    }
    public GameItem GetFirstTemplateItem(string templateId, Predicate<GameItem> predicate = null)
    {
        if (templateId is null)
            return null;
        string type = templateId?.Split(":")[0];
        return GetFirstItem(type, item => item?.templateId == templateId && predicate.Try(item));
    }

    public void SendItemUpdate(GameItem item)
    {
        if (item?.profile == this)
        {
            OnItemUpdated?.Invoke(item);
            item.NotifyChanged();
        }
    }

    public async Task<GameProfile> Query(bool force = false)
    {
        var queryAccount = account.isOwned ? account : GameAccount.activeAccount;
        if (queryAccount is null)
            return this;

        await queryAccount.profileOperationSemaphore.WaitAsync();
        try
        {
            if (!hasProfile || force)
            {
                await PerformOperationUnsafe("QueryProfile");
            }
            return this;
        }
        finally
        {
            queryAccount.profileOperationSemaphore.Release();
        }
    }

    //should only be used after manually entering the profileOperationSemaphore (and validating account existance), such as in GameAccount.QueryAllProfiles()
    public async Task<GameProfile> QueryUnsafe(bool force = false)
    {
        if (!hasProfile || force)
        {
            await PerformOperationUnsafe("QueryProfile");
        }
        return this;
    }

    public async Task<JsonArray> PerformOperation(string operation, JsonNode content)=>
        await PerformOperation(operation, content.ToString());

    public async Task<JsonArray> PerformOperation(string operation, string content = "{}")
    {
        var opAccount = account.isOwned ? account : GameAccount.activeAccount;
        if (opAccount is null)
            return null;
        await opAccount.profileOperationSemaphore.WaitAsync();
        try
        {
            return await PerformOperationUnsafe(operation, content);
        }
        finally
        {
            opAccount.profileOperationSemaphore.Release();
        }
    }

    public void MarkItemsSeen(GameItem[] items)
    {
        var filteredItems = items.Where(i => i.profile == this && i.template is not null).ToArray();
        if (
            filteredItems.Length == 0 ||
            !(
                profileId == FnProfileTypes.AccountItems ||
                profileId == FnProfileTypes.Common ||
                profileId == FnProfileTypes.CosmeticInventory
            )
        )
            return;
        for (int i = 0; i < filteredItems.Length; i++)
        {
            filteredItems[i].SetSeenLocal(true);
        }
        string content = @$"{{""itemIds"": [{string.Join(", ", filteredItems.Select(i => @$"""{i.uuid}"""))}]}}";
        PerformOperation("MarkItemSeen", content).StartTask();
    }

    public JsonObject lastOp { get; private set; }
    async Task<JsonArray> PerformOperationUnsafe(string operation, string content = "{}")
    {
        if (!account.isOwned)
        {
            if (operation == "QueryProfile")
                operation = "QueryPublicProfile";

            if (operation != "QueryPublicProfile")
            {
                GD.Print($"cannot perform \"{operation}\" on unowned profile");
                return null;
            }

            if (profileId != FnProfileTypes.AccountItems && profileId != FnProfileTypes.Common)
            {
                GD.Print($"cannot access unowned profile of type \"{profileId}\"");
                return null;
            }
        }

        AuthenticationHeaderValue authHeader;
        if (!account.isOwned && await GameAccount.activeAccount.Authenticate())
        {
            authHeader = GameAccount.activeAccount.AuthHeader;
        }
        else if (account.isOwned && await account.Authenticate())
        {
            authHeader = account.AuthHeader;
        }
        else
        {
            GD.Print($"Authentication failed");
            return null;
        }

        if (operation == "MarkItemSeen")
        {
            var targetItemIDs = JsonNode.Parse(content)["itemIds"].AsArray().Select(n => n.ToString());
            var targetItems = targetItemIDs.Select(uuid => items.TryGetValue(uuid, out var item) ? item : null).Where(x => x != null);
            bool hasUnseen = false;
            foreach (var item in targetItems)
            {
                if (item.attributes["item_seen"] is null)
                {
                    item.SetSeenLocal();
                    OnItemUpdated?.Invoke(item);
                    hasUnseen = true;
                }
            }
            if (!hasUnseen)
                return null;
        }

        StringContent jsonContent = new(content, Encoding.UTF8, "application/json");
        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                $"fortnite/api/game/v2/profile/{account.accountId}/{(account.isOwned ? "client" : "public")}/{operation}?profileId={profileId}&rvn={rvn}"
            )
            {
                Content = jsonContent
            };
        request.Headers.Authorization = authHeader;

        var result = (await Helpers.MakeRequest(FnWebAddresses.game, request))?.AsObject();
        lastOp = result;
        if(result is null)
            return null;
        if (result.ContainsKey("errorCode"))
        {
            var _ = GenericConfirmationWindow.ShowErrorForWebResult(result);
            return null;
        }

        var changes = result["profileChanges"]?.AsArray();

        if (changes is null)
        {
            GD.Print($"unknown profile op result: {result}");
            return null;
        }
        if (changes.Count == 0) { }
        else if (changes[0]["changeType"].ToString() == "fullProfileUpdate")
        {
            GD.Print("FULLUPDATE: " + profileId);
            var resultItems = result["profileChanges"][0]["profile"]["items"].AsObject();
            var resultStats = result["profileChanges"][0]["profile"]["stats"]["attributes"].AsObject();
            if (hasProfile)
                ApplyProfileChanges(GenerateChanges(resultItems, resultStats));
            else
            {
                items = resultItems.Select(kvp => new GameItem(this, kvp.Key, kvp.Value.AsObject())).ToDictionary(item => item.uuid);
                groupedItems = new(
                    items.GroupBy(kvp => kvp.Value.templateId.Split(":")[0])
                    .Select(grouping => KeyValuePair.Create(grouping.Key, grouping.Select(kvp => kvp.Value).ToList()))
                );
                statAttributes = resultStats;
            }
        }
        else
        {
            if (operation == "UpgradeItemBulk" || operation == "UpgradeItemRarity")
            {
                JsonNode additionChange = changes.FirstOrDefault(n => n["changeType"].ToString() == "itemAdded");
                JsonNode removalChange = changes.FirstOrDefault(n => n["changeType"].ToString() == "itemRemoved");
                if(additionChange is not null && removalChange is not null)
                {
                    var additionIndex = changes.IndexOf(additionChange);
                    var removingId = removalChange["itemId"].ToString();
                    List<JsonNode> irrelevantChanges = [];
                    for (int i = additionIndex; i < changes.Count; i++)
                    {
                        var change = changes[i];
                        if (change["itemId"].ToString()==removingId)
                            irrelevantChanges.Add(change);
                    }
                    foreach (var change in irrelevantChanges)
                    {
                        changes.Remove(change);
                    }
                    additionChange["newItemId"] = additionChange["itemId"].ToString();
                    additionChange["itemId"] = removingId;
                    additionChange["changeType"] = "itemUpgraded";
                }
            }
            ApplyProfileChanges(changes);
        }
        rvn = result["profileRevision"].GetValue<int>();

        if (result.ContainsKey("multiUpdate"))
        {
            foreach (var profileUpdate in result["multiUpdate"].AsArray())
            {
                string profileId = profileUpdate["profileId"].ToString();
                if (account.HasProfile(profileId))
                {
                    var profile = account.GetProfile(profileId);
                    profile.ApplyProfileChanges(profileUpdate["profileChanges"].AsArray());
                    profile.rvn = profileUpdate["profileRevision"].GetValue<int>();
                }
            }
        }
        printChanges = false;

        GD.Print($"operation complete ({operation} in {profileId} as {account.DisplayName})");

        if (result.ContainsKey("notifications"))
            return result["notifications"].AsArray();
        return [];
    }

    JsonArray GenerateChanges(JsonObject newItems, JsonObject newStats = null)
    {
        var oldKeys = items.Keys.ToArray();
        var newKeys = newItems.Select(x => x.Key).ToArray();

        var addedKeys = newKeys.Except(oldKeys);
        var removedKeys = oldKeys.Except(newKeys);
        var possiblyChangedKeys = oldKeys.Intersect(newKeys);

        JsonArray profileChanges = [];

        foreach (var itemKey in removedKeys)
        {
            profileChanges.Add(new JsonObject()
            {
                ["changeType"] = "itemRemoved",
                ["itemId"] = itemKey
            });
        }
        foreach (var itemKey in possiblyChangedKeys)
        {
            var from = items[itemKey].SimpleRawData.ToString();
            var to = newItems[itemKey].ToString();
            if (from != to)
            {
                GD.Print($"FROM ({from}) >>> ({to})");
                profileChanges.Add(new JsonObject()
                {
                    ["changeType"] = "itemFullyChanged",
                    ["itemId"] = itemKey,
                    ["item"] = newItems[itemKey].SafeDeepClone()
                });
            }
        }
        foreach (var itemKey in addedKeys)
        {
            profileChanges.Add(new JsonObject()
            {
                ["changeType"] = "itemAdded",
                ["itemId"] = itemKey,
                ["item"] = newItems[itemKey].SafeDeepClone()
            });
        }
        foreach (var statKVP in newStats)
        {
            if (!statAttributes.TryGetPropertyValue(statKVP.Key, out var statVal) || statKVP.Value.ToString() != statVal.ToString())
            {
                profileChanges.Add(new JsonObject()
                {
                    ["changeType"] = "statModified",
                    ["name"] = statKVP.Key,
                    ["value"] = statVal.SafeDeepClone()
                });
            }
        }

        return profileChanges;
    }

    public bool printChanges = false;

    public void ApplyProfileChanges(JsonArray profileChanges)
    {
        List<GameItem> itemsToNotify = [];
        List<GameItem> itemsToIgnore = [];
        Dictionary<string, GameItem> reassociations = [];
        foreach (var change in profileChanges)
        {
            string changeType = change["changeType"].ToString();
            string uuid = change["itemId"]?.ToString();

            //ProfileItemId profileItemId = new(profileId, uuid);
            GameItem targetItem;
            switch (changeType)
            {
                case "itemAdded":
                    targetItem = new(this, uuid, change["item"].AsObject());
                    lock (items)
                    {
                        items[uuid] = targetItem;
                    }
                    lock (groupedItems)
                    {
                        groupedItems.TryAdd(targetItem.templateId.Split(":")[0], []);
                        groupedItems[targetItem.templateId.Split(":")[0]].Add(targetItem);
                    }
                    if (printChanges)
                        GD.Print($"ADDED: {items[uuid]}");
                    OnItemAdded?.Invoke(targetItem);
                    break;
                case "itemRemoved":
                    if (!items.ContainsKey(uuid))
                        continue;
                    targetItem = items[uuid];
                    if (printChanges)
                        GD.Print($"REMOVING: {targetItem}");
                    targetItem.NotifyRemoving();
                    lock (items)
                    {
                        items.Remove(uuid);
                    }
                    lock (groupedItems)
                    {
                        if (groupedItems[targetItem.templateId.Split(":")[0]] is List<GameItem> list && list.Contains(targetItem))
                            list.Remove(targetItem);
                    }
                    itemsToIgnore.Add(targetItem);
                    OnItemRemoved?.Invoke(targetItem);
                    targetItem.DisconnectFromProfile();
                    break;
                case "statModified":
                    var statName = change["name"].ToString();
                    var statVal = change["value"].SafeDeepClone();
                    var hadStat = statAttributes.TryGetPropertyValue(statName, out var oldStatVal);

                    statAttributes[statName] = statVal;

                    var statValText = statVal.ToString();
                    if (statValText.Length > 500)
                        statValText = "[500+]";

                    string oldStatValText = null;
                    if (hadStat)
                    {
                        oldStatValText = oldStatVal.ToString();
                        if (oldStatValText.Length > 500)
                            oldStatValText = "[500+]";
                    }
                    if (printChanges)
                        GD.Print($"STAT CHANGED: {statName}: {(hadStat ? $"\n{oldStatValText}\n=>\n" : "")}{statValText}");
                    OnStatChanged?.Invoke(statName);
                    OnStatsChanged?.Invoke();

                    break;
                case "itemQuantityChanged":
                    targetItem = items[uuid];
                    var oldQuantity = targetItem.quantity;
                    targetItem.SetLocalQuantity(change["quantity"].GetValue<int>());
                    if (printChanges)
                        GD.Print($"CHANGED (quantity): {uuid} ({oldQuantity} => {targetItem.quantity})");
                    itemsToNotify.Add(targetItem);
                    break;
                case "itemAttrChanged":
                    targetItem = items[uuid];
                    var oldValue = targetItem.attributes[change["attributeName"].ToString()]?.ToString();
                    targetItem.attributes[change["attributeName"].ToString()] = change["attributeValue"].SafeDeepClone();
                    if (printChanges)
                        GD.Print($"CHANGED (attribute): {uuid}[{change["attributeName"]}] \n{oldValue}\n=>\n{change["attributeValue"]}");
                    itemsToNotify.Add(targetItem);
                    break;
                case "itemUpgraded":
                    //reassociates an item that was removed and readded as part of an upgrade
                    targetItem = items[uuid];
                    targetItem.Reassociate(change["newItemId"]?.ToString(), change["item"].AsObject());
                    if (printChanges)
                        GD.Print($"UPGRADED: {items[uuid]}");
                    itemsToNotify.Add(targetItem);
                    reassociations.Add(uuid, targetItem);
                    break;
                case "itemFullyChanged":
                    //autogenerated item changes
                    targetItem = items[uuid];
                    targetItem.SetRawData(change["item"].AsObject());
                    if (printChanges)
                        GD.Print($"CHANGED (full): {targetItem}");
                    itemsToNotify.Add(targetItem);
                    break;
            }
        }
        itemsToNotify.RemoveAll(i => itemsToIgnore.Contains(i));
        foreach (var item in itemsToNotify)
        {
            if (printChanges)
                GD.Print($"Notifying : {item.uuid}");
            item.NotifyChanged();
            OnItemUpdated?.Invoke(item);
        }
        foreach (var kvp in reassociations)
        {
            if (printChanges)
                GD.Print($"Reassociating : {kvp.Key} => {kvp.Value.uuid}");
            OnItemReassociated?.Invoke(kvp.Key, kvp.Value);
        }
    }
}

public class GameItem
{
    #region Statics

    static Dictionary<string, string> zcpEquivelents = new()
    {
        ["CardPack:zcp_reagent_c_t04\\w*"] = "AccountResource:reagent_c_t04",
        ["CardPack:zcp_reagent_c_t03\\w*"] = "AccountResource:reagent_c_t03",
        ["CardPack:zcp_reagent_c_t02\\w*"] = "AccountResource:reagent_c_t02",
        ["CardPack:zcp_reagent_c_t01\\w*"] = "AccountResource:reagent_c_t01",

        ["CardPack:zcp_reagent_alteration_upgrade_sr\\w*"] = "AccountResource:reagent_alteration_upgrade_sr",
        ["CardPack:zcp_reagent_alteration_upgrade_vr\\w*"] = "AccountResource:reagent_alteration_upgrade_vr",
        ["CardPack:zcp_reagent_alteration_upgrade_r\\w*"] = "AccountResource:reagent_alteration_upgrade_r",
        ["CardPack:zcp_reagent_alteration_upgrade_uc\\w*"] = "AccountResource:reagent_alteration_upgrade_uc",
        ["CardPack:zcp_reagent_alteration_generic\\w*"] = "AccountResource:reagent_alteration_generic",

        ["CardPack:zcp_phoenixxp\\w*"] = "AccountResource:phoenixxp",
        ["CardPack:zcp_personnelxp\\w*"] = "AccountResource:personnelxp",
        ["CardPack:zcp_heroxp\\w*"] = "AccountResource:heroxp",
        ["CardPack:zcp_schematicxp\\w*"] = "AccountResource:schematicxp",

        ["CardPack:zcp_ore_copper\\w*"] = "Ingredient:ingredient_ore_copper",
        ["CardPack:zcp_ore_silver\\w*"] = "Ingredient:ingredient_ore_silver",
        ["CardPack:zcp_ore_malachite\\w*"] = "Ingredient:ingredient_ore_malachite",
        ["CardPack:zcp_ore_obsidian\\w*"] = "Ingredient:ingredient_ore_obsidian",
        ["CardPack:zcp_ore_brightcore\\w*"] = "Ingredient:ingredient_ore_brightcore",

        ["CardPack:zcp_crystal_quartz\\w*"] = "Ingredient:ingredient_crystal_quartz",
        ["CardPack:zcp_crystal_shadowshard\\w*"] = "Ingredient:ingredient_crystal_shadowshard",
        ["CardPack:zcp_crystal_sunbeam\\w*"] = "Ingredient:ingredient_crystal_sunbeam",

        ["CardPack:zcp_improvised_r"] = "Ingredient:ingredient_rare_mechanism",
        ["CardPack:zcp_improvised_vr"] = "Ingredient:ingredient_rare_powercell",

        ["CardPack:zcp_eventscaling\\w*"] = "AccountResource:eventcurrency_scaling",
    };

    static GameItem FindZcpEquivelent(string templateId)
    {
        if (!(templateId?.StartsWith("CardPack:zcp_") ?? false))
            return null;
        foreach (var equivelent in zcpEquivelents)
        {
            if (Regex.Match(templateId, equivelent.Key).Success)
            {
                GameItem equivelentItem = GameItemTemplate.Get(equivelent.Value).CreateInstance();
                equivelentItem.SetSeenLocal();
                equivelentItem.GetSearchTags();
                return equivelentItem;
            }
        }
        return null;
    }

    #endregion

    public event Action OnChanged;
    public event Action OnRemoving;
    public event Action OnRemoved;

    public GameItem(GameProfile profile, string uuid, JsonObject rawData)
    {
        this.uuid = uuid;
        this.profile = profile;
        SetRawData(rawData);
    }

    public void Reassociate(string newUUID, JsonObject rawData)
    {
        uuid = newUUID;
        SetRawData(rawData);
    }

    public GameItem(GameItemTemplate template, int quantity, JsonObject attributes = null, GameItem inspectorOverride = null, JsonObject customData = null, string templateId = null)
    {
        _template = template;
        this.templateId = template?.TemplateId ?? templateId;
        upgradeBasis = template;
        this.quantity = quantity;
        this.attributes = attributes;
        this.customData = customData ?? [];
        this.inspectorOverride = inspectorOverride;
        zcpEquivelent = FindZcpEquivelent(templateId);
        isSeenLocal = true;
    }

    public GameProfile profile { get; private set; }
    public string uuid { get; private set; }

    public GameItemTemplate sortingTemplate => zcpEquivelent?.template ?? template;
    public GameItem zcpEquivelent { get; private set; }
    public GameItem inspectorOverride { get; private set; }

    public string templateId { get; private set; }
    GameItemTemplate _template;
    public GameItemTemplate template => _template ??= GameItemTemplate.Get(templateId);

    public JsonObject attributes { get; private set; }
    public JsonObject customData { get; private set; } = [];

    public int quantity { get; private set; }
    public void SetLocalQuantity(int newQuant) =>  quantity = newQuant;

    public int TotalQuantity
    {
        get
        {
            if (profile is null)
                return quantity;
            return profile.GetTemplateItems(templateId).Select(i => i.quantity).Sum();
        }
    }

    JsonObject _rawData;
    public JsonObject RawData => _rawData ?? GenerateRawData();
    public JsonObject GenerateRawData()
    {
        var templateData = template?.rawData.SafeDeepClone();
        templateData ??= [];
        templateData["searchTags"] = null;
        templateData.Remove("searchTags");
        _rawData = new()
        {
            ["uuid"] = uuid,
            ["templateId"] = templateId,
            ["attributes"] = attributes?.SafeDeepClone(),
            ["quantity"] = quantity,
            ["template"] = templateData,
            ["searchTags"] = GetSearchTags()?.SafeDeepClone(),
        };
        if (profile is null)
            _rawData.Remove("uuid"); //doing this backwards to make uuid the first property of rawData
        if(customData is not null)
            _rawData["custom"] = customData.SafeDeepClone();
        if(zcpEquivelent is not null)
            _rawData["bundleItem"] = zcpEquivelent.template.rawData.SafeDeepClone();
        return _rawData;
    }
    public JsonArray Alterations => (attributes?["alterations"] ?? attributes?["alterationDefinitions"])?.AsArray();

    public string Personality=> attributes?["personality"]?.ToString() is string rawPersonality ? ParseSurvivorAttribute(rawPersonality) : null;
    public string SetBonus => attributes?["set_bonus"]?.ToString() is string rawSetBonus ? ParseSurvivorAttribute(rawSetBonus) : null;

    public GameItem[] CardPackChoices => attributes?["options"]?
                .AsArray()
                .Select(c =>
                {
                    var template = GameItemTemplate.Get(c["itemType"].ToString());
                    var choiceItem = template.CreateInstance(c["quantity"].GetValue<int>(), c["attributes"]?.AsObject().SafeDeepClone());
                    choiceItem.SetRewardNotification(null, true);
                    return choiceItem;
                })
                .ToArray();
    static string ParseSurvivorAttribute(string survivorAttr)
    {
        survivorAttr = survivorAttr.Split(".")[^1][2..];
        if (survivorAttr.EndsWith("Low"))
            survivorAttr = survivorAttr[..^3];
        if (survivorAttr.EndsWith("High"))
            survivorAttr = survivorAttr[..^4];
        return Regex.Replace(survivorAttr, "[A-Z]", " $&").Trim();
    }

    JsonArray _searchTags;
    public JsonArray GetSearchTags(bool assumeUncommon = true)
    {
        if(_searchTags is not null)
            return _searchTags;
        if(zcpEquivelent is not null)
        {
            _searchTags = zcpEquivelent.GetSearchTags(assumeUncommon);
            _searchTags.Add("Bundle");
            return _searchTags;
        }
        _searchTags = template?.GenerateSearchTags(assumeUncommon)?.SafeDeepClone();
        if (_searchTags is null)
            return [.. templateId.Split(":")];
        if (attributes?["inventory_overflow_date"] is not null)
            _searchTags.Add("Overflow");
        if (attributes?["personality"]?.ToString() is string rawPersonality)
            _searchTags.Add(ParseSurvivorAttribute(rawPersonality));
        if (attributes?["set_bonus"]?.ToString() is string rawSetBonus)
            _searchTags.Add(ParseSurvivorAttribute(rawSetBonus));
        if (attributes?["quest_state"]?.ToString() is string questState)
            _searchTags.Add(questState);
        return _searchTags;
    }

    public JsonObject SimpleRawData => new()
    {
        ["templateId"] = templateId,
        ["attributes"] = attributes?.SafeDeepClone(),
        ["quantity"] = quantity,
    };

    public void SetRawData(JsonObject rawData)
    {
        var newTemplate = rawData["templateId"]?.ToString() ?? rawData["itemType"]?.ToString();
        if (templateId != newTemplate)
        {
            _template = null;
            templateId = newTemplate;
            zcpEquivelent = FindZcpEquivelent(templateId);
        }
        quantity = rawData["quantity"].GetValue<int>();
        attributes = rawData["attributes"]?.AsObject();
        isSeenLocal = null;
        customData = [];
        ResetCachedData();
    }

    void ResetCachedData()
    {
        _rawData = null;
        _rating = null;
        _searchTags = null;
        textures.Clear();
    }

    bool? isFavouritedLocal = null;
    public bool IsFavourited => isFavouritedLocal ?? attributes?["favorite"]?.GetValue<bool>() == true;
    public void SetFavouritedLocal(bool? newVal)
    {
        if (isFavouritedLocal == newVal || template?.CanBeFavourited != true)
            return;
        bool realVal = attributes?["favorite"]?.GetValue<bool>() ?? false;
        bool update = (newVal ?? realVal) != (isFavouritedLocal ?? realVal);
        isFavouritedLocal = newVal;
        if (update)
            NotifyChanged();
    }

    public void SetFavourited(bool newVal)
    {
        if (template?.CanBeFavourited != true)
            return;
        SetFavouritedLocal(newVal);
        string content = @$"{{""targetItemId"": ""{uuid}"", ""bFavorite"":{newVal.ToString().ToLower()}}}";
        GD.Print("FAV: " + content);
        profile.PerformOperation("SetItemFavoriteStatus", content).StartTask();
    }

    bool? isSeenLocal = null;
    public bool IsSeen => isSeenLocal ?? attributes?["item_seen"]?.GetValue<bool>() ?? false || template?.CanBeUnseen != true;
    public void SetSeenLocal(bool? newVal = true)
    {
        if (isSeenLocal == newVal || template?.CanBeUnseen != true)
            return;
        bool realVal = attributes?["item_seen"]?.GetValue<bool>() ?? false;
        bool update = (newVal ?? realVal) != (isSeenLocal ?? realVal);
        isSeenLocal = newVal;
        if (update)
            NotifyChanged();
    }

    public void MarkItemSeen()
    {
        if (attributes?["item_seen"] is not null || template?.CanBeUnseen != true)
            return;
        SetSeenLocal();
        string content = @$"{{""itemIds"": [""{uuid}""]}}";
        profile.PerformOperation("MarkItemSeen", content).StartTask();
    }

    public async void SetRewardNotification(GameAccount account = null, bool force = false)
    {
        account ??= GameAccount.activeAccount;
        if (profile is not null || (!force && isSeenLocal != null))
            return;

        if(!IsSeen)
            SetSeenLocal(true);

        bool exists = await SetCollected(account) ?? true;

        if (!exists)
        {
            var accountItems = await account.GetProfile(FnProfileTypes.AccountItems).Query();
            exists = accountItems
            .GetItems(template.Type, item =>
                item.template?.DisplayName == (template?.DisplayName ?? "nope") &&
                item.template?.RarityLevel >= template?.RarityLevel)
            .Any();
        }

        if (!exists)
            SetSeenLocal(false);
    }

    public bool? isCollectedCache { get; private set; }
    public async Task<bool?> SetCollected(GameAccount account = null)
    {
        if (template?.IsCollectable != true)
            return null;

        account ??= profile?.account ?? GameAccount.activeAccount;
        await account.GetProfile(template.CollectionProfile).Query();

        var collectionBook = account.GetProfile(template.CollectionProfile);
        if (template.Type == "Worker")
        {
            if (template.Name.StartsWith("workerhalloween"))
            {
                //with costume party attendees, 3 of each rarity can be collected
                return collectionBook
                    .GetItems("Worker", item =>
                        item.template.Name.StartsWith("workerhalloween") &&
                        item.template.Rarity == template.Rarity)
                    .Length < 3;
            }
            else if (template.SubType is not null)
            {
                //with mythic lead survivors, one of each unique lead can be collected
                if (template.Rarity == "Mythic")
                    return collectionBook
                        .GetItems("Worker", item => item.templateId == templateId)
                        .Any();
                //with regular lead survivors, one of each subtype-rarity combo can be collected
                else
                    return collectionBook
                        .GetItems("Worker", item =>
                            item.template.SubType == template.SubType &&
                            item.template.Rarity == template.Rarity)
                        .Any();
            }
            //with regular survivors, one of personality-rarity combo can be collected
            return collectionBook
                .GetItems("Worker", item =>
                    item.attributes?["personality"]?.ToString() == (attributes?["personality"]?.ToString() ?? "nope") &&
                    item.template.Rarity == template.Rarity)
                .Any();
        }
        var result = collectionBook
            .GetItems(template.Type, item => item.templateId == templateId)
            .Any();
        if (isCollectedCache != result)
        {
            isCollectedCache = result;
            NotifyChanged();
        }
        return result;
    }

    public float GetHeroStat(string stat, int givenLevel = 0, int givenTier = 0)
    {
        if (PegLegResourceManager.HeroStats is not JsonObject stats)
            return 0;

        if (givenLevel <= 0)
        {
            givenLevel = attributes?["level"]?.GetValue<int>() ?? 1;
            givenTier = template.Tier;
        }

        string heroStatLine = template["HeroStatLine"].ToString();
        string heroRarityAndTier = template.GetCompactRarityAndTier(givenTier);
        var statLookup = stats["Types"]?[$"{template.SubType}_{heroStatLine}"]?[heroRarityAndTier]?[stat]?.AsObject();
        if (statLookup is null)
            return 0;
        int statKey = Mathf.Clamp(givenLevel - (int)statLookup["FirstLevel"], 0, statLookup["Values"].AsArray().Count - 1);
        return (float)statLookup["Values"][statKey];
    }

    public bool QuestPinned => profile?.account.HasPinnedQuest(this) ?? false;
    public string QuestState => attributes?["quest_state"]?.ToString();
    public bool QuestComplete => QuestState == "unknown" || QuestState == "Claimed"; //TODO: need to check which state value refers to unclaimed complete quests

    public void ClearRating() => _rating = null;
    int? _rating;
    public int Rating => _rating ??= CalculateRating();
    public int UpdateRating() => (_rating = CalculateRating()) ?? 0;

    public int CalculateRating()
    {
        if (PegLegResourceManager.ItemRatings is not JsonObject ratings)
            return 0;
        if (template is null)
            return 0;
        var tier = template.Tier;
        if (template.Type == "Schematic" && !(template.Category == "Ingredient" || template.Category == "Ammo") && tier == 0)
            tier = 1;
        if (tier == 0)
            return 0;

        var level = attributes?["level"]?.GetValue<int>() ?? 0;
        var bonusMax = attributes?["max_level_bonus"]?.GetValue<int>() ?? 0;
        if (!template.CanBeLeveled && tier == 5) //crafted weapons and traps dont have max_level_bonus attribute
            bonusMax = 10;
        level = Mathf.Clamp(level, Mathf.Min(1, (tier * 10) - 10), (tier * 10) + bonusMax);
        string ratingCategory = template.Type == "Worker" ? (template.SubType is null ? "Survivor" : "LeadSurvivor") : "Default";

        string ratingKey = template.GetCompactRarityAndTier(tier);
        if (ratingCategory == "LeadSurvivor")
            ratingKey = ratingKey.Replace("UR_", "SR_");

        var ratingSet = ratings[ratingCategory]["Tiers"][ratingKey];
        if (ratingSet is null)
        {
            GD.Print($"no rating set {ratingCategory}:{ratingKey}");
            return 0;
        }
        int subLevel = level - ratingSet["FirstLevel"].GetValue<int>();
        if (subLevel < 0)
            return 0;
        return (int)ratingSet["Ratings"][subLevel].GetValue<float>();
    }

    public int CalculateSurvivorRating(bool useSquad = true, string survivorSquad = null)
    {
        var rating = Rating;
        survivorSquad ??= attributes?["squad_id"]?.ToString();
        if (!useSquad || rating == 0 || (template.Type != "Worker" && survivorSquad is null))
            return rating;

        if (template.SubType is string leadType)
        {
            //check for lead synergy match
            var matchedSquadID = PegLegResourceManager.supplimentaryData.SynergyToSquadId.TryGetValue(leadType.Replace(" ", ""), out var match) ? match : null;
            if (matchedSquadID == survivorSquad)
                rating *= 2;
        }
        else if (profile?.profileId == FnProfileTypes.AccountItems)
        {
            var leadSurvivor = profile.GetItems("Worker", item =>
                item.attributes?["squad_id"]?.ToString() == survivorSquad &&
                item.attributes["squad_slot_idx"].GetValue<int>() == 0
            ).FirstOrDefault();

            string leaderRarity = leadSurvivor?.template.Rarity ?? "";
            int rarityBoost = leaderRarity switch
            {
                "Mythic" => 8,
                "Legendary" => 8,
                "Epic" => 5,
                "Rare" => 4,
                "Uncommon" => 3,
                "Common" => 2,
                _ => 3
            };

            int rarityPenalty = (leaderRarity == "Mythic") ? 2 : 0;

            string targetPersonality = leadSurvivor?.attributes["personality"].ToString().Split(".")[^1] ?? "";
            string currentPersonality = attributes["personality"].ToString().Split(".")[^1];

            rating += currentPersonality == targetPersonality ? rarityBoost : -rarityPenalty;
            rating = Mathf.Max(rating, 1);
        }

        return rating;
    }

    Dictionary<FnItemTextureType, string> textures = [];

    public Texture2D GetTexture(FnItemTextureType textureType = FnItemTextureType.Preview) => GetTexture(textureType, PegLegResourceManager.defaultIcon);
    public Texture2D GetTexture(Texture2D fallbackIcon) => GetTexture(FnItemTextureType.Preview, fallbackIcon);

    const string llamaDefaultPreviewImage = "PinataStandardPack";
    public static readonly Texture2D[] llamaTierIcons = new Texture2D[]
    {
        ResourceLoader.Load<Texture2D>("res://Images/Llamas/PinataStandardPack.png", "Texture2D"),
        ResourceLoader.Load<Texture2D>("res://Images/Llamas/PinataSilver.png", "Texture2D"),
        ResourceLoader.Load<Texture2D>("res://Images/Llamas/PinataGold.png", "Texture2D"),
    };

    public Texture2D GetTexture(FnItemTextureType textureType, Texture2D fallbackIcon)
    {
        if (textures.TryGetValue(textureType, out string value))
            return PegLegResourceManager.LoadResourceAsset<Texture2D>("GameAssets/"+value);

        if (textureType == FnItemTextureType.Personality)
            return GetPersonalityTexture(fallbackIcon);

        if (textureType == FnItemTextureType.SetBonus)
            return GetSetBonusTexture(fallbackIcon);

        if (textureType == FnItemTextureType.Preview && GameItemTemplate.Get(attributes?["portrait"]?.ToString()) is GameItemTemplate portraitTemplate)
        {
            if (portraitTemplate.TryGetTexturePath(out var texturePath))
            {
                textures[textureType] = texturePath;
                return portraitTemplate.GetTexture(fallbackIcon);
            }
        }
        if (template?.Type == "CardPack" && template?.Name.StartsWith("ZCP_") == false)
        {
            if (attributes?.ContainsKey("options") ?? false)
            {
                if (textureType == FnItemTextureType.Preview)
                    return llamaTierIcons[0];
                if (textureType == FnItemTextureType.PackImage)
                    textureType = FnItemTextureType.Preview;
            }
            else if (textureType == FnItemTextureType.PackImage && (!template.DisplayName.Contains("Llama") || template.DisplayName.Contains("Mini")))
                return null;
            else if (textureType == FnItemTextureType.Preview && customData?["llamaTier"]?.GetValue<int>() is int llamaTier)
            {
                string llamaPinataName =
                    (template.TryGetTexturePath(out var imagePath) ? imagePath : null)
                    ?.ToString().Split("\\")[^1];
                if (llamaPinataName?.StartsWith(llamaDefaultPreviewImage) ?? false)
                    return llamaTierIcons[llamaTier];
            }
        }

        return template?.GetTexture(textureType, fallbackIcon);
    }
    
    Texture2D GetPersonalityTexture(Texture2D fallbackIcon = null)
    {
        if (template.Type != "Worker")
            return fallbackIcon;

        var personalityId = template.Personality ?? attributes?["personality"]?.ToString()?.Split(".")?[^1];

        if (personalityId is not null && PegLegResourceManager.supplimentaryData.PersonalityIcons.ContainsKey(personalityId))
            return PegLegResourceManager.supplimentaryData.PersonalityIcons[personalityId];

        return fallbackIcon;
    }

    Texture2D GetSetBonusTexture(Texture2D fallbackIcon = null)
    {
        if (template.Type != "Worker")
            return fallbackIcon;

        if (template.SubType is string subType)
        {
            subType = subType.Replace("Martial Artist", "MartialArtist");
            if (PegLegResourceManager.supplimentaryData.SquadIcons.ContainsKey(subType))
                return PegLegResourceManager.supplimentaryData.SquadIcons[subType];
        }
        else if(attributes?["set_bonus"]?.ToString()?.Split(".")?[^1] is string setBonus)
        {
            if (PegLegResourceManager.supplimentaryData.SetBonusIcons.ContainsKey(setBonus))
                return PegLegResourceManager.supplimentaryData.SetBonusIcons[setBonus];
        }

        return fallbackIcon;
    }
        

    public override string ToString() => $"{{\n  id:{uuid}\n  template:{templateId}\n  quantity:{quantity}\n  attributes:{attributes}\n  custom:{customData}\n}}";

    public GameItem GetUpgradedClone(int rarityUp, int tierUp)
    {
        GameItem newItemClone = Clone(useInspectorOverride: false);
        newItemClone.SetCloneUpgrade(rarityUp, tierUp);
        return newItemClone;
    }

    GameItemTemplate upgradeBasis;
    public void SetCloneUpgrade(int rarityUp, int tierUp)
    {
        if (upgradeBasis is null)
            return;
        var newTemplate = upgradeBasis;
        for (int i = 0; i < rarityUp; i++)
        {
            if (newTemplate.TryGetNextRarity() is not GameItemTemplate newRarity)
                break;
            newTemplate = newRarity;
        }
        for (int i = 0; i < tierUp; i++)
        {
            if (newTemplate.TryGetNextTier() is not GameItemTemplate newTier)
                break;
            newTemplate = newTier;
        }
        templateId = newTemplate.TemplateId;
        _template = newTemplate;
        ResetCachedData();
    }

    public GameItem Clone(int? quantity = null, JsonObject customData = null, bool useInspectorOverride = true) => 
        new(template, quantity ?? this.quantity, attributes.SafeDeepClone(), useInspectorOverride ? (profile is null ? inspectorOverride ?? this : this) : null, customData ?? this.customData.SafeDeepClone(), templateId);

    public void NotifyChanged()
    {
        ResetCachedData();
        OnChanged?.Invoke();
    }

    public void NotifyRemoving() => OnRemoving?.Invoke();
    public void DisconnectFromProfile()
    {
        profile = null;
        uuid = null;
        OnRemoved?.Invoke();
    }
}
