using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Godot;

/* Old Requests
static class MissionRequests
{
    const string missionCacheSavePath = "user://missions.json";
    const int MissionVersion = 4;

    public static uint missionHash { get; private set; }
    static DateTime missionReset;
    public static GameMission[] currentMissions { get; private set; }

    public static async Task<bool> CheckForMissionChanges()
    {
        var account = GameAccount.activeAccount;
        if (!await account.Authenticate())
            return false;

        JsonNode fullMissions = await Helpers.MakeRequest(
                HttpMethod.Get,
                FnEndpoints.gameEndpoint,
                "fortnite/api/game/v2/world/info",
                "",
                account.AuthHeader
            );

        var newHash = fullMissions["missionAlerts"].ToString().Hash();
        if (missionHash == 0)
        {
            missionHash = newHash;
            return false;
        }
        if (missionHash != newHash)
        {
            GD.Print(missionHash + " >> " + newHash);
            missionHash = newHash;
            currentMissions = null;
            missionReset = DateTime.Now;
            return true;
        }
        return false;
    }

    public static bool MissionsEmptyOrOutdated(uint? compareHash = null)
    {
        if (compareHash is not null && compareHash != missionHash)
            return true;
        if (currentMissions is null)
            return true;
        return DateTime.UtcNow.CompareTo(missionReset) >= 0;
    }

    static SemaphoreSlim missionSemaphore = new(1);
    static bool forceQueued = false;
    static bool isBeingForced = false;
    public static async Task<GameMission[]> GetMissions(bool forceRefresh = false)
    {
        if (!isBeingForced && forceRefresh)
            forceQueued = true;

        await missionSemaphore.WaitAsync();
        try
        {
            JsonObject missionData = null;
            if (forceQueued)
            {
                GD.Print("forcing refresh");
                isBeingForced = true;
                currentMissions = null;
            }
            else if (currentMissions is not null && DateTime.UtcNow.CompareTo(missionReset) >= 0)
            {
                GD.Print("missions expired");
                currentMissions = null;
            }
            else if (currentMissions is null && FileAccess.FileExists(missionCacheSavePath))
            {
                //load from file
                using FileAccess missionFile = FileAccess.Open(missionCacheSavePath, FileAccess.ModeFlags.Read);
                missionData = JsonNode.Parse(missionFile.GetAsText()).AsObject();
                if(missionData["version"]?.GetValue<int>()== MissionVersion)
                {
                    missionHash = missionData["hash"]?.AsValue().TryGetValue(out uint hashVal) ?? false ? hashVal : 0;
                    missionReset = DateTime.Parse(missionData["expiryDate"]?.ToString(), CultureInfo.InvariantCulture);
                    Debug.WriteLine("mission file loaded");
                }
                else
                {
                    missionData = null;
                    Debug.WriteLine("mission file version mismatch");
                }
            }
            forceQueued = false;

            if(currentMissions is not null)
                return currentMissions;

            if (missionData is null)
            {
                GD.Print("requesting missions");
                missionData = await RequestMissions();
            }

            //GD.Print("max rewards: " + missionsCache.SelectMany(kvp => kvp.Value.AsArray().Select(m => m["missionRewards"].AsArray().Count)).Max());
            //GD.Print("max alert rewards: " + missionsCache.SelectMany(kvp => kvp.Value.AsArray().Select(m => m["missionAlert"]?["rewards"].AsArray().Count ?? 0)).Max());
            currentMissions = await GenerateMissions(missionData);
            return currentMissions;
        }
        finally
        {
            isBeingForced = false;
            missionSemaphore.Release();
        }
    }

    static async Task<JsonObject> RequestMissions()
    {
        var account = GameAccount.activeAccount;
        if (!await account.Authenticate())
            return null;

        GD.Print("retrieving missions from epic...");
        JsonNode missionData = await Helpers.MakeRequest(
                HttpMethod.Get,
                FnEndpoints.gameEndpoint,
                "fortnite/api/game/v2/world/info",
                "",
                account.AuthHeader
            );
        if(missionData["errorMessage"] is not null)
        {
            GD.Print("Error: "+missionData.ToString());
            return null;
        }
        var alerts = missionData["missionAlerts"];
        missionData["version"] = MissionVersion;
        missionData["expiryDate"] = alerts[0]["nextRefresh"].ToString()[..^1]; //the Z messes with daylight savings time
        missionReset = DateTime.Parse(missionData["expiryDate"].ToString(), CultureInfo.InvariantCulture);
        missionData["hash"] = missionHash = alerts.ToString().Hash();
        missionHash = missionData["hash"].GetValue<uint>();
        //GD.Print(fullMissions.ToString()[..350] + "...");
        //GD.Print(missionsCache.ToString()[..350] + "...");
        //save to file

        using FileAccess missionFile = FileAccess.Open(missionCacheSavePath, FileAccess.ModeFlags.Write);
        missionFile.StoreString(missionData.ToString());
        missionFile.Flush();
        return missionData.AsObject();
    }
}
*/

