using Amazon.S3;
using Godot;
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

public partial class GameMission
{
    #region Static

    public static event Action OnMissionsUpdated;
    public static event Action OnMissionsInvalidated;
    public static GameMission[] currentMissions { get; private set; }
    public static DateTime missionReset { get; private set; }
    public static ImageTexture DailyCat {  get; private set; }

    static Dictionary<DateTime, ArchiveData> loadedArchives = [];
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

        var missionResponse = await RequestMissions();

        if (await missionResponse.CheckForError())
            return (checkMissionsState = false, null);

        JsonNode missionData = await missionResponse.ReadJson<JsonObject>();

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

    public static async Task ReparseMissions(JsonNode customData = null) => await UpdateMissions(customData ?? recentMissionData, customData is not null);

    static JsonNode recentMissionData;

    static async Task<HttpResponseMessage> RequestMissions()
    {
        if (!GameAccount.ActiveAccount.isOwned)
        {
            const string litePath = "user://latestLiteMissions.json";
            if (FileAccess.FileExists(litePath) && (DateTime.UtcNow.Hour > 0 || DateTime.UtcNow.Minute > 10))
            {
                using var latestMissionsFile = FileAccess.Open(litePath, FileAccess.ModeFlags.Read);
                if (latestMissionsFile.GetError() == Error.Ok)
                {
                    string missionText = latestMissionsFile.GetAsText();
                    var missionData = JsonNode.Parse(missionText);
                    var expiryDate = missionData["missionAlerts"][0]["nextRefresh"].ToString()[..^1]; //the Z messes with daylight savings time
                    var reset = DateTime.Parse(expiryDate, CultureInfo.InvariantCulture);
                    if (reset > DateTime.UtcNow)
                        return new HttpResponseMessage() { Content = new StringContent(missionText) };
                    GD.Print("latest lite missions out of date");
                }
            }
            return await ApiWebAddresses.pegLegLiteBucket
                .MakeRequest("latestMissions.json")
                .Send();
        }
        return await FnWebAddresses.FortGame
                .MakeRequest("fortnite/api/game/v2/world/info")
                .SetAccount()
                .Send();
    }

    static AmazonS3Client BucketClient = null;

