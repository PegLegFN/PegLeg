using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
            GD.Print($"[{DateTime.Now}] Requesting Missions...");
            missionData ??= await Helpers.MakeRequest(
                    HttpMethod.Get,
                    FnWebAddresses.game,
                    "fortnite/api/game/v2/world/info",
                    "",
                    account.AuthHeader
                );
            var newHash = missionData["missionAlerts"].ToString().Hash();

            GD.Print($"[{DateTime.Now}] {missionHash} >> {newHash}");
            await Helpers.WaitForFrame();
            missionHash = newHash;
            var expiryDate = missionData["missionAlerts"][0]["nextRefresh"].ToString()[..^1]; //the Z messes with daylight savings time
            missionReset = DateTime.Parse(expiryDate, CultureInfo.InvariantCulture);

            List<GameMission> generatedMissions;
            try
            {
                generatedMissions = GenerateMissions(missionData);
                GD.Print("missions parsed");
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
                .ThenBy(m => m.missionGenerator?.DisplayName ?? "AAAAA")
            ];
            OnMissionsUpdated?.Invoke();
            return;
        }
    }

    static string ParseItemPath(string itemPath) => itemPath[(itemPath.LastIndexOf('.') + 1)..itemPath.LastIndexOf('\'')];
    static JsonSerializerOptions serialiserOptions = new() { IncludeFields = true, WriteIndented = true };
    public record struct Requirements
    {
        public int personalPowerRating;
        public int maxPersonalPowerRating;
        public string[] activeQuestDefinitions;
        public string questDefinition;
        public string eventFlag;

        public bool MeetsRequirements(GameAccount account, bool ventures)
        {
            var pl = ventures ? account.VentureFortStats.PowerLevel : account.FortStats.PowerLevel;
            if (pl < personalPowerRating)
                return false;
            if (maxPersonalPowerRating > 0 && pl > maxPersonalPowerRating)
                return false;
            if ((questDefinition ?? "None") != "None")
            {
                var quest = account.GetProfile(FnProfileTypes.AccountItems).GetFirstTemplateItem($"Quest:{ParseItemPath(questDefinition)}");
                if (quest?.QuestComplete != true)
                    return false;
            }
            foreach (var questDef in activeQuestDefinitions)
            {
                var quest = account.GetProfile(FnProfileTypes.AccountItems).GetFirstTemplateItem($"Quest:{ParseItemPath(questDef)}");
                if (quest?.QuestState != "Active")
                    return false;
            }
            return true;
        }
    }

    public record struct ItemReward
    {
        public string itemType;
        public int quantity;
        public GameItem ToItem() => new(GameItemTemplate.Get(itemType), quantity);
    }

    public record struct ItemCollection
    {
        public string tierGroupName;
        public ItemReward[] items;
    }

    public record class TheaterInfo
    {
        //fill this in manually
        public string displayName;
        public string category;

        public Requirements requirements;
        [JsonInclude]
        ModifierPair[] gameplayModifierList;
        struct ModifierPair
        {
            public string eventFlagName;
            public string gameplayModifier;
        }
        public GameItemTemplate[] GetModifiers()
        {
            //for each pair, check if calender has event flag (or if event flag is empty) and get the modifier template 
            return [];
        }
        public override string ToString() => JsonSerializer.Serialize(this, serialiserOptions);
    }

    public record class MissionData
    {
        public string missionGuid;
        public ItemCollection missionRewards;
        public string missionGenerator;
        [JsonInclude]
        DataTableRowRef missionDifficultyInfo;
        public int tileIndex;
        public DateTime availableUntil;

        struct DataTableRowRef
        {
            public string dataTable;
            public string rowName;
        }

        public GameItemTemplate GetMissionGenerator() => GameItemTemplate.Get($"MissionGen:{missionGenerator[(missionGenerator.LastIndexOf('.') + 1)..]}");
        public DifficultyInfo GetDifficultyInfo() => PegLegResourceManager.DifficultyInfo?[missionDifficultyInfo.rowName]?.Deserialize<DifficultyInfo>(serialiserOptions);
        public override string ToString() => JsonSerializer.Serialize(this, serialiserOptions);
    }

    public record class DifficultyInfo
    {
        public int DifficultyLevel;
        public string DisplayName;
        public int MaximumRating;
        public int RecommendedRating;
        public int RequiredRating;
        public override string ToString() => JsonSerializer.Serialize(this, serialiserOptions);
    }

    public record class AlertData
    {
        public string missionAlertGuid;
        public int tileIndex;
        public DateTime availableUntil;
        public ItemCollection missionAlertRewards;
        public ItemCollection missionAlertModifiers;
        public override string ToString() => JsonSerializer.Serialize(this, serialiserOptions);
    }

    public record class Tile
    {
        public string tileType;
        [JsonInclude]
        string zoneTheme;
        public GameItemTemplate GetZoneTheme() => GameItemTemplate.Get($"ZoneTheme:{zoneTheme[(zoneTheme.IndexOf('.') + 1)..]}");
        public Requirements requirements;
        [JsonInclude]
        int xCoordinate;
        [JsonInclude]
        int yCoordinate;
        [JsonIgnore]
        public Vector2I Coordinates => new(xCoordinate, yCoordinate);
        public override string ToString() => JsonSerializer.Serialize(this, serialiserOptions);
    }

    public record class Region
    {
        [JsonInclude]
        int[] tileIndices;
        FrozenSet<int> tileSet;
        public bool IncludesTile(int idx)
        {
            tileSet ??= (tileIndices ?? []).ToFrozenSet();
            return tileSet.Contains(idx);
        }
        public Requirements requirements;
        //display mission weights to user?
        public override string ToString() => JsonSerializer.Serialize(this, serialiserOptions);
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
            var theaterInfo = theater["runtimeInfo"].Deserialize<TheaterInfo>(serialiserOptions) with
            {
                displayName = theaterName,
                category = theaterCat,
            };

            //Missions
            var theaterMissions = allMissions
                .FirstOrDefault(t => t["theaterId"].ToString() == theaterID)
                ["availableMissions"]
                .Deserialize<MissionData[]>(serialiserOptions);

            //Mission Alerts (indexed by Tile Index, as that is the common factor between missions and mission alerts)
            var missionAlertDict = allMissionAlerts
                .FirstOrDefault(t => t["theaterId"].ToString() == theaterID)
                ["availableMissionAlerts"]
                .Deserialize<AlertData[]>(serialiserOptions)
                .Reverse()
                .DistinctBy(a => a.tileIndex)
                .ToDictionary(a => a.tileIndex);

            var missionTiles = theater["tiles"].Deserialize<Tile[]>(serialiserOptions);

            var missionRegionList = theater["regions"].Deserialize<Region[]>(serialiserOptions);

            Parallel.ForEach(theaterMissions, missionData =>
            {
                if (missionData.missionGenerator.Contains("_TheOutpost_"))
                    return;
                missionList.Add(new(
                    theaterInfo,
                    [.. missionRegionList.Where(r => r.IncludesTile(missionData.tileIndex) == true)],
                    missionTiles[missionData.tileIndex],
                    missionData,
                    missionAlertDict.TryGetValue(missionData.tileIndex, out var alertData) ? alertData : null
                ));
            });
        }
        return missionList;
    }

    #endregion

    public TheaterInfo theaterInfo { get; private set; }
    public MissionData missionData { get; private set; }
    public AlertData alertData { get; private set; }
    public DifficultyInfo difficultyInfo { get; private set; }
    public Tile tile { get; private set; }
    public Region[] regions { get; private set; }

    public string DisplayName => missionGenerator?.DisplayName;
    public string Description => missionGenerator?.Description;
    public string Location => zoneTheme?.DisplayName;
    public string LocationDescription => zoneTheme?.Description;
    public int PowerLevel => difficultyInfo?.RecommendedRating ?? 0;
    public int MinPower => difficultyInfo?.RequiredRating ?? 0;
    public int MaxPower => difficultyInfo?.MaximumRating ?? 0;
    public string TheaterName => theaterInfo.displayName.EndsWith("Venture Zone") ?
        theaterInfo.displayName[..^13] :
        theaterInfo.displayName;
    public string TheaterCat => theaterInfo.category;
    public int TheaterIdx => TheaterCat switch
        {
            "s" => 0,
            "p" => 1,
            "c" => 2,
            "t" => 3,
            "v" => 4,
            _ => 0
        };
    public int TileIdx => missionData.tileIndex;
    public bool IsFourPlayer => difficultyInfo?.DisplayName?.EndsWith("4 Players") == true;
    public bool IsStoryMission => (tile?.requirements.activeQuestDefinitions?.Length ?? 0) > 0;
    public bool HasLargeReward { get; private set; }

    JsonObject searchObject;
    public JsonObject SearchObject => searchTags is null ? [] : (searchObject ??= new() { ["searchTags"] = searchTags });
    public JsonArray searchTags { get; private set; }

    public GameItemTemplate missionGenerator { get; private set; }
    public GameItemTemplate zoneTheme { get; private set; }
    public Texture2D backgroundTexture =>
            missionGenerator.GetTexture(FnItemTextureType.LoadingScreen, null) ??
            zoneTheme.GetTexture(FnItemTextureType.LoadingScreen, null);

    public GameItem[] rewardItems { get; private set; }
    public GameItem[] alertModifiers { get; private set; }
    public GameItem[] alertRewardItems { get; private set; }

    public IEnumerable<GameItem> allItems => alertRewardItems?.Union(rewardItems) ?? rewardItems;

    GameMission(TheaterInfo theaterInfo, Region[] regions, Tile tile, MissionData missionData, AlertData alertData)
    {
        this.theaterInfo = theaterInfo;
        this.regions = regions;
        this.tile = tile;
        this.missionData = missionData;
        this.alertData = alertData;

        difficultyInfo = missionData.GetDifficultyInfo();
        missionGenerator = missionData.GetMissionGenerator();
        zoneTheme = tile.GetZoneTheme();

        if (missionGenerator is null || zoneTheme is null)
            return;

        Dictionary<string, GameItem> rewardItemList = [];
        foreach (var itemData in missionData.missionRewards.items ?? [])
        {
            GameItem item = itemData.ToItem();
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
            foreach (var itemData in alertData.missionAlertModifiers.items ?? [])
            {
                GameItem modifier = itemData.ToItem();
                modifier.SetSeenLocal();
                modifier.GetSearchTags();
                alertModifierList.Add(modifier);
            }
            alertModifiers = [.. alertModifierList];

            List<GameItem> alertRewardItemList = [];

            foreach (var itemData in alertData.missionAlertRewards.items ?? [])
            {
                GameItem item = itemData.ToItem();
                item.GetSearchTags();
                alertRewardItemList.Add(item);
            }
            alertRewardItems = [.. alertRewardItemList];
        }
        alertModifiers ??= [];
        alertRewardItems ??= [];

        searchTags ??= [];
        if (IsFourPlayer)
            searchTags.Add("Group");
        if(alertModifiers.Length>0)
            searchTags.Add("Alert");
        if (TheaterCat=="v")
            searchTags.Add("Ventures");
        //this is super lazy, i dont want to figure out how to query the total of specific items procedurally
        if (
            rewardItems.Where(i =>
                i.sortingTemplate.Name.StartsWith("Reagent_Alteration_Upgrade", StringComparison.InvariantCultureIgnoreCase) ||
                i.sortingTemplate.Name.Equals("Reagent_Alteration_Generic", StringComparison.InvariantCultureIgnoreCase) ||
                i.sortingTemplate.Name.StartsWith("Reagent_C", StringComparison.InvariantCultureIgnoreCase) ||
                i.sortingTemplate.Name.Equals("PersonnelXP", StringComparison.InvariantCultureIgnoreCase) ||
                i.sortingTemplate.Name.Equals("SchematicXP", StringComparison.InvariantCultureIgnoreCase) ||
                i.sortingTemplate.Name.Equals("HeroXP", StringComparison.InvariantCultureIgnoreCase)
            ).Select(i => i.quantity).Sum() >= 4
        )
        {
            HasLargeReward = true;
            searchTags.Add("LargeReward");
        }
        searchTags.Add(PowerLevel);
        searchTags.Add(Location);
        searchTags.Add(TheaterName);
    }

    public bool PlayableBy(GameAccount account)
    {
        return
            theaterInfo.requirements.MeetsRequirements(account, TheaterCat == "v") &&
            tile.requirements.MeetsRequirements(account, TheaterCat == "v") && 
            regions.All(r => r.requirements.MeetsRequirements(account, TheaterCat == "v"));
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

    //old code, use PlayableBy instead
    async Task<bool> MissionIsPlayable(GameAccount byAccount=null)
    {
        //byAccount ??= GameAccount.activeAccount;
        //if(!await byAccount.Authenticate())
        //    return false;
        //var powerLevel = byAccount.GetFORTStats().PowerLevel;
        //bool isAboveMin = powerLevel >= difficultyInfo["RequiredRating"].GetValue<int>();
        //bool isBelowMax = powerLevel <= difficultyInfo["MaximumRating"].GetValue<int>();
        //if(!isAboveMin || (isBelowMax && TheaterCat=="v"))
        //    return false;

        //string requiredQuest = tileData["requirements"]["questDefinition"].ToString().Split(".")[^1];
        //bool requiredQuestCheckPassed = requiredQuest == "None" ||
        //    (
        //        (await byAccount.GetProfile(FnProfileTypes.AccountItems).Query()).GetFirstTemplateItem("Quest") is GameItem targetQuest &&
        //        targetQuest.QuestComplete
        //    );
        //if(!requiredQuestCheckPassed)
        //    return false;

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