public class GameMission
{
    #region Static

    public static event Action OnMissionsUpdated;
    public static event Action OnMissionsInvalidated;
    public static GameMission[] currentMissions { get; private set; }
    public static DateTime missionReset { get; private set; }


    static uint? missionHash;
    static SemaphoreSlim missionCheckSemaphore = new(1);
    static bool checkMissionsState = false;
    public static async Task<bool> MissionsNeedUpdate(bool ignoreHashCheck = false)
    {
        var (result, _) = await MissionsNeedUpdateInternal(ignoreHashCheck);
        return result;
    }
    static async Task<(bool, JsonNode)> MissionsNeedUpdateInternal(bool ignoreHashCheck = false)
    {
        using var st = await missionCheckSemaphore.AwaitToken();
        if (!st.wasImmediate)
            return (checkMissionsState, null);

        if (DateTime.UtcNow.CompareTo(missionReset) >= 0)
            return (checkMissionsState = true, null);

        if (!ignoreHashCheck)
            return (checkMissionsState = false, null);

        var account = GameAccount.activeAccount;
        if (!await account.Authenticate())
            return (checkMissionsState = false, null);

        JsonNode missionData = await Helpers.MakeRequest(
                HttpMethod.Get,
                FnWebAddresses.game,
                "fortnite/api/game/v2/world/info",
                "",
                account.AuthHeader
            );

        if (missionData["errorMessage"] is not null)
        {
            GD.Print("Error: " + missionData.ToString());
            return (checkMissionsState = false, missionData);
        }

        var newHash = missionData["missionAlerts"].ToString().Hash();
        if (missionHash == newHash)
            return (checkMissionsState = false, missionData);

        return (checkMissionsState = true, missionData);
    }

    public static async Task CheckMissions(bool ignoreHashCheck = false)
    {
        var (result, missionData) = await MissionsNeedUpdateInternal(ignoreHashCheck);
        if (result)
            await UpdateMissions(missionData);
    }

    static SemaphoreSlim missionUpdateSemaphore = new(1);
    public static async Task UpdateMissions() => await UpdateMissions(null);