    static async Task UpdateMissions(JsonNode missionData, bool ignoreExpiry = false)
    {
        using var st = await missionUpdateSemaphore.AwaitToken();
        if (!st.wasImmediate)
            return;
        bool delayFirst = DateTime.UtcNow.Hour == 0 && DateTime.UtcNow.Minute == 0 && DateTime.UtcNow.Second == 0;
        bool retryLiteMissions = DateTime.UtcNow.Hour == 0 && DateTime.UtcNow.Minute == 0 && DateTime.UtcNow.Second <30;;

        currentMissions = null;
        missionReset = DateTime.UtcNow;
        OnMissionsInvalidated?.Invoke();

        int totalRetries = 0;

        while (true)
        {
            if (delayFirst)
            {
                //if request is made exactly on the hour, its likely for daily reset.
                //requesting missions exactly at reset can cause consistancy issues, so
                //we add a 1 second delay before the request
                if (!GameAccount.ActiveAccount.isOwned)
                {
                    GD.Print("pausing 5 seconds to wait for lite missions");
                    await Helpers.WaitForTimer(5);
                }
                {
                    GD.Print("pausing 1 second to ensure missions are accurate");
                    await Helpers.WaitForTimer(1);
                }
                missionData = null;
            }
            GD.Print($"Requesting Missions...");

            FetchDailyCat();
            var missionResponse = await RequestMissions();

            if (await missionResponse.CheckForError())
                return;

            missionData = await missionResponse.ReadJson<JsonObject>();

            recentMissionData = missionData.DeepClone();
            var newHash = missionData["missionAlerts"].ToString().Hash();

            GD.Print($"[{DateTime.UtcNow}] {missionHash?.ToString() ?? "{No Missions}"} >> {newHash}");
            await Helpers.WaitForFrame();
            missionHash = newHash;
            var resetString = missionData["missionAlerts"][0]["nextRefresh"].ToString()[..^1]; //the Z messes with daylight savings time
            missionReset = DateTime.Parse(resetString, CultureInfo.InvariantCulture);

            List<GameMission> generatedMissions;
            try
            {
                generatedMissions = GenerateMissions(missionData);
                GD.Print("missions parsed");
            }
            catch(Exception e)
            {
                GD.PushWarning(e);
                generatedMissions = [];
            }

            //edge case where missions expire after being requested but before the response is returned
            if (!ignoreExpiry && missionReset < DateTime.UtcNow)
            {
                if(totalRetries < 3 && retryLiteMissions)
                {
                    totalRetries++;
                    missionData = null;
                    GD.Print("lite missions still out of date, pausing 5 more seconds");
                    await Helpers.WaitForTimer(5);
                    continue;
                }
                else if (totalRetries >= 3)
                {
                    GD.Print("abandoning retries");
                }
                GD.Print("Warning: lite missions are out of date");
                //todo: show onscreen error
            }

            if (missionReset > DateTime.UtcNow)
            {
                try
                {
                    string latestOutput = AppConfig.Get("missions", "latest_output", "");
                    if (string.IsNullOrWhiteSpace(latestOutput))
                        latestOutput = "user://latestMissions";
                    latestOutput += ".json";
                    string latestOutputParent = latestOutput[..(latestOutput.LastIndexOf('/') + 1)];
                    if (!DirAccess.DirExistsAbsolute(latestOutputParent))
                        DirAccess.MakeDirAbsolute(latestOutputParent);
                    using var latestMissionsFile = FileAccess.Open(latestOutput, FileAccess.ModeFlags.Write);
                    latestMissionsFile.StoreString(recentMissionData.ToString());
                }
                catch { }

                SendMissionsToBucket();
            }

            currentMissions =
            [
                .. generatedMissions
                .Where(m => m is not null)
                .OrderBy(m => m.TheaterIdx)
                .ThenBy(m => m.PowerLevel)
                .ThenBy(m => m.IsFourPlayer)
                .ThenBy(m => m.missionGenerator?.DisplayName ?? "AAAAA")
            ];
            OnMissionsUpdated?.Invoke();

            ArchiveMissions();

            return;
        }
    }

    static DateTime lastBucketedAt;
    static async void SendMissionsToBucket()
    {
        if (
            !GameAccount.ActiveAccount.isOwned ||
            !AppConfig.TryGet("missions", "bucketAccessID", out string bucketAccessID) ||
            !AppConfig.TryGet("missions", "bucketAccessSecret", out string bucketAccessSecret) ||
            !AppConfig.TryGet("missions", "bucketURL", out string bucketURL)
        )
            return;

        if ((lastBucketedAt - DateTime.UtcNow).TotalDays < 1 && lastBucketedAt.Date == DateTime.UtcNow.Date)
            return;

        var missionData = recentMissionData.ToString();
        const string litePath = "user://liteMissions.json";
        if (FileAccess.FileExists(litePath))
        {
            using var latestMissionsFile = FileAccess.Open(litePath, FileAccess.ModeFlags.Read);
            if (latestMissionsFile.GetError() == Error.Ok && missionData.Hash() == latestMissionsFile.GetAsText().Hash())
                return;
        }

        lastBucketedAt = DateTime.UtcNow;

        using (var latestMissionsFile = FileAccess.Open(litePath, FileAccess.ModeFlags.Write))
            latestMissionsFile.StoreString(missionData);
        var fullPath = ProjectSettings.GlobalizePath(litePath);

        BucketClient ??= new(bucketAccessID, bucketAccessSecret, new AmazonS3Config()
        {
            ServiceURL = bucketURL
        });
        var response = await BucketClient.PutObjectAsync(new() {
            BucketName = "pegleg-lite-data",
            Key = "latestMissions.json",
            FilePath = fullPath,
            DisablePayloadSigning = true
        });
        var statusCode = (int)response.HttpStatusCode;
        if (statusCode<200 || statusCode > 299)
        {
            GD.Print($"Bucket Upload Failure, status: {response.HttpStatusCode}");
        }
    }

