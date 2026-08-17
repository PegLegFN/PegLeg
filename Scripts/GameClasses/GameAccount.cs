using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public partial class AppData
{
	public string lastAccount;
}

public enum OrderRange
{
	Daily,
	Weekly,
	Monthly
}

public readonly record struct RatingData(float fortitude, float offense, float resistance, float technology, double loadoutRating = 0, double backpackRating = 0, bool legacy = false)
{
	//todo: export this via BanjoBotAssets
	static DataTableCurve homebaseRatingCurve;
	public static DataTableCurve HomebaseRatingCurve => homebaseRatingCurve ??= DataTableCurve.LoadHomebaseRatingMap();

	double SampleHomebaseRating() => HomebaseRatingCurve.Sample(4 * (fortitude + offense + resistance + technology));

	const double HomebaseRatingWeight = 0.58;
	const double CommanderLoadoutWeight = 0.21;
	const double BackpackLoadoutWeight = 0.21;
	const double RatingDeltaWeight = 0.24;

	const double CommanderWeight = 0.7;
	const double SupportHeroWeight = 0.06;

	const double WeaponWeight1 = 0.5;
	const double WeaponWeight2 = 0.3;
	const double WeaponWeight3 = 0.2;

	public float PowerLevel
	{
		get
		{
			var homebaseRating = SampleHomebaseRating();
			if (legacy)
				return (float)Mathf.Max(1, homebaseRating);
			//shoutout Krowe moh!
			var averageGearRating = (loadoutRating + backpackRating) / 2;
			var ratingDelta = Mathf.Abs(homebaseRating - averageGearRating);

			return (float)Mathf.Max(1,
				(homebaseRating * HomebaseRatingWeight) +
				(backpackRating * BackpackLoadoutWeight) +
				(loadoutRating * CommanderLoadoutWeight) -
				(ratingDelta * RatingDeltaWeight)
			);
		}
	}

	public void Print(string prefix = "")
	{
		if (!string.IsNullOrWhiteSpace(prefix))
		{
			prefix = prefix.Trim() + " ";
		}
		if (legacy)
			GD.Print($"{prefix}PL: {PowerLevel:0.##} (FORT: {fortitude}, {offense}, {resistance}, {technology})");
		else
		{
			var fortRating = SampleHomebaseRating();
			GD.Print($"{prefix}PL: {PowerLevel:0.##} (FORT: {fortitude}, {offense}, {resistance}, {technology})\n>>> (Homebase Rating: {fortRating:0.##}) (Loadout Rating: {loadoutRating:0.##}) (Backpack Rating: {backpackRating:0.##})");
		}
	}


	public static double GetWeaponPower(GameAccount account)
	{
		var accountProfile = account.GetProfile(FnProfileTypes.AccountItems);
		var accountItems = accountProfile.GetItems(i => i.template?.Category == "Melee" || i.template?.Category == "Ranged");

		if (!account.isOwned)
		{
			//since the highest schematic can make 2x copies of the weapon, assume the highest schematic PL is the highest obtained Weapon Power
			return accountItems
				.Select(item => item.CalculateRating())
				.OrderDescending()
				.FirstOrDefault();
		}

		var backpackProfile = account.GetProfile(FnProfileTypes.Backpack);
		var backpackItems = backpackProfile?.GetItems(i => i.template?.Category == "Melee" || i.template?.Category == "Ranged") ?? [];

		var backpackPowerLevels = backpackItems.Union(accountItems)
		//var backpackPowerLevels = accountItems
			.Select(item => item.CalculateRating())
			.OrderDescending()
			.ToArray();

		if (backpackPowerLevels.Length > 3)
			backpackPowerLevels = backpackPowerLevels[..3];


		//GD.Print($"Highest {string.Join(", ", backpackPowerLevels)}");

		double backpackPower = 0;

		if (backpackPowerLevels.Length >= 1)
			backpackPower += backpackPowerLevels[0] * WeaponWeight1;
		if (backpackPowerLevels.Length >= 2)
			backpackPower += backpackPowerLevels[1] * WeaponWeight2;
		if (backpackPowerLevels.Length >= 3)
			backpackPower += backpackPowerLevels[2] * WeaponWeight3;
		return backpackPower;
	}

	public static double GetLoadoutPower(GameAccount account)
	{
		var accountItems = account.GetProfile(FnProfileTypes.AccountItems);

		var loadoutItem = accountItems.GetItem(accountItems?.statAttributes?["selected_hero_loadout"]?.ToString());
		if (loadoutItem is null)
			return 0;

		var crewMembers = loadoutItem.attributes["crew_members"]?.Deserialize<Dictionary<string, string>>() ?? [];
		if (crewMembers.Count == 0)
			return 0;

		double heroPower = 0;
		heroPower += accountItems.GetItem(crewMembers["commanderslot"]).CalculateRating() * CommanderWeight;

		heroPower += (accountItems.GetItem(crewMembers.GetValueOrDefault("followerslot1"))?.CalculateRating() ?? 0) * SupportHeroWeight;
		heroPower += (accountItems.GetItem(crewMembers.GetValueOrDefault("followerslot2"))?.CalculateRating() ?? 0) * SupportHeroWeight;
		heroPower += (accountItems.GetItem(crewMembers.GetValueOrDefault("followerslot3"))?.CalculateRating() ?? 0) * SupportHeroWeight;
		heroPower += (accountItems.GetItem(crewMembers.GetValueOrDefault("followerslot4"))?.CalculateRating() ?? 0) * SupportHeroWeight;
		heroPower += (accountItems.GetItem(crewMembers.GetValueOrDefault("followerslot5"))?.CalculateRating() ?? 0) * SupportHeroWeight;

		return heroPower;
	}
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

	public static GameAccount[] OwnedAccounts => [.. gameAccountCache.Values.Where(a => a.isOwned)];
	public static GameAccount GetOrCreateAccount(string accountId) => gameAccountCache.ContainsKey(accountId) ? gameAccountCache[accountId] : gameAccountCache[accountId] = new(accountId);
	public static async Task<bool> RemoveAccount(string accountId, bool force = false)
	{
		if (!gameAccountCache.TryGetValue(accountId, out GameAccount account))
			return true;
		if (!await account.RemoveDeviceDetails(force))
			return false;
		account.DeleteLocalData();
		gameAccountCache.Remove(accountId);
		account.accountId = null;
		if (_activeAccount == account)
			_activeAccount = null;
		return true;
	}

	public static async Task<GameAccount> SearchForAccount(string username, GameAccount asAccount = null)
	{
		asAccount ??= ActiveAccount;
		var searchResponse = await FnWebAddresses.EpicUserSearch
			.MakeRequest($"/api/v1/search/{asAccount.accountId}?platform=epic&prefix={username}")
			.SetJsonContent()
			.SetAccount(asAccount)
			.Send();
		if (await searchResponse.CheckForError())
			return null;
		var accountArray = await searchResponse.ReadJson<JsonArray>();
		if (accountArray.Count == 0)
			return null;
		var resultAccount = GetOrCreateAccount(accountArray[0]["accountId"].ToString());
		if (resultAccount is not null && accountArray[0]["matches"]?[0]?["value"]?.ToString() is string displayName)
			resultAccount.SetLocalData("DisplayName", displayName);
		return resultAccount;
	}

	static GameAccount _activeAccount;
	static GameAccount emptyAccount = new(null);
	public static GameAccount ActiveAccount => _activeAccount ??= emptyAccount;
	public static event Action ActiveAccountChangedEarly;
	public static event Action ActiveAccountChanged;
	public static event Action RemindersChanged;

	public static event Action<string> LocalDataChanged;

	public static async Task<bool> SetActiveAccount(string accountId, Action<string> progress = null)
	{
		if (!gameAccountCache.TryGetValue(accountId, out GameAccount account))
			return false;
		progress?.Invoke("Logging in");
		if (!await account.Authenticate())
			return false;
		var profile = await account.GetProfile(FnProfileTypes.AccountItems).Query();
		if (!profile.hasProfile)
			return false;
		progress?.Invoke("Fetching profiles");
		await account.CheckLocalPinnedQuests();
		await account.QueryAllProfiles();
		progress?.Invoke("Checking calendar");
		await account.CheckCalender();
		progress?.Invoke("");
		_activeAccount = account;
		ActiveAccountChangedEarly?.Invoke();
		RemindersChanged?.Invoke();
		var keys = account.localData.Select(x => x.Key).ToArray();
		foreach (var key in keys)
		{
			LocalDataChanged?.Invoke(key);
		}
		ActiveAccountChanged?.Invoke();
		AppConfig.Set("account", "lastUsed", accountId);
		return true;
	}

	public static void ClearActiveAccount() => _activeAccount = emptyAccount;

	public static async Task RefreshActiveAccount()
	{
		if (!await ActiveAccount.Authenticate(assumeValid: false))
		{
			GenericConfirmationWindow.ShowError("Failed to authenticate", "Refresh Failed").StartTask();
			return;
		}
		_activeAccount.ratingData = null;
		_activeAccount.ventureRatingData = null;
		_activeAccount.localData = null;
		_activeAccount.localPinnedQuests = [];
		foreach (var profile in _activeAccount.profiles.Values)
		{
			profile.InvalidateProfile();
		}
		await _activeAccount.QueryAllProfiles(true);
		await _activeAccount.CheckLocalPinnedQuests();
		await SetActiveAccount(_activeAccount.accountId);
	}

	public static GameAccount LoginToAccount(JsonObject accountAuthResponse)
	{
		if (accountAuthResponse["account_id"]?.ToString() is not string accountId)
			return null;
		var account = GetOrCreateAccount(accountId);
		account.SetAuthentication(accountAuthResponse);
		return account;
	}

	public GameClient TargetClient { get; private set; }
	//public XmppManager XmppManager { get; private set; }
	public GameAccount(string accountId)
	{
		this.accountId = accountId;
		//XmppManager = new(this);
		//if (GetLocalData("GameClient")?.ToString() is string clientId)
		//{
		//    TargetClient = GameClient.clients.TryGetValue(clientId, out var c) ? c : null;
		//}
		//else if (GetLocalData("DeviceDetails") is not null)
		//{
		//    TargetClient = GameClient.NewSwitchClient;
		//    SetLocalData("GameClient", TargetClient.ClientID);
		//}
		TargetClient ??= GameClient.PreferredClient;
	}

	public Action OnAccountUpdated;

	public string accountId { get; private set; }
	bool isValid => !string.IsNullOrWhiteSpace(accountId);

	public bool loginFailure { get; private set; }
	public string loginFailureMessage { get; private set; }
	public bool isAuthed => isValid && !loginFailure && !AuthTokenExpired;
	public bool isOwned => isValid && (isAuthed || GetLocalData("DeviceDetails") is not null);

	public string DisplayName => GetLocalData("DisplayName")?.ToString() ?? $"<{accountId}>";
	public Texture2D ProfileIcon => GetLocalData("IconPath")?.ToString() is string iconPath ? CatalogRequests.GetLocalCosmeticResource(iconPath) : null;

	public async Task FetchDisplayNames(params GameAccount[] accounts)
	{
		if (accounts.Length > 100)
		{
			await FetchDisplayNames(accounts[..100]);
			await FetchDisplayNames(accounts[100..]);
			return;
		}
		var res = await FnWebAddresses.EpicAccount
			.MakeRequest($"/account/api/public/account?{string.Join("&", accounts.Select(a => $"accountId={a.accountId}"))}")
			.SetAccount(this)
			.Send();
		if (await res.CheckForError())
			return;
		//GD.Print(await res.Content.ReadAsStringAsync());
		var displayNames = await res.ReadJson<AccountDisplayNames[]>(Helpers.JsonOptions.Fields);
		var displayNameDict = displayNames.ToDictionary(d => d.id);
		foreach (var acc in accounts)
		{
			if (displayNameDict.TryGetValue(acc.accountId, out var dnData))
				acc.SetLocalData("DisplayName", dnData.DisplayName);
		}
	}

	Dictionary<string, GameProfile> profiles = [];

	public GameProfile this[string profileId] => GetProfile(profileId);
	public GameProfile GetProfile(string profileId) => profiles.ContainsKey(profileId ?? "") ? profiles[profileId ?? ""] : profiles[profileId ?? ""] = new(this, profileId ?? "");
	public bool HasProfile(string profileId) => profiles.ContainsKey(profileId);

	Dictionary<string, FriendData> friends = [];
	public IEnumerable<FriendData> Friends => friends.Values;
	public FriendData? GetFriend(string accountId) => friends.TryGetValue(accountId, out var friendData) ? friendData : null;

	public async Task FetchFriends()
	{
		var res = await FnWebAddresses.EpicFriends
			.MakeRequest($"friends/api/v1/{accountId}/summary")
			.SetAccount(this)
			.Send();
		if (await res.CheckForError())
			return;
		var friendData = await res.ReadJson<FriendSummary>(Helpers.JsonOptions.Fields);
		//GD.Print(await res.Content.ReadAsStringAsync());
		friends = friendData.friends.ToDictionary(f => f.accountId ?? "");
		var userIds =
			friends.Keys
			.Union(friendData.incoming.Select(f => f.accountId))
			.Union(friendData.outgoing.Select(f => f.accountId))
			.Union(friendData.blocklist.Select(f => f.accountId))
			.Distinct();
		await FetchDisplayNames([.. userIds.Select(GetOrCreateAccount)]);
	}

	public async Task<GameAccount> EnsureProfile(string profileId, bool force = false)
	{
		await GetProfile(profileId).Query(force);
		return this;
	}

	public async Task QueryAllProfiles(bool force = false)
	{
		if (!isOwned)
		{
			if (ActiveAccount is null)
				return;
			await Task.WhenAll(
				GetProfile(FnProfileTypes.AccountItems).Query(force),
				GetProfile(FnProfileTypes.Common).Query(force)
			);
		}
		else
		{
			await Task.WhenAll(
				GetProfile(FnProfileTypes.AccountItems).Query(force),
				GetProfile(FnProfileTypes.Common).Query(force),
				GetProfile(FnProfileTypes.CosmeticInventory).Query(force),
				GetProfile(FnProfileTypes.PeopleCollection).Query(force),
				GetProfile(FnProfileTypes.SchematicCollection).Query(force),
				GetProfile(FnProfileTypes.Backpack).Query(force),
				GetProfile(FnProfileTypes.Storage).Query(force)
			);
		}
	}

	string authToken;
	int authExpiresAt = -999;
	string refreshToken;
	int refreshExpiresAt = -999;
	AuthenticationHeaderValue accountAuthHeader;
	//fails 60 seconds before it would actualy expire
	public bool AuthTokenExpired => authExpiresAt <= (Time.GetTicksMsec() * 0.001) + 60;
	bool RefreshTokenExpired => refreshExpiresAt <= (Time.GetTicksMsec() * 0.001) + 10;
	public string AuthToken => authToken;
	public AuthenticationHeaderValue AuthHeader => accountAuthHeader;
	SemaphoreSlim authSemaphore = new(1);
	public async Task<bool> Authenticate(bool loadingOverlay = false, bool assumeValid = true)
	{
		if (isAuthed && assumeValid)
			return true;
		if (!isOwned)
			return false;

		using var loadToken = LoadingOverlay.CreateToken("Authenticating...");
		if (!loadingOverlay)
			loadToken.Dispose();

		using var _ = await authSemaphore.AwaitToken();

		if (!assumeValid)
		{
			using var req = await FnWebAddresses.EpicAccount
				.MakeRequest("account/api/oauth/verify")
				.SetAuthorisation(AuthHeader)
				.Send();
			if (req.IsSuccessStatusCode)
				return true;
			ForceExpireToken();
		}

		if (!RefreshTokenExpired)
		{
			var refreshRequest = await TargetClient.LoginWithRefreshToken(refreshToken);

			if (!await refreshRequest.CheckForError())
			{
				GD.Print($"Token refreshed for {DisplayName}");
				var json = await refreshRequest.ReadJson();
				SetAuthentication(json);
				return true;
			}
			GD.Print($"Refresh token error for {DisplayName}");
		}
		var dd = GetLocalData("DeviceDetails")?.AsArray().Select(n => n.GetValue<byte>()).ToArray();
		var ddJson = DecryptDeviceDetails(dd);
		string failMsg = "";
		bool offline = false;
		if (ddJson is null)
		{
			failMsg = "Failed to decrypt auth data\n(You may have changed the name of your device)";
		}
		else
		{
			var deviceRequest = await TargetClient.LoginWithDeviceAuth(ddJson);

			(var didError, var errorContent) = await deviceRequest.CheckForErrorJson();
			if (!didError)
			{
				GD.Print($"Token created for {DisplayName}");
				SetAuthentication(await deviceRequest.ReadJson());
				return true;
			}

			//check if offline by pinging googles dns
			offline = !await WebHelpers.Ping("8.8.8.8");

			failMsg = offline ?
				"Offline" :
				(
					errorContent?["errorMessage"].ToString() ??
					deviceRequest.ReasonPhrase ??
					"Unknown error"
				);
		}
		GD.Print("Login failure: " + failMsg);
		if (!loginFailure)
		{
			loginFailure = true;
			loginFailureMessage = failMsg;
			OnAccountUpdated?.Invoke();

			//dont send login failure notif when offline
			if (offline)
				return false;

			string displayName = DisplayName;
			if (AppConfig.Get("ui", "obscure_names", false))
			{
				var idx = Array.IndexOf(OwnedAccounts, this);
				if (idx >= 0)
					displayName = $"Account #{idx + 1}";
			}

			NotificationManager.PushOne(
				new()
				{
					header = "Login Failure",
					icon = ProfileIcon,
					itemColor = Color.FromHtml("#aa0000"),
					body=$"""
                    Could not Login to {displayName}, please Login again from the Account Selector
                    Err: {loginFailureMessage}
                    """
				}
			);
		}

		return false;
	}

	string eosToken;
	int eosExpiresAt = -999;
	AuthenticationHeaderValue eosHeader;
	//fails 60 seconds before it would actualy expire
	public bool EOSTokenExpired => eosExpiresAt <= (Time.GetTicksMsec() * 0.001) + 60;
	public string EOSToken => eosToken;
	public AuthenticationHeaderValue EOSHeader => eosHeader;
	public async Task<bool> AuthenticateEOS(bool loadingOverlay = false, bool assumeValid = true)
	{
		if (!EOSTokenExpired && assumeValid)
			return true;
		if (!isOwned)
			return false;

		using var loadToken = LoadingOverlay.CreateToken("Authenticating...");
		if (!loadingOverlay)
			loadToken.Dispose();

		if (!await Authenticate(false, assumeValid))
			return false;

		var response = await TargetClient.EOSLogin(AuthToken);
		if (await response.CheckForError())
		{
			GD.Print("Failed to auth EOS");
			return false;
		}

		var json = await response.ReadJson();
		eosToken = json["access_token"].ToString();
		eosHeader = new("Bearer", eosToken);
		eosExpiresAt = Mathf.FloorToInt(Time.GetTicksMsec() * 0.001) + json["expires_in"].GetValue<int>();

		return true;
	}

	public void ForceExpireToken()
	{
		authExpiresAt = 0;
		eosExpiresAt = 0;
	}

	public async Task<string> GenerateExchangeCode()
	{
		if (!await Authenticate())
			return null;
		var response = await FnWebAddresses.EpicAccount
			.MakeRequest("account/api/oauth/exchange")
			.SetFormContent("consumingClientId=launcherAppClient2")
			.SetAccount(this)
			.Send();
		if (await response.CheckForError())
			return null;
		var result = await response.ReadJson();
		return result["code"]?.ToString();
	}

	public SemaphoreSlim profileOperationSemaphore { get; private set; } = new(1);

	public async Task<JsonArray> PurchaseOffer(GameOffer offer, int purchaseQuantity = 1, bool hideCompleteMsg = false)
	{
		var profile = GetProfile(FnProfileTypes.Common);
		GameProfile.silenceOperationLog = hideCompleteMsg;
		var result = await profile
			.PerformOperation("PurchaseCatalogEntry", $$"""
            {
                "offerId": "{{offer.OfferId}}",
                "purchaseQuantity": {{purchaseQuantity}},
                "currency": "{{offer["prices"][0]["currencyType"]}}",
                "currencySubType": "{{offer["prices"][0]["currencySubType"]}}",
                "expectedTotalPrice": {{offer.CalculatePersonalPrice(this).quantity * purchaseQuantity}},
                "gameContext": "PegLeg"
            }
            """);
		offer.NotifyChanged();
		return result;
	}

	public async Task<bool> SetAsActiveAccount(Action<string> progress = null) => await SetActiveAccount(accountId, progress);

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
		await UpdateIconTask(this);
	}

	public async Task UpdateIconTask(GameAccount asAccount = null)
	{
		asAccount ??= this;
		if (iconSemaphore.CurrentCount <= 0)
			return;
		await iconSemaphore.WaitAsync();
		try
		{
			if (!await asAccount.Authenticate())
				return;

			var avatarResponse = await FnWebAddresses.EpicAvatar
				.MakeRequest($"/v1/avatar/fortnite/ids?accountIds={accountId}")
				.SetJsonContent()
				.SetAccount(asAccount)
				.Send();
			if (await avatarResponse.CheckForError())
				return;

			var avatarData = await avatarResponse.ReadJson();
			if (avatarData is JsonObject obj)
			{
				GD.Print($"avatar fetch error (which is strange since this shouldve been caught): \n{obj}");
				return;
			}

			string skinId = avatarData[0]?["avatarId"]?.ToString() is string avId ? avId.Split(":")[^1] : null;
			GD.Print($"skinID: {skinId}");
			if (string.IsNullOrWhiteSpace(skinId) || skinId == "CID_STWHERO") //todo: add dedicated icon for STW Hero skins
			{
				SetLocalData("IconPath", null);
				return;
			}

			var skinResponse = await ApiWebAddresses.fnDashApi
				.MakeRequest($"/v2/cosmetics/br/{skinId}")
				.SetJsonContent()
				.AddCosmeticHeader()
				.Send();
			var skinData = await skinResponse.ReadJson();

			string skinIconServerPath = null;
			try
			{
				skinIconServerPath =
					skinData["data"]?["images"]?["icon"]?.ToString() ??
					skinData["data"]?["images"]?["smallIcon"]?.ToString();
			}
			catch { }
			if (skinIconServerPath is null)
			{
				SetLocalData("IconPath", null);
				return;
			}

			var skinIcon = await CatalogRequests.GetCosmeticResource(skinIconServerPath);

			if (skinIcon is null)
			{
				SetLocalData("IconPath", null);
				return;
			}
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
		var deviceResponse = await FnWebAddresses.EpicAccount
			.MakeRequest($"account/api/public/account/{accountId}/deviceAuth", HttpMethod.Post)
			.AddHeader("X-Epic-Device-Inf",
				new JsonObject()
				{
					["type"] = OS.GetName(),
					["model"] = OS.GetModelName(),
					["os"] = OS.GetVersion()
				}.ToJsonString()
			)
			.SetAccount(this)
			.Send();

		if (await deviceResponse.CheckForError())
			return;

		JsonObject deviceDetails = await deviceResponse.ReadJson<JsonObject>();

		if (GetLocalData("DeviceDetails") is not null)
		{
			await RemoveDeviceDetails(true);
		}

		SetLocalData("Client", TargetClient.ClientID);
		SetLocalData("DeviceDetails", new JsonArray([.. EncryptDeviceDetails(deviceDetails).Select(b => (JsonNode)b)]));
	}

	public async Task<bool> RemoveDeviceDetails(bool force = false)
	{
		if (!force && !await Authenticate())
		{
			GD.Print("Authentication failed, aborting device detail deletion");
			return false;
		}
		var dd = GetLocalData("DeviceDetails")?.AsArray().Select(n => n.GetValue<byte>()).ToArray();
		if (DecryptDeviceDetails(dd) is JsonObject deviceDetails)
		{
			var response = await FnWebAddresses.EpicAccount
				.MakeRequest($"account/api/public/account/{accountId}/deviceAuth/{deviceDetails["deviceId"]}", HttpMethod.Delete)
				.SetAccount(this)
				.Send();
			(var didError, var errorContent) = await response.CheckForErrorJson();
			if (didError)
			{
				if (errorContent?["numericErrorCode"]?.ToString() == "18130")
					GD.Print("Device has already been unregistered, proceeding with removal");
				else
				{
					GD.Print("Could not unregister device");
					if (!force)
						return false;
					GD.Print("Device removal forced, so error is disregarded");
				}
			}
			ClearLocalData("DeviceDetails");
			GD.Print("Device details deleted");
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

		if (!FileAccess.FileExists($"{accountDataPath}/{accountId}"))
		{
			GD.Print($"no local data file for user <{accountId}>");
			localData = [];
			return;
		}

		Error fileErr = Error.Bug;
		string localDataString = null;
		for (int i = 0; i < 3; i++)
		{
			using FileAccess localDataFile = FileAccess.Open($"{accountDataPath}/{accountId}", FileAccess.ModeFlags.Read);
			fileErr = localDataFile.GetError();
			if (fileErr == Error.Ok)
			{
				localDataString = localDataFile.GetAsText();
				break;
			}
			GD.Print("could not read local data file: " + fileErr);
			Thread.Sleep(1);
		}
		if (fileErr != Error.Ok)
		{
			GenericConfirmationWindow.ShowError($"Error reading local data for account. Please report this to Tomatech, and try restart PegLeg.\n({fileErr})").StartTask();
			return;
		}

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
		if (value is null)
		{
			if (!localData.ContainsKey(key))
				return;
			localData.Remove(key);
		}
		else
			localData[key] = value.SafeDeepClone();

		if (this == GameAccount.ActiveAccount)
			LocalDataChanged?.Invoke(key);

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

	public event Action<GameAccount> OnRatingDataChanged;
	RatingData? ratingData;
	public RatingData RatingData => GetRatingData();
	public RatingData GetRatingData(bool force = false)
	{
		try
		{
			if (!force && ratingData is not null)
				return ratingData.Value;

			var accountItems = GetProfile(FnProfileTypes.AccountItems);
			var statItems = accountItems.GetItems("Stat");
			var equippedWorkerItems = accountItems.GetItems("Worker", item => item.attributes.ContainsKey("squad_id"));

			var equippedWhereNull = equippedWorkerItems.Where(item => item?.attributes?["squad_id"]?.ToString() == null).ToArray();
			if (equippedWhereNull.Length > 0)
				GD.Print($"Equipped Survivors Where Null {{aid:{accountId}}}:[\n{string.Join(",\n", equippedWhereNull.Select(i => i?.GameItemData))}\n]");

			int LookupStatItem(string statId)
			{
				var stat = statItems.FirstOrDefault(item => item.templateId == statId)?.quantity ?? 0;
				//GD.Print($"Stat:{statId}:{stat}");
				return stat;
			}

			float LookupWorkers(string squadId)
			{
				var matchingWorkersSafe = equippedWorkerItems.Where(item => item?.attributes?["squad_id"]?.ToString() == squadId);
				var statSafe = matchingWorkersSafe.Sum(item => item?.CalculateSurvivorRating() ?? 0);
				return statSafe;
				/*
				try
				{
					var matchingWorkers = equippedWorkerItems.Where(item => item.attributes["squad_id"].ToString() == squadId);
					var stat = matchingWorkers.Sum(item => item.CalculateSurvivorRating());
					//GD.Print($"Squad:{squadId}:{stat}");
					return stat;
				}
				catch(NullReferenceException e)
				{
					GD.Print("SquadNullRefErrorCTX: (" +
						$"equippedArrayIsNull:{equippedWorkerItems is null}, " +
						$"equippedArrayLen:{equippedWorkerItems?.Length}, " +
						$"equippedWhereNull:{equippedWorkerItems?.Count(item => item?.attributes?["squad_id"]?.ToString() == null)}, " +
						$"matchingLen:{equippedWorkerItems?.Count(item => item?.attributes?["squad_id"]?.ToString() == squadId)}, " +
						$"matchingSum:{equippedWorkerItems?.Where(item => item?.attributes?["squad_id"]?.ToString() == squadId).Sum(item => item?.CalculateSurvivorRating() ?? 0)}, " +
						$"matchingWhereRatingNull:{equippedWorkerItems?.Where(item => item?.attributes?["squad_id"]?.ToString() == squadId).Count(item => item?.CalculateSurvivorRating() == null)}, " +
						")"
					);
				}*/
			}

			double backpackPower = RatingData.GetWeaponPower(this);
			double loadoutPower = RatingData.GetLoadoutPower(this);

			//+ profileStats["fortitude"].GetValue<int>()
			float fortitude = LookupStatItem("Stat:fortitude") + LookupWorkers("squad_attribute_medicine_trainingteam") + LookupWorkers("squad_attribute_medicine_emtsquad");
			float offense = LookupStatItem("Stat:offense") + LookupWorkers("squad_attribute_arms_fireteamalpha") + LookupWorkers("squad_attribute_arms_closeassaultsquad");
			float resistance = LookupStatItem("Stat:resistance") + LookupWorkers("squad_attribute_scavenging_scoutingparty") + LookupWorkers("squad_attribute_scavenging_gadgeteers");
			float technology = LookupStatItem("Stat:technology") + LookupWorkers("squad_attribute_synthesis_corpsofengineering") + LookupWorkers("squad_attribute_synthesis_thethinktank");


			RatingData newFortStats = new(fortitude, offense, resistance, technology, loadoutPower, backpackPower);
			if (ratingData != newFortStats)
			{
				if (isOwned)
					newFortStats.Print("Main");
				ratingData = newFortStats;
				OnRatingDataChanged?.Invoke(this);
			}
			return newFortStats;
		}
		catch
		{
			return ratingData.Value;
		}
	}

	public event Action<GameAccount> OnVentureRatingDataChanged;
	RatingData? ventureRatingData;
	public RatingData VentureRatingData => GetVentureRatingData();
	public RatingData GetVentureRatingData(bool force = false)
	{
		if (!force && ventureRatingData is not null)
			return ventureRatingData.Value;

		var accountItems = GetProfile(FnProfileTypes.AccountItems);
		var statItems = accountItems.GetItems("Stat");
		int LookupStatItem(string statId) => statItems.FirstOrDefault(item => item.templateId == statId)?.quantity ?? 0;

		//double loadoutPower = GetLoadoutPower();
		//double backpackPower = GetBackpackPower();

		//+ profileStats["fortitude"].GetValue<int>()
		float fortitude = LookupStatItem("Stat:fortitude_phoenix");
		float offense = LookupStatItem("Stat:offense_phoenix");
		float resistance = LookupStatItem("Stat:resistance_phoenix");
		float technology = LookupStatItem("Stat:technology_phoenix");

		RatingData newVentureFortStats = new(fortitude, offense, resistance, technology, legacy: true);
		if (ventureRatingData != newVentureFortStats)
		{
			newVentureFortStats.Print("Venture");
			ventureRatingData = newVentureFortStats;
			OnVentureRatingDataChanged?.Invoke(this);
		}
		return ventureRatingData.Value;
	}

	int itemLevelCap = 0;
	public int GetItemLevelCap(bool force = false)
	{
		if (!isOwned)
			return 999;
		if (itemLevelCap > 0 && !force)
			return itemLevelCap;
		var profile = GetProfile(FnProfileTypes.AccountItems);
		var resourceLookupData = PegLegResourceManager.MagicNumbers["questTemplateToItemLevelCap"]?.AsObject() ?? new()
		{
			["Quest:outpostquest_t1_l6"] = 20,
			["Quest:plankertonquest_outpost_l4"] = 30,
			["Quest:cannyvalleyquest_outpost_l2"] = 40,
			["Quest:cannyvalleyquest_outpost_l6"] = 50
		};
		var resourceLookup = resourceLookupData.Deserialize<Dictionary<string, int>>().ToArray();
		resourceLookup = [.. resourceLookup.OrderBy(kvp => -kvp.Value)];
		foreach (var kvp in resourceLookup)
		{
			if (profile.GetFirstTemplateItem(kvp.Key) is GameItem quest && quest.QuestClaimed) //this could instead depend on quest completion, idk
			{
				return itemLevelCap = kvp.Value;
			}
		}
		return itemLevelCap = 10;
	}

	public async Task GenerateXRayLlamaResults(bool force = false)
	{
		var campaign = GetProfile(FnProfileTypes.AccountItems);
		var existingPrerolls = campaign.GetItems("PrerollData");
		if (force || existingPrerolls.Length == 0 || existingPrerolls.Any(p => (p.attributes["expiration"]?.Deserialize<DateTime>() ?? default) < DateTime.UtcNow))
			await campaign.PerformOperation("PopulatePrerolledOffers");
	}

	public async Task<float> GetSurvivorBonus(string bonusID, int perSquadRequirement = 2, float boostBase = 5)
	{
		if (!isOwned)
			return 0;
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
		if(!isOwned)
			return null;

		var commonData = await GetProfile(FnProfileTypes.Common).Query();

		var orderRange = commonData.statAttributes?[range.ToAttribute()];
		var lastInterval = orderRange?["lastInterval"]?.ToString();
		if (lastInterval is null)
			return null;

		var lastIntervalTime = DateTime.Parse(lastInterval, null, DateTimeStyles.RoundtripKind);
		if (lastIntervalTime != range.ToInterval())
			return null;

		return orderRange["purchaseList"].AsObject();
	}

	public async Task<int> GetPurchaseLimit(GameOffer offer)
	{
		var stockLimitTask = GetStockLimit(offer);
		var affordableLimitTask = GetAffordableLimit(offer);
		return Mathf.Min(
			await stockLimitTask,
			await affordableLimitTask
		);
	}

	public async Task<int> GetStockLimit(GameOffer offer)
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
			GameItem eventTracker = commonData?.GetItems("EventPurchaseTracker", item =>
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
			totalLimit = Mathf.Min(totalLimit, 3000 - accountItemData?.GetFirstTemplateItem("Token:accountinventorybonus")?.quantity ?? 0);
		}

		if (offer.itemGrants[0].templateId == "CampaignHeroLoadout:purchaseabledefaultloadout")
		{
			var accountItemData = await GetProfile(FnProfileTypes.AccountItems).Query();
			totalLimit = Mathf.Min(totalLimit, 11 - accountItemData?.GetTemplateItems("CampaignHeroLoadout:purchaseabledefaultloadout")?.Length ?? 0);
		}

		return totalLimit;
	}

	public async Task<int> GetAffordableLimit(GameOffer offer, bool cosmetic = false)
	{
		var pricePerPurchase = cosmetic ? offer.CalculatePersonalPrice() : offer.Price;
		pricePerPurchase ??= offer.Price; //if personal price fails, fall back to standard price
		if ((pricePerPurchase?.quantity ?? 0) == 0)
			return 999;
		if (cosmetic)
		{
			int vbucks = 0;//put vbucks here
			return Mathf.FloorToInt((float)vbucks / pricePerPurchase.quantity);
		}
		var inInventory = (await GetProfile(FnProfileTypes.AccountItems).Query())?.GetFirstTemplateItem(pricePerPurchase.templateId);
		return Mathf.FloorToInt((float)(inInventory?.quantity ?? 0) / pricePerPurchase.quantity);
	}

	public bool MatchesFulfillmentRequirements(GameOffer offer)
	{
		if (!isOwned)
			return true;
		var common = GetProfile(FnProfileTypes.Common);
		JsonObject fulfillments = null;
		if (offer.FulfillmentDenyList.Count > 0)
		{
			fulfillments ??= common.statAttributes["in_app_purchases"]?["fulfillmentCounts"]?.AsObject() ?? [];
			if (offer.FulfillmentDenyList.Any(check => (fulfillments[check.Key ?? ""]?.GetValue<int>() ?? 0) >= check.Value))
				return false;
		}

		if (offer.FulfillmentRequireList.Count > 0)
		{
			fulfillments ??= common.statAttributes["in_app_purchases"]?["fulfillmentCounts"]?.AsObject() ?? [];
			if (offer.FulfillmentRequireList.Any(check => (fulfillments[check.Key ?? ""]?.GetValue<int>() ?? 0) < check.Value))
				return false;
		}

		return true;
	}

	public bool MatchesItemRequirements(GameOffer offer)
	{
		if (offer.ItemDenyList.Count == 0)
			return true;
		static int Quant(GameItem i) => i.quantity;
		foreach (var check in offer.ItemDenyList)
		{
			var total = profiles.Sum(p =>
				p.Value
				.GetTemplateItems(check.Key)?
				.Sum(Quant) ?? 0
			);
			if (total >= check.Value)
				return false;
		}
		return true;
	}

	public async Task<string> GetSACCode(bool addExpiredText = true)
	{
		var commonData = await GetProfile(FnProfileTypes.Common).Query();
		var lastSetTime = DateTime.TryParse(commonData?.statAttributes["mtx_affiliate_set_time"]?.ToString(), null, DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.UtcNow.AddDays(-15);
		bool isExpired = (DateTime.UtcNow - lastSetTime).Days > 13;
		var creator = commonData?.statAttributes["mtx_affiliate"]?.ToString();
		return (creator ?? "None") + (isExpired && creator is not null && addExpiredText ? " (Expired)" : "");
	}

	public async Task<bool> IsSACExpired() => Mathf.FloorToInt(await GetSACTime()) > 13;

	public async Task<double> GetSACTime()
	{
		var commonData = await GetProfile(FnProfileTypes.Common).Query();
		var lastSetTimeString = commonData.statAttributes["mtx_affiliate_set_time"]?.ToString();
		if (lastSetTimeString is null)
			return 999;
		var lastSetTime = DateTime.Parse(lastSetTimeString, null, DateTimeStyles.RoundtripKind);
		return (DateTime.UtcNow - lastSetTime).TotalDays;
	}

	public async Task<bool> SetSACCode(string newName)
	{
		await GetProfile(FnProfileTypes.Common)
			.PerformOperation("SetAffiliateName", $$"""
            {
                "affiliateName": "{{newName}}"
            }
            """);
		//TODO: return false if creator code not found
		return true;
	}

	HashSet<string> reminderItems;

	public void ToggleReminder(GameItemTemplate template)
	{
		if (!isOwned || template?.CanBeFavourited != true)
			return;
		if (template.Tier > 1)
		{
			template = GameItemTemplate.Get(TierSubstitute().Replace(template.TemplateId, "_t01"));
		}
		reminderItems ??=
			GetLocalData("reminderItems")?.Deserialize<HashSet<string>>() ??
			GetLocalData("bookmarkedItems")?.Deserialize<HashSet<string>>() ?? [];
		ClearLocalData("bookmarkedItems");

		var tid = template.TemplateId.ToLower();
		bool hasReminder = reminderItems.Contains(tid);
		//GD.Print($"already: {isBookmarked} ({tid})");
		if (hasReminder)
		{
			var tidSearch = RaritySubstitute().Replace(tid, "_..?_");
			tidSearch = TierSubstitute().Replace(tidSearch, "_t0\\d");
			//GD.Print("reminder removal search " + tidSearch);
			var toRemove = reminderItems.Where(x => Regex.IsMatch(x, tidSearch));
			foreach (var item in toRemove)
			{
				reminderItems.Remove(item);
				GD.Print("reminder removed " + item);
			}
		}
		else
		{
			reminderItems.Add(tid);
			GD.Print("reminder added " + tid);
			while (template.TryGetNextRarity() is GameItemTemplate upgradedTemplate)
			{
				tid = upgradedTemplate.TemplateId.ToLower();
				if (reminderItems.Contains(tid))
					break;
				reminderItems.Add(tid);
				GD.Print("reminder added " + tid);
				template = upgradedTemplate;
			}
		}
		SetLocalData("reminderItems", JsonSerializer.SerializeToNode(reminderItems));
		RemindersChanged?.Invoke();
	}

	public bool HasReminder(GameItemTemplate template)
	{
		if (template?.CanBeFavourited != true)
			return false;
		return HasReminder(template.TemplateId);
	}

	public bool HasReminder(string templateId)
	{
		if (templateId is null)
			return false;
		reminderItems ??= GetLocalData("reminderItems")?.Deserialize<HashSet<string>>() ?? [];
		var result = reminderItems.Contains(TierSubstitute().Replace(templateId, "_t01").ToLower());
		return result;
	}

	Dictionary<string, string> loadoutCustomNames;
	public string GetCustomNameForLoadoutSlot(GameItem loadout)
	{
		loadoutCustomNames ??= GetLocalData("heroLoadoutSlotNames")?.Deserialize<Dictionary<string, string>>() ?? [];
		return loadoutCustomNames.TryGetValue(loadout?.uuid, out var name) ? name : null;
	}

	public void SetCustomNameForLoadoutSlot(GameItem loadout, string newName)
	{
		if (loadout?.uuid is null)
			return;
		loadoutCustomNames ??= GetLocalData("heroLoadoutSlotNames")?.Deserialize<Dictionary<string, string>>() ?? [];
		if (string.IsNullOrWhiteSpace(newName))
			loadoutCustomNames.Remove(loadout.uuid);
		else
			loadoutCustomNames[loadout.uuid] = newName;
		loadout.NotifyChanged();
		SetLocalData("heroLoadoutSlotNames", JsonSerializer.SerializeToNode(loadoutCustomNames));
	}

	public const string HeroLoadoutBlueprintTID = "PLHeroLoadoutBlueprint:blueprint";
	Dictionary<string, GameItem> heroLoadoutBlueprintDict;
	Dictionary<string, GameItem> HeroLoadoutBlueprintDict => heroLoadoutBlueprintDict ??= GetLocalData("heroLoadoutBlueprints")?.Deserialize<Dictionary<string, JsonNode>>(Helpers.JsonOptions.Fields).ToDictionary(kvp => kvp.Key, kvp => new GameItem(null, 1, kvp.Value.AsObject().SafeDeepClone(), templateId: HeroLoadoutBlueprintTID).SetUUID(kvp.Key)) ?? [];
	public GameItem[] HeroLoadoutBlueprints => [.. HeroLoadoutBlueprintDict.Values];
	public void CreateHeroLoadoutBlueprint(GameItem loadoutSlot)
	{
		if (loadoutSlot?.profile?.account != this)
			return;

		var displayName = GetCustomNameForLoadoutSlot(loadoutSlot);
		JsonObject loadoutCrew = loadoutSlot.attributes["crew_members"]?.AsObject() ?? [];

		JsonObject crewMembers = [];
		var commanderGuid = loadoutCrew[$"commanderslot"]?.ToString();
		var commanderHero = commanderGuid is not null ? loadoutSlot.profile.GetItem(commanderGuid) : null;
		crewMembers[$"commanderslot"] = JsonSerializer.SerializeToNode(LoadoutBlueprintHero.FromHero(commanderHero.template), Helpers.JsonOptions.Fields);

		var teamPerkGuid = loadoutSlot.attributes["team_perk"]?.ToString();
		var teamPerk = teamPerkGuid is not null ? loadoutSlot.profile.GetItem(teamPerkGuid) : null;

		for (int i = 0; i < 5; i++)
		{
			var supportGuid = loadoutCrew[$"followerslot{i + 1}"]?.ToString();
			var supportHero = supportGuid is not null ? loadoutSlot.profile.GetItem(supportGuid) : null;
			crewMembers[$"followerslot{i + 1}"] = JsonSerializer.SerializeToNode(LoadoutBlueprintHero.FromHero(supportHero?.template), Helpers.JsonOptions.Fields);
		}

		var gadgetTemplates = loadoutSlot.attributes["gadgets"]?.AsArray().OrderBy(g => (int)g["slot_index"]).Select(g => g["gadget"].ToString()).ToArray() ?? [];

		if (loadoutCrew.Any(kvp => kvp.Key.StartsWith("defenderslot")))
		{
			for (int i = 0; i < 3; i++)
			{
				var supportGuid = loadoutCrew[$"defenderslot{i + 1}"]?.ToString();
				var supportDefender = supportGuid is not null ? loadoutSlot.profile.GetItem(supportGuid) : null;
				crewMembers[$"defenderslot{i + 1}"] = JsonSerializer.SerializeToNode(LoadoutBlueprintDefender.FromDefender(supportDefender?.template), Helpers.JsonOptions.Fields);
			}
		}

		GameItem newLoadoutBlueprint = new GameItem(null, 1, new()
		{
			["displayName"] = displayName,
			["crew_members"] = crewMembers,
			["team_perk"] = teamPerk?.templateId,
			["gadgets"] = JsonSerializer.SerializeToNode(gadgetTemplates)
		}, templateId: HeroLoadoutBlueprintTID).SetUUID();
		HeroLoadoutBlueprintDict.Add(newLoadoutBlueprint.uuid, newLoadoutBlueprint);
		SaveHeroLoadoutBlueprints();
	}

	public partial struct LoadoutBlueprintHero
	{
		public string displayTemplate;
		public string heroTemplatePrefix;
		public string heroPerkFallback;

		public static LoadoutBlueprintHero FromHero(GameItemTemplate heroTemplate)
		{
			var abilities = heroTemplate?.GetHeroAbilities();
			if (abilities is null)
				return default;
			return new()
			{
				displayTemplate = heroTemplate.TemplateId,
				heroTemplatePrefix = GameItemTemplate.TierSuffix().Replace(GameItemTemplate.RaritySuffix().Replace(heroTemplate.TemplateId, "_"), "_"),
				heroPerkFallback = abilities[0].TemplateId
			};
		}

		public string ResolveHeroUUID(GameProfile profile)
		{
			if (displayTemplate is null)
				return null;
			var prefix = heroTemplatePrefix;
			var perk = heroPerkFallback;
			var display = displayTemplate;
			var candidates = profile.GetItems("Hero", i => i.template?.GetHeroAbilities()[0]?.TemplateId.Equals(perk, StringComparison.OrdinalIgnoreCase) == true)
				.OrderBy(i => i.templateId != display)
				.ThenBy(i => !i.templateId.StartsWith(prefix))
				.ThenBy(i => -i.template.RarityLevel)
				.ThenBy(i => -i.CalculateRating());
			return candidates.FirstOrDefault()?.uuid;
		}
	}
	public partial struct LoadoutBlueprintDefender
	{
		public string displayTemplate;
		public string defenderType;

		public static LoadoutBlueprintDefender FromDefender(GameItemTemplate defenderTemplate)
		{
			return new()
			{
				displayTemplate = defenderTemplate.TemplateId,
				defenderType = defenderTemplate.SubType
			};
		}

		public string ResolveDefenderUUID(GameProfile profile)
		{
			if (displayTemplate is null)
				return null;
			var subtype = defenderType;
			var display = displayTemplate;
			var candidates = profile.GetItems("Defender", i => i.template?.SubType?.Equals(subtype, StringComparison.OrdinalIgnoreCase) == true)
				.OrderBy(i => i.templateId != display)
				.ThenBy(i => -i.template.RarityLevel)
				.ThenBy(i => -i.CalculateRating());
			return candidates.FirstOrDefault()?.uuid;
		}
	}

	public void RenameHeroLoadoutBlueprint(GameItem loadoutBlueprint, string newName)
	{
		if (!HeroLoadoutBlueprintDict.ContainsKey(loadoutBlueprint.uuid))
		{
			GD.Print($"Loadout Blueprint not found: {loadoutBlueprint.uuid}");
			return;
		}
		loadoutBlueprint.attributes["displayName"] = newName;
		SaveHeroLoadoutBlueprints();
		loadoutBlueprint.NotifyChanged();
	}

	public void RemoveHeroLoadoutBlueprint(GameItem loadoutBlueprint) =>
		RemoveHeroLoadoutBlueprint(loadoutBlueprint.uuid);
	public void RemoveHeroLoadoutBlueprint(string uuid)
	{
		if (!HeroLoadoutBlueprintDict.Remove(uuid))
			return;
		SaveHeroLoadoutBlueprints();
	}

	void SaveHeroLoadoutBlueprints()
	{
		var itemDataDict = HeroLoadoutBlueprintDict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.attributes);
		SetLocalData("heroLoadoutBlueprints", JsonSerializer.SerializeToNode(itemDataDict));
		OnAccountUpdated?.Invoke();
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
		double diff = (DateTime.UtcNow - questsLastRefreshedAt).TotalMinutes;
		bool outOfDate = diff > 5;
		if (localPinnedQuests != null && !outOfDate)
			return accountItems;

		await accountItems.Query(ignoreCache: true);
		questsLastRefreshedAt = DateTime.UtcNow;
		localPinnedQuests = accountItems.statAttributes["client_settings"]?["pinnedQuestInstances"]?.Deserialize<HashSet<string>>() ?? [];
		return accountItems;
	}

	public async Task ClientQuestLoginCampaign()
	{
		await GetProfile(FnProfileTypes.AccountItems)
			.PerformOperation("ClientQuestLogin", @"{""streamingAppKey"": """"}");
	}

	public async Task ClientQuestLoginAthena()
	{
		await GetProfile(FnProfileTypes.CosmeticInventory)
			.PerformOperation("ClientQuestLogin", @"{""streamingAppKey"": """"}");
	}

	public async Task AddPinnedQuest(GameItem item)
	{
		var accountItems = await CheckLocalPinnedQuests();
		if (item.templateId.StartsWith("Quest") && item.profile == accountItems && !localPinnedQuests.Contains(item.uuid))
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
			GD.Print("Pinned Quests: " + localPinnedQuests.Count);
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

	public bool HasPinnedQuest(GameItem item) => item is not null && localPinnedQuests.Contains(item.uuid);

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
		return accountItems.GetItems("Quest", item => item.templateId == notif["newQuestId"].ToString()).FirstOrDefault();
	}

	public bool CanRerollQuest() => (GetProfile(FnProfileTypes.AccountItems).statAttributes?["quest_manager"]?["dailyQuestRerolls"]?.GetValue<int>() ?? 0) > 0;
}