    static async Task UpdateMissions(JsonNode missionData)
    {
        using var st = await missionUpdateSemaphore.AwaitToken();
        if (!st.wasImmediate)
            return;

        var account = GameAccount.activeAccount;
        if (!await account.Authenticate())
            return;

        currentMissions = null;
        missionReset = DateTime.Now;
        OnMissionsInvalidated?.Invoke();

        int retriesRemaining = 3;

        while (true)
        {
            missionData ??= await Helpers.MakeRequest(
                    HttpMethod.Get,
                    FnWebAddresses.game,
                    "fortnite/api/game/v2/world/info",
                    "",
                    account.AuthHeader
                );
            var newHash = missionData["missionAlerts"].ToString().Hash();

            GD.Print(missionHash + " >> " + newHash);
            missionHash = newHash;
            var expiryDate = missionData["missionAlerts"][0]["nextRefresh"].ToString()[..^1]; //the Z messes with daylight savings time
            missionReset = DateTime.Parse(expiryDate, CultureInfo.InvariantCulture);

            List<GameMission> generatedMissions;
            try
            {
                generatedMissions = GenerateMissions(missionData);
            }
            catch(Exception e)
            {
                GD.PushWarning(e);
                missionData = null;
                if (retriesRemaining > 0)
                {
                    GD.Print("retrying missions");
                    retriesRemaining--;
                    continue;
                }
                else
                {
                    throw;
                }
            }

            //edge case where missions expire after being requested but before being converted to MissionEntries
            if (await MissionsNeedUpdate(true))
            {
                missionData = null;
                continue;
            }

            currentMissions = 
            [.. generatedMissions
                .Where(m => m is not null)
                .OrderBy(m => m.TheaterIdx)
                .ThenBy(m => m.PowerLevel)
                .ThenBy(m => m.IsFourPlayer)
                .ThenBy(m => m.missionGenerator?.template?.DisplayName ?? "AAAAA")
            ];
            OnMissionsUpdated?.Invoke();
            return;
        }
    }

    static List<GameMission> GenerateMissions(JsonNode rootNode)
    {
        //Theaters
        List<string> allowedTheaterIDs =
        [
            "33A2311D4AE64B361CCE27BC9F313C8B",
            "D477605B4FA48648107B649CE97FCF27",
            "E6ECBD064B153234656CB4BDE6743870",
            "D9A801C5444D1C74D1B7DAB5C7C12C5B"
        ];

        //ventures theater
        var venturesTheater = rootNode["theaters"]
            .AsArray()
            .FirstOrDefault(t => t["missionRewardNamedWeightsRowName"]?.ToString() == "Theater.Phoenix");
        if (venturesTheater is not null)
            allowedTheaterIDs.Add(venturesTheater["uniqueId"].ToString());

        JsonArray allMissions = rootNode["missions"].AsArray();
        JsonArray allMissionAlerts = rootNode["missionAlerts"].AsArray();

        List<GameMission> missionList = [];

        //int counter = 0;
        foreach (var theaterID in allowedTheaterIDs)
        {
            var theater = rootNode["theaters"].AsArray().First(t => t["uniqueId"].ToString() == theaterID);
            if (theater is null)
                continue;

            string theaterName = theater["displayName"]["en"].ToString();
            string theaterCat = theaterName switch
            {
                "Stonewood" => "s",
                "Plankerton" => "p",
                "Canny Valley" => "c",
                "Twine Peaks" => "t",
                _ => "v"
            };
            bool isVentures = theaterCat == "v";

            //Missions
            JsonArray theaterMissions = allMissions.FirstOrDefault(t => t["theaterId"].ToString() == theaterID)["availableMissions"].AsArray();

            //Mission Alerts (indexed by Tile Index, as that is the common factor between missions and mission alerts)
            var missionAlertDict = allMissionAlerts
                .FirstOrDefault(t => t["theaterId"].ToString() == theaterID)
                ["availableMissionAlerts"]
                .AsArray()
                .Reverse()
                .DistinctBy(n => n["tileIndex"].GetValue<int>())
                .ToDictionary(n => n["tileIndex"].GetValue<int>());

            var missionTiles = theater["tiles"].AsArray();
            //List<JsonObject> missionsToDetach = new();

            void ProcessMission(JsonNode missionObj)
            {
                string missionGen = missionObj["missionGenerator"].ToString();

                int tileIndex = missionObj["tileIndex"].GetValue<int>();
                var tileData = missionTiles[tileIndex].AsObject();
                missionTiles.Remove(tileIndex);

                JsonObject alertObj = null;
                if (missionAlertDict.ContainsKey(tileIndex))
                {
                    alertObj = missionAlertDict[tileIndex].AsObject();
                    missionAlertDict.Remove(tileIndex);
                }

                //skip Homebase and quest exclusive missions
                if (
                    missionGen.Contains("_TheOutpost_") ||
                    tileData["requirements"]["activeQuestDefinitions"].AsArray().Count > 0 //||
                    //tileData["requirements"]["eventFlag"].ToString() != ""
                    )
                    return;

                missionObj["theaterCat"] = theaterCat;
                missionObj["theaterName"] = theaterName;

                missionList.Add(new(missionObj.AsObject(), alertObj, tileData));
            }

            Parallel.ForEach(theaterMissions, ProcessMission);
            //foreach (var missionObj in theaterMissions)
            //{
            //    ProcessMission(missionObj);
            //}

            //foreach (var mission in missionList)
            //{
            //    mission.PreloadResources();
            //}

            //detach from original json
            theaterMissions.Clear();
            //foreach (var mission in missionsToDetach)
            //{
            //    theaterMissions.Remove(mission);
            //}
        }
        return missionList;
    }