    //https://cataas.com/cat?width=64&height=64
    static async void FetchDailyCat()
    {
        var catResponse = await WebHelpers
                .MakeRequest("https://cataas.com/cat?width=64&height=64")
                .AddHeader("Accept", "image/jpeg")
                .Send();
        if (await catResponse.CheckForError())
            return;
        var catImage = await catResponse.ReadImage();
        if (catImage is null)
            return;
        if (DailyCat is null || DailyCat.GetFormat() != catImage.GetFormat())
            DailyCat = ImageTexture.CreateFromImage(catImage);
        else
            DailyCat.Update(catImage);
    }

    static JsonSerializerOptions archiveSerialisation = new()
    {
        IncludeFields = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    static DiscordWebhookProxy archiveWebhook;
    static void ArchiveMissions()
    {
        if (!AppConfig.Get("advanced", "archive_missions", false))
            return;
        bool didArchive = ArchiveMissions(currentMissions, missionReset, out string archiveName, out string archivePath);
        if (!didArchive)
            return;
        //optionally post a webhook message for each new file generated
        archiveWebhook ??= new("PegLeg Mission Archive", "missionArchive");
        archiveWebhook.Execute(
            () => Task.FromResult(archiveName),
            () => Task.FromResult<string[]>([archivePath])
        ).StartTask();
    }

    public static void ManuallyCreateArchive(JsonNode missionData)
    {
        var resetString = missionData["missionAlerts"][0]["nextRefresh"].ToString()[..^1]; //the Z messes with daylight savings time
        var resetDateTime = DateTime.Parse(resetString, CultureInfo.InvariantCulture);
        var missions = GenerateMissions(missionData);
        ArchiveMissions(missions, resetDateTime, out _, out _);
    }

    static bool ArchiveMissions(IEnumerable<GameMission> missions, DateTime resetTime, out string archiveName, out string archivePath)
    {
        archiveName = null;
        archivePath = null;

        var archiveDirPath = AppConfig.Get("mission_archive", "target_folder", "user://mission_archive/");
        if (string.IsNullOrWhiteSpace(archiveDirPath))
            archiveDirPath = "user://mission_archive/";
        if (!DirAccess.DirExistsAbsolute(archiveDirPath))
        {
            try
            {
                DirAccess.MakeDirAbsolute(archiveDirPath);
            }
            catch
            {
                GD.Print($"Failed to create archive directory \"{archiveDirPath}\"");
                return false;
            }
        }

        if (!missions.Any(m => m.DisplayName is not null))
        {
            GD.Print($"Archiving abandoned due to missing resources");
            return false;
        }

        using var archiveDir = DirAccess.Open(archiveDirPath);
        var groupedMissions = missions.GroupBy(m => m.theaterInfo);
        List<ArchiveData.CompactTheater> compactedTheaters = [];
        foreach (var pair in groupedMissions)
        {
            compactedTheaters.Add(new()
            {
                theaterId = pair.Key.uniqueId,
                theaterName = pair.Key.displayName,
                missions = [..pair.Select(m =>
                    new ArchiveData.CompactMission()
                    {
                        missionName = m.DisplayName,
                        zoneName = m.zoneTheme is GameItemTemplate zt ? $"{zt.DisplayName} - {m.TheaterName}" : m.tile.zoneTheme,
                        powerLevel = m.PowerLevel,
                        fourPlayer = m.difficultyInfo?.DisplayName?.EndsWith("4 Players") ?? false,
                        rewards = m.missionData.missionRewards.NamedItems,
                        modifiers = m.alertData?.missionAlertModifiers.NamedItems,
                        alertRewards = m.alertData?.missionAlertRewards.NamedItems,

                        tileIndex = m.TileIdx,
                        missionGuid = m.missionData.missionGuid,
                        alertGuid = m.alertData?.missionAlertGuid,
                        missionGenerator = m.missionData.missionGenerator,
                        zoneTheme = m.tile.zoneTheme,
                        difficultyRow = m.missionData.DifficultyRow
                    }
                ).OrderBy(m=>m.powerLevel).ThenBy(m=>m.tileIndex)]
            });
        }

        ArchiveData archiveData = new()
        {
            expiresUTC = resetTime,
            expiresEST = resetTime.AddHours(-5),
            theaters = [..compactedTheaters]
        };

        try
        {
            TimeZoneInfo easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            //archiveData.beganEST = TimeZoneInfo.ConvertTimeFromUtc(archiveData.beganUTC, easternTimeZone);
            archiveData.expiresEST = TimeZoneInfo.ConvertTimeFromUtc(archiveData.expiresUTC, easternTimeZone);
        }
        catch
        {
            GD.PushWarning("EST Time Conversion failed, mission archive times will not account for EST Daylight Savings");
        }

        string archiveContent = JsonSerializer.Serialize(archiveData, archiveSerialisation);

        DateTime archiveDateTime = archiveData.expiresUTC.AddDays(-1);

        string archiveDateText = archiveDateTime.ToString(AppConfig.Get("mission_archive", "date_format", "yyyy-MM-dd"));
        int increment = 1;
        archivePath = $"{archiveDir.GetCurrentDir()}/{archiveDateText}.json";
        archiveName = $"{archiveDateText}";
        var prevArchivePath = archivePath;
        while (archiveDir.FileExists(archivePath))
        {
            increment++;
            prevArchivePath = archivePath;
            archivePath = $"{archiveDir.GetCurrentDir()}/{archiveDateText}_{increment}.json";
            archiveName = $"{archiveDateText}, Revision {increment}";
        }
        if (prevArchivePath != archivePath)
        {
            using var existingArchiveFile = FileAccess.Open(prevArchivePath, FileAccess.ModeFlags.Read);
            if (existingArchiveFile.GetAsText().Hash() == archiveContent.Hash())
                return false;
        }

        loadedArchives[archiveDateTime] = archiveData;

        bool writeSuccess = false;
        try
        {
            using var archiveFile = FileAccess.Open(archivePath, FileAccess.ModeFlags.Write);
            writeSuccess = archiveFile?.StoreString(archiveContent) ?? false;
        }
        catch { }
        if (!writeSuccess)
        {
            archivePath = null;
            GD.PushWarning($"Failed to archive missions as \"{archivePath}\"");
            return false;
        }
        GD.Print($"Missions archived as \"{archivePath}\"");
        return true;
    }

    public static bool TryGetOrLoadArchive(DateTime date, out ArchiveData archive) =>
        TryGetArchive(date, out archive) ||
        TryLoadArchive(date, out archive);
    public static bool TryGetArchive(DateTime date, out ArchiveData archive) =>
        loadedArchives.TryGetValue(date, out archive);
    public static bool TryLoadArchive(DateTime forDate, out ArchiveData data)
    {
        forDate = forDate.ToUniversalTime().Date;
        data = default;
        var archiveDirPath = AppConfig.Get("mission_archive", "target_folder", "user://mission_archive/");
        if (string.IsNullOrWhiteSpace(archiveDirPath))
            archiveDirPath = "user://mission_archive/";
        if (!DirAccess.DirExistsAbsolute(archiveDirPath))
            return false;
        using var archiveDir = DirAccess.Open(archiveDirPath);
        string archiveDate = forDate.ToString(AppConfig.Get("mission_archive", "date_format", "yyyy-MM-dd"));
        string archivePath = $"{archiveDir.GetCurrentDir()}/{archiveDate}.json";
        int increment = 1;
        while (archiveDir.FileExists($"{archiveDir.GetCurrentDir()}/{archiveDate}_{increment+1}.json"))
        {
            increment++;
            archivePath = $"{archiveDir.GetCurrentDir()}/{archiveDate}_{increment}.json";
        }
        using var archiveFile = FileAccess.Open(archivePath, FileAccess.ModeFlags.Read);
        if (FileAccess.GetOpenError() != Error.Ok)
            return false;
        data = JsonSerializer.Deserialize<ArchiveData>(archiveFile.GetAsText(), archiveSerialisation);
        loadedArchives.Add(forDate, data);
        return true;
    }

    public record class ArchiveData
    {
        public ArchiveVersion blakebeardArchiveFormat = new(1, 0);
        public DateTime expiresUTC;
        public DateTime expiresEST;
        public CompactTheater[] theaters;

        [JsonIgnore]
        GameMission[] missions;
        [JsonIgnore]
        public GameMission[] Missions => missions ??= [.. theaters.SelectMany(a => a.CreateMissions())];

        public record struct ArchiveVersion(int major, int minor);
        public record struct CompactTheater()
        {
            public string theaterId;
            public string theaterName;
            public CompactMission[] missions;

            public IEnumerable<GameMission> CreateMissions()
            {
                TheaterInfo theaterInfo = new()
                {
                    displayName = theaterName,
                    category = TheaterNameToCat(theaterName),
                };
                return missions.Select(m => new GameMission(m, theaterInfo));
            }
        }

        public record struct CompactMission()
        {
            public string missionName;
            public string zoneName;
            public int powerLevel;
            public bool fourPlayer;
            public ItemReward[] rewards;
            public ItemReward[] modifiers;
            public ItemReward[] alertRewards;

            public int tileIndex;
            public string missionGuid;
            public string alertGuid;
            public string missionGenerator;
            public string zoneTheme;
            public string difficultyRow;
        }
    }

    static string ParseItemPath(string itemPath) => itemPath[(itemPath.LastIndexOf('.') + 1)..itemPath.LastIndexOf('\'')];
    public record struct Requirements()
    {
        public int personalPowerRating;
        public int maxPersonalPowerRating;
        public string[] activeQuestDefinitions = [];
        public string questDefinition = "None";
        public string eventFlag = "";

        public bool MeetsRequirements(GameAccount account, bool ventures)
        {
            if (!account.isOwned)
                return true;
            var pl = ventures ? account.VentureFortStats.PowerLevel : account.FortStats.PowerLevel;
            if (pl < personalPowerRating)
                return false;
            if (maxPersonalPowerRating > 0 && pl > maxPersonalPowerRating)
                return false;
            if ((questDefinition ?? "None") != "None")
            {
                var questName = ParseItemPath(questDefinition);
                if(questName != "ReactiveQuest_DistressCalls")//i assume this is hardcoded similarly ingame too?
                {
                    var quest = account.GetProfile(FnProfileTypes.AccountItems).GetFirstTemplateItem($"Quest:{questName}");
                    if (quest?.QuestComplete != true)
                        return false;
                }
            }
            activeQuestDefinitions ??= [];
            foreach (var questDef in activeQuestDefinitions)
            {
                var quest = account.GetProfile(FnProfileTypes.AccountItems).GetFirstTemplateItem($"Quest:{ParseItemPath(questDef)}");
                if (quest?.QuestComplete != true)
                    return false;
            }
            return true;
        }
    }

    public record struct ItemReward()
    {
        public string name;
        public string itemType;
        public int quantity = 1;
        public GameItem ToItem() => new(GameItemTemplate.Get(itemType), quantity);
    }

    public record struct ItemCollection
    {
        public string tierGroupName;
        public ItemReward[] items;
        [JsonIgnore]
        public ItemReward[] NamedItems
        {
            get
            {
                items ??= [];
                for (int i = 0; i < items.Length; i++)
                {
                    items[i].name = GameItemTemplate.Get(items[i].itemType)?.DisplayName ?? $"<{items[i].itemType}>";
                }
                return items;
            }
        }
    }

    public record class TheaterInfo
    {
        //fill this in manually
        public string uniqueId;
        public string displayName;
        public string category;
        [JsonIgnore]
        public Region[] regions;
        [JsonIgnore]
        public Tile[] tiles;

        public Requirements requirements;
        [JsonInclude]
        public ModifierPair[] gameplayModifierList;
        public struct ModifierPair
        {
            public string eventFlagName;
            public string gameplayModifier;
        }
        public GameItemTemplate[] GetModifiers()
        {
            //for each pair, check if calender has event flag (or if event flag is empty) and get the modifier template 
            return [];
        }
        public override string ToString() => JsonSerializer.Serialize(this, Helpers.JsonOptions.Fields);
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

        public GameItemTemplate GetMissionGenerator() => GetMissionGenerator(missionGenerator);
        public static GameItemTemplate GetMissionGenerator(string generatorPath) => GameItemTemplate.Get($"MissionGen:{generatorPath[(generatorPath.LastIndexOf('.') + 1)..]}");
        public string DifficultyRow => missionDifficultyInfo.rowName;
        public DifficultyInfo GetDifficultyInfo() => GetDifficultyInfo(DifficultyRow);
        public static DifficultyInfo GetDifficultyInfo(string row)=> PegLegResourceManager.DifficultyInfo?[row]?.Deserialize<DifficultyInfo>(Helpers.JsonOptions.Fields);
        public override string ToString() => JsonSerializer.Serialize(this, Helpers.JsonOptions.Fields);
    }

    public record class DifficultyInfo
    {
        public int DifficultyLevel;
        public string DisplayName;
        public int MaximumRating;
        public int RecommendedRating;
        public int RequiredRating;
        public override string ToString() => JsonSerializer.Serialize(this, Helpers.JsonOptions.Fields);
    }

    public record class AlertData
    {
        public string missionAlertGuid;
        public int tileIndex;
        public DateTime availableUntil;
        public ItemCollection missionAlertRewards;
        public ItemCollection missionAlertModifiers;
        public override string ToString() => JsonSerializer.Serialize(this, Helpers.JsonOptions.Fields);
    }

    public record class Tile
    {
        public string tileType;
        public string zoneTheme;
        public GameItemTemplate GetZoneTheme() => GetZoneTheme(zoneTheme);
        public static GameItemTemplate GetZoneTheme(string zoneThemePath) => GameItemTemplate.Get($"ZoneTheme:{zoneThemePath[(zoneThemePath.IndexOf('.') + 1)..]}");
        public Requirements requirements;
        [JsonInclude]
        int xCoordinate;
        [JsonInclude]
        int yCoordinate;
        [JsonIgnore]
        public Vector2I Coordinates => new(xCoordinate, yCoordinate);
        public override string ToString() => JsonSerializer.Serialize(this, Helpers.JsonOptions.Fields);
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
        public override string ToString() => JsonSerializer.Serialize(this, Helpers.JsonOptions.Fields);
    }

    static string TheaterNameToCat(string name) => name switch
    {
        "Stonewood" => "s",
        "Plankerton" => "p",
        "Canny Valley" => "c",
        "Twine Peaks" => "t",
        _ => "v"
    };

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

            var nameNode = theater["displayName"];
            string theaterName = nameNode.GetValueKind()==JsonValueKind.String ? nameNode.ToString() : nameNode["en"]?.ToString();
            string theaterCat = TheaterNameToCat(theaterName);
            bool isVentures = theaterCat == "v";
            var theaterInfo = theater["runtimeInfo"].Deserialize<TheaterInfo>(Helpers.JsonOptions.Fields) with
            {
                displayName = theaterName,
                category = theaterCat,
            };

            //Missions
            var theaterMissions = allMissions
                .FirstOrDefault(t => t["theaterId"].ToString() == theaterID)
                ["availableMissions"]
                .Deserialize<MissionData[]>(Helpers.JsonOptions.Fields);

            //Mission Alerts (indexed by Tile Index, as that is the common factor between missions and mission alerts)
            var missionAlertDict = allMissionAlerts
                .FirstOrDefault(t => t["theaterId"].ToString() == theaterID)
                ["availableMissionAlerts"]
                .Deserialize<AlertData[]>(Helpers.JsonOptions.Fields)
                .Reverse()
                .DistinctBy(a => a.tileIndex)
                .ToDictionary(a => a.tileIndex);

            theaterInfo.uniqueId = theater["uniqueId"].ToString();
            theaterInfo.tiles = theater["tiles"].Deserialize<Tile[]>(Helpers.JsonOptions.Fields);
            theaterInfo.regions = theater["regions"].Deserialize<Region[]>(Helpers.JsonOptions.Fields);

            foreach (var missionData in theaterMissions)
            {
                if (missionData.missionGenerator.Contains("_TheOutpost_"))
                    continue;
                missionList.Add(new(
                    theaterInfo,
                    [.. theaterInfo.regions.Where(r => r.IncludesTile(missionData.tileIndex) == true)],
                    theaterInfo.tiles[missionData.tileIndex],
                    missionData,
                    missionAlertDict.TryGetValue(missionData.tileIndex, out var alertData) ? alertData : null
                ));
            }
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
        GenerateItems(
            missionData.missionRewards.items, 
            alertData?.missionAlertModifiers.items, 
            alertData?.missionAlertRewards.items
        );
        GenerateSearchTags();
    }