    #endregion


    public JsonObject missionData { get; private set; }
    public JsonObject alertData { get; private set; }
    public JsonObject tileData { get; private set; }
    public JsonObject difficultyInfo { get; private set; }

    public string DisplayName => missionGenerator?.template?.DisplayName;
    public string Description => missionGenerator?.template?.Description;
    public string Location => zoneTheme?.template?.DisplayName;
    public string LocationDescription => zoneTheme?.template?.Description;
    public int PowerLevel => difficultyInfo?["RecommendedRating"]?.GetValue<int>() ?? 0;
    public string TheaterName => missionData["theaterName"].ToString() is string theaterName ?
        (theaterName.EndsWith("Venture Zone") ? theaterName[..13] : theaterName) :
        null;
    public string TheaterCat => missionData["theaterCat"].ToString();
    public int TheaterIdx => TheaterCat switch
        {
            "s" => 0,
            "p" => 1,
            "c" => 2,
            "t" => 3,
            "v" => 4,
            _ => 0
        };
    public int TileIdx => missionData["tileIndex"]?.GetValue<int>() ?? -1;
    public bool IsFourPlayer => difficultyInfo?["DisplayName"]?.ToString().EndsWith("4 Players") ?? false;
    public JsonArray SearchTags => missionData["searchTags"]?.AsArray();

    public GameItem missionGenerator { get; private set; }
    public GameItem zoneTheme { get; private set; }
    public Texture2D backgroundTexture =>
            missionGenerator.GetTexture(FnItemTextureType.LoadingScreen, null) ??
            zoneTheme.GetTexture(FnItemTextureType.LoadingScreen, null);

    public GameItem[] rewardItems { get; private set; }
    public GameItem[] alertModifiers { get; private set; }
    public GameItem[] alertRewardItems { get; private set; }

    public IEnumerable<GameItem> allItems => alertRewardItems?.Union(rewardItems) ?? rewardItems;

    public GameMission(JsonObject missionData, JsonObject alertData, JsonObject tileData)
    {
        this.missionData = missionData;
        this.alertData = alertData;
        this.tileData = tileData;
        difficultyInfo = PegLegResourceManager.DifficultyInfo?[missionData["missionDifficultyInfo"]["rowName"].ToString()]?.AsObject();

        missionGenerator = GameItemTemplate.Get($"MissionGen:{missionData["missionGenerator"].ToString().Split(".")[1]}")?.CreateInstance();
        zoneTheme = GameItemTemplate.Get($"ZoneTheme:{tileData["zoneTheme"].ToString().Split(".")[1]}")?.CreateInstance();

        if (missionGenerator is null || zoneTheme is null)
            return;

        Dictionary<string, GameItem> rewardItemList = [];
        foreach (var itemData in missionData["missionRewards"]["items"].AsArray().DetachAll())
        {
            GameItem item = new(null, null, itemData.AsObject());
            item.GetSearchTags();
            var match = Regex.Match(item.template.Name.ToLower(), "zcp_.*t\\d{1,2}");
            string key = match.Success ?
                match.Groups[0].Value :
                item.template.Name.ToLower();
            if (rewardItemList.TryGetValue(key, out GameItem targetItem))
            {
                targetItem.SetLocalQuantity(targetItem.quantity + item.quantity);
            }
            else
            {
                rewardItemList.Add(key, item);
            }
        }
        rewardItems = [.. rewardItemList.Values];

        if (alertData is not null)
        {
            List<GameItem> alertModifierList = [];
            if (alertData["missionAlertModifiers"]?["items"]?.AsArray() is JsonArray modifierData)
            {
                foreach (var itemData in modifierData)
                {
                    GameItem modifier = new(null, null, itemData.AsObject());
                    modifier.SetSeenLocal();
                    modifier.GetSearchTags();
                    alertModifierList.Add(modifier);
                }
            }
            alertModifiers = [.. alertModifierList];

            List<GameItem> alertRewardItemList = [];
            if (alertData["missionAlertRewards"]?["items"]?.AsArray() is JsonArray rewardData)
            {
                foreach (var itemData in rewardData)
                {
                    GameItem item = new(null, null, itemData.AsObject());
                    var __ = item.template;
                    item.GetSearchTags();
                    alertRewardItemList.Add(item);
                }
            }
            alertRewardItems = [.. alertRewardItemList];
        }
        alertModifiers ??= [];
        alertRewardItems ??= [];

        JsonArray searchTags = [];
        if (IsFourPlayer)
            searchTags.Add("Group");
        if(alertModifiers.Length>0)
            searchTags.Add("Alert");
        if (TheaterCat=="v")
            searchTags.Add("Ventures");
        //this is super lazy, i dont want to figure out how to query the total of specific items procedurally
        if (rewardItems.Where(i =>
                i.sortingTemplate.Name.StartsWith("Reagent_Alteration_Upgrade") ||
                i.sortingTemplate.Name == "Reagent_Alteration_Generic" ||
                i.sortingTemplate.Name.StartsWith("Reagent_C") ||
                i.sortingTemplate.Name== "PersonnelXP" ||
                i.sortingTemplate.Name == "SchematicXP" ||
                i.sortingTemplate.Name == "HeroXP"
            ).Select(i => i.quantity).Sum() >= 4)
            searchTags.Add("LargeReward");
        searchTags.Add(PowerLevel);
        searchTags.Add(Location);
        searchTags.Add(TheaterName);
        missionData["searchTags"] = searchTags;
    }

    public void PreloadResources()
    {
        missionGenerator.GetTexture(FnItemTextureType.Icon);
        var _ = backgroundTexture;
        foreach (var item in allItems)
        {
            item.GetTexture();
        }
    }

    public async Task SetMissionPlayableTag(GameAccount byAccount = null)
    {
        bool playable = await MissionIsPlayable(byAccount);
        var searchTags = missionData["searchTags"]?.AsArray();
        if (playable == searchTags.Contains("Playable"))
            return;
        if (playable)
            searchTags.Add("Playable");
        else
            searchTags.Remove("Playable");
    }

    public async Task<bool> MissionIsPlayable(GameAccount byAccount=null)
    {
        byAccount ??= GameAccount.activeAccount;
        if(!await byAccount.Authenticate())
            return false;
        var powerLevel = byAccount.GetFORTStats().PowerLevel;
        bool isAboveMin = powerLevel >= difficultyInfo["RequiredRating"].GetValue<int>();
        bool isBelowMax = powerLevel <= difficultyInfo["MaximumRating"].GetValue<int>();
        if(!isAboveMin || (isBelowMax && TheaterCat=="v"))
            return false;

        string requiredQuest = tileData["requirements"]["questDefinition"].ToString().Split(".")[^1];
        bool requiredQuestCheckPassed = requiredQuest == "None" ||
            (
                (await byAccount.GetProfile(FnProfileTypes.AccountItems).Query()).GetFirstTemplateItem("Quest") is GameItem targetQuest &&
                targetQuest.QuestComplete
            );
        if(!requiredQuestCheckPassed)
            return false;

        //implement more checks in future

        return true;
    }

    public void UpdateRewardNotifications(bool force = false)
    {
        foreach (var item in allItems)
        {
            item.SetRewardNotification(null, force);
        }
    }
}