    GameMission(ArchiveData.CompactMission mission, TheaterInfo theaterInfo)
    {
        this.theaterInfo = theaterInfo;
        regions = [];
        //this.regions = regions;
        //this.tile = tile;
        //this.missionData = missionData;
        //this.alertData = alertData;
        difficultyInfo = MissionData.GetDifficultyInfo(mission.difficultyRow);
        missionGenerator = MissionData.GetMissionGenerator(mission.missionGenerator);
        zoneTheme = Tile.GetZoneTheme(mission.zoneTheme);

        if (missionGenerator is null || zoneTheme is null)
            return;
        GenerateItems(
            mission.rewards, 
            mission.modifiers, 
            mission.alertRewards
        );
        GenerateSearchTags();
    }


    void GenerateItems(ItemReward[] rewards, ItemReward[] modifiers, ItemReward[] alertRewards)
    {
        Dictionary<string, GameItem> rewardItemList = [];
        foreach (var itemData in rewards ?? [])
        {
            GameItem item = itemData.ToItem();
            item.GetSearchTags();
            var match = ZCPConverter().Match(item.template.Name.ToLower());
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

        List<GameItem> alertModifierList = [];
        foreach (var itemData in modifiers ?? [])
        {
            GameItem modifier = itemData.ToItem();
            modifier.SetSeenLocal();
            modifier.GetSearchTags();
            alertModifierList.Add(modifier);
        }
        alertModifiers = [.. alertModifierList];

        List<GameItem> alertRewardItemList = [];

        foreach (var itemData in alertRewards ?? [])
        {
            GameItem item = itemData.ToItem();
            item.GetSearchTags();
            alertRewardItemList.Add(item);
        }
        alertRewardItems = [.. alertRewardItemList];
    }

    void GenerateSearchTags()
    {
        searchTags = [];
        searchTags.Add(DisplayName);
        if (IsFourPlayer)
            searchTags.Add("Group");
        if (alertModifiers.Length > 0)
            searchTags.Add("Alert");
        if (TheaterCat == "v")
            searchTags.Add("Ventures");
        //this is super lazy, i dont want to figure out how to query the total of specific items procedurally
        if (
            rewardItems.Where(i =>
                i.sortingTemplate?.Name.StartsWith("Reagent_Alteration_Upgrade", StringComparison.InvariantCultureIgnoreCase) == true ||
                i.sortingTemplate?.Name.Equals("Reagent_Alteration_Generic", StringComparison.InvariantCultureIgnoreCase) == true ||
                i.sortingTemplate?.Name.StartsWith("Reagent_C", StringComparison.InvariantCultureIgnoreCase) == true ||
                i.sortingTemplate?.Name.Equals("PersonnelXP", StringComparison.InvariantCultureIgnoreCase) == true ||
                i.sortingTemplate?.Name.Equals("SchematicXP", StringComparison.InvariantCultureIgnoreCase) == true ||
                i.sortingTemplate?.Name.Equals("HeroXP", StringComparison.InvariantCultureIgnoreCase) == true
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
            tile?.requirements.MeetsRequirements(account, TheaterCat == "v")==true && 
            regions.All(r => r.requirements.MeetsRequirements(account, TheaterCat == "v"));
    }
    public bool IsAlertCompleteFor(GameAccount account)
    {
        if(alertData is null)
            return false;
        var statAttributes = account.GetProfile(FnProfileTypes.AccountItems).statAttributes;
        var claimArray = statAttributes?["mission_alert_redemption_record"]?["claimData"]?.AsArray();
        if (claimArray is null)
            return false;
        return claimArray.Any(c => c["missionAlertId"]?.ToString() == alertData.missionAlertGuid);
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

    public void UpdateRewardNotifications(bool force = false)
    {
        foreach (var item in allItems)
        {
            item.SetRewardNotification(null, force);
        }
    }

    [GeneratedRegex("zcp_.*t\\d{1,2}")]
    private static partial Regex ZCPConverter();
}
