using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Text;
using System.Text.Json;

public partial class NotificationDispatcher : Node
{
    const string notifTimerPath = "user://notifTimes.json";

    const string monthlyFreeLlamaOfferId = "8339003D26B24F70878EE280B70C340D";
    const string firstFreeLlamaOfferId = "B9B0CE758A5049F898773C1A47A69ED4";

    class RefreshTimeContainer
    {
        public DateTime lastDailyCheck { get; set; }
        public DateTime lastWeeklyCheck { get; set; }
        public DateTime lastHourlyCheck { get; set; }
    }

    RefreshTimeContainer refreshTimes;

    [Export]
    Texture2D freeLlamaIcon;
    [Export]
    AudioStream freeLlamaSound;
    [Export]
    Texture2D missionIcon;
    [Export]
    AudioStream missionSound;
    [Export]
    Texture2D shopIcon;
    [Export]
    AudioStream shopSound;

    public override void _Ready()
    {
        refreshTimes = new();
        if (FileAccess.FileExists(notifTimerPath))
        {
            //load times from file
            using var timerFile = FileAccess.Open(notifTimerPath, FileAccess.ModeFlags.Read);
            refreshTimes = JsonSerializer.Deserialize<RefreshTimeContainer>(timerFile.GetAsText());
        }
        RefreshTimerController.OnHourChanged += HourlyNotifs;
        HourlyNotifs();
        GameMission.CheckMissions().StartTask();
    }

    public override void _ExitTree()
    {
        RefreshTimerController.OnHourChanged -= HourlyNotifs;
    }

    public override void _Input(InputEvent @event)
    {
        if(@event is InputEventKey keyEvt && keyEvt.AltPressed && keyEvt.Keycode == Key.N && keyEvt.Pressed)
        {
            GetViewport().SetInputAsHandled();
            HourlyNotifs(true);
        }
        base._Input(@event);
    }


    void HourlyNotifs() => HourlyNotifs(false);
    bool isForced = false;
    CancellationTokenSource notifCTS;
    async void HourlyNotifs(bool force)
    {
        if (!AppConfig.Get("experimental", "notifications", false))
            return;
        var hour = DateTime.UtcNow.Date.AddHours(DateTime.UtcNow.Hour);
        isForced = force;
        if (refreshTimes.lastHourlyCheck == hour && !isForced)
            return;
        refreshTimes.lastHourlyCheck = hour;
        notifCTS = notifCTS.CancelAndRegenerate(out var ct);

        bool hasAuth = await GameAccount.activeAccount.Authenticate();
        if (!hasAuth || ct.IsCancellationRequested)
            return;

        Task<NotificationData[]>[] notifTasks =
        [
            CheckDailyLlamas(ct),
            ..DailyNotifs(ct)
        ];

        //save refresh times
        using var timerFile = FileAccess.Open(notifTimerPath, FileAccess.ModeFlags.Write);
        timerFile.StoreString(JsonSerializer.Serialize(refreshTimes));

        var notifs = (await Task.WhenAll(notifTasks)).SelectMany(n => n);
        if (ct.IsCancellationRequested)
            return;

        NotificationManager.Push(notifs);
    }

    Task<NotificationData[]>[] DailyNotifs(CancellationToken ct)
    {
        if (refreshTimes.lastDailyCheck == DateTime.UtcNow.Date && !isForced)
            return [];
        refreshTimes.lastDailyCheck = DateTime.UtcNow.Date;
        return [
            CheckMissions(ct),
            CheckCosmetics(ct),
            //check calender for quests
            ..WeeklyNotifs(ct)
        ];
    }

    Task<NotificationData[]>[] WeeklyNotifs(CancellationToken ct)
    {
        int utcDayOfWeek = (int)DateTime.UtcNow.DayOfWeek;
        int daysSinceThursday = (3 + utcDayOfWeek) % 7;
        var thisThursday = DateTime.UtcNow.Date.AddDays(daysSinceThursday);
        if (refreshTimes.lastWeeklyCheck >= thisThursday && !isForced)
            return [];
        refreshTimes.lastWeeklyCheck = DateTime.UtcNow.Date;
        return [
            //if week is new, send 160 reward as notif
            CheckBdayLlamas(ct),
        ];
    }

    NotificationData? freeLlamaNotif;
    NotificationData FreeLlamaNotif => freeLlamaNotif ??= new()
    {
        header = "Free Llamas",
        body = "These Llamas arent available for long, so claim them quick!",
        icon = freeLlamaIcon,
        sound = freeLlamaSound,
        urgent = true,
        firstAction = "View",
        //superAction = "Claim All",
        itemColor = Color.FromHtml("#bf00ff"),
        HandleAction = a =>
        {
            if (a == NotifAction.FirstAction)
                GD.Print("View Llama");
            return true;
        }
    };

    NotificationData? eventLlamaNotif;
    NotificationData EventLlamaNotif => eventLlamaNotif ??= new()
    {
        header = "Event Llamas",
        body = "These Llamas dont come by often, and contain rare weapons",
        icon = freeLlamaIcon,
        urgent = true,
        firstAction = "View",
        itemColor = Color.FromHtml("#bf00ff"),
    };
    static readonly FrozenSet<string> excludeFreeLlamaIds = (new string[] { 
        monthlyFreeLlamaOfferId,
        firstFreeLlamaOfferId,
    }).ToFrozenSet();
    static readonly FrozenSet<string> eventLlamaIds = (new string[] { 
        "", 
        "", 
        "" 
    }).ToFrozenSet();
    async Task<NotificationData[]> CheckDailyLlamas(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return [];

        var xrayStorefront = await GameStorefront.GetStorefront(FnStorefrontTypes.XRayLlamaCatalog, RefreshTimeType.Hourly);
        if (ct.IsCancellationRequested)
            return [];

        List<NotificationData> notifs = [];
        if (xrayStorefront.Offers.Any(o => !excludeFreeLlamaIds.Contains(o.OfferId) && o.WeeklyLimit==-1 && o.Price.quantity == 0))
        {
            GD.Print($"Free Llamas: {string.Join(", ", xrayStorefront
                .Offers
                .Where(o => 
                    !excludeFreeLlamaIds.Contains(o.OfferId) && 
                    o.Price.quantity == 0
                ).Select(o => o.OfferId)
            )}");
            //deliver 1hr daily notif
            notifs.Add(FreeLlamaNotif with
            {
                body = "These Llamas appear randomly, and are only available for one hour at a time, so claim them quick!",
                expires = RefreshTimerController.GetRefreshTime(RefreshTimeType.Hourly)
            });
        }
        if (xrayStorefront.Offers.Any(o => o.OfferId == monthlyFreeLlamaOfferId))
        {
            //deliver 24hr daily notif
            notifs.Add(FreeLlamaNotif with
            {
                body = "These Llamas are available for 24 hours, and return at the start of each month.",
                expires = RefreshTimerController.GetRefreshTime(RefreshTimeType.Daily)
            });
        }
        if (xrayStorefront.Offers.FirstOrDefault(o => eventLlamaIds.Contains(o.itemGrants[0].templateId)) is GameOffer evtOffer)
        {
            //deliver 24hr daily notif
            //todo: list amount of llamas, and the event item in the current one
            var contents = await evtOffer.GetXRayLlamaData(GameAccount.activeAccount);
            notifs.Add(EventLlamaNotif with
            {
                icon = evtOffer.itemGrants[0].GetTexture(),
                expires = RefreshTimerController.GetRefreshTime(RefreshTimeType.Daily)
            });
        }
        if (ct.IsCancellationRequested)
            return [];
        //todo: if any llamas contain item with reminder, show notification
        return [.. notifs];
    }

    NotificationData? bDayLlamaNotif;
    NotificationData BDayLlamaNotif => bDayLlamaNotif ??= new()
    {
        header = "Birthday Llama",
        body = "A free Birthday Llama is available until the next weekly reset",
        icon = freeLlamaIcon,
        urgent = true,
        firstAction = "View",
        itemColor = Color.FromHtml("#bf00ff"),
    };
    async Task<NotificationData[]> CheckBdayLlamas(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return [];

        var xrayStorefront = await GameStorefront.GetStorefront(FnStorefrontTypes.XRayLlamaCatalog, RefreshTimeType.Hourly);
        if (ct.IsCancellationRequested)
            return [];
        if (xrayStorefront.Offers.FirstOrDefault(o => o.WeeklyLimit > 0) is GameOffer bdayOffer)
        {
            //deliver bday llama notif
            //todo: if birthday llama contains item with reminder, show notification
            return [BDayLlamaNotif with
            {
                icon = bdayOffer.itemGrants[0].GetTexture(),
                expires = RefreshTimerController.GetRefreshTime(RefreshTimeType.Weekly)
            }];
        }
        return [];
    }

    NotificationData? missionNotif;
    NotificationData MissionNotif => missionNotif ??= new()
    {
        header = "Missions Updated",
        body = "[PH] Mission Items",
        icon = missionIcon,
        sound = missionSound,
        flipbookSlice = new(6, 5),
        flipbookLength = 29,
        animDuration = 1
    };

    static readonly FrozenSet<string> targetMissionRewardIds = (new string[] 
    { 
        "AccountResource:currency_mtxswap", 
        "Worker:workerbasic_sr_t01"
    }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    async Task<NotificationData[]> CheckMissions(CancellationToken ct)
    {
        await GameMission.CheckMissions();
        if (GameMission.currentMissions is not GameMission[] missions || ct.IsCancellationRequested)
            return [];

        Dictionary<string, int> totals = [];
        List<GameItemTemplate> mythicLeads = [];

        await Task.Run(() =>
        {
            foreach (var mission in missions)
            {
                foreach (var item in mission.alertRewardItems ?? [])
                {
                    var tid = item.templateId;
                    if (tid.Contains("Worker:manager") && tid.Contains("_sr_"))
                        mythicLeads.Add(item.template);
                    if (!targetMissionRewardIds.Contains(tid))
                        continue;
                    if (!totals.ContainsKey(tid))
                        totals[tid] = 0;
                    totals[tid] += item.quantity;
                }
            }
        }, ct);
        if (ct.IsCancellationRequested)
            return [];

        //todo: search for items marked as needing reminders
        List<string> totalStrings = [];
        totals.TryGetValue("AccountResource:currency_mtxswap", out var vbucks);
        if (vbucks > 0)
            totalStrings.Add($"V-Bucks: {vbucks}");
        totals.TryGetValue("Worker:workerbasic_sr_t01", out var legSurvivors);
        if (legSurvivors > 0)
            totalStrings.Add($"Legendary Survivors: {legSurvivors}");
        //ctd
        StringBuilder bodyContent = new(string.Join(",  ", totalStrings));
        if (mythicLeads.Count > 0)
            bodyContent.AppendLine($"{(bodyContent.Length>0?"\n":"")}Mythic Lead{(mythicLeads.Count == 1 ? "" : "s")}: {string.Join(", ", mythicLeads.Select(m => m.DisplayName))}");

        //show notif of total quantities
        return [MissionNotif with
        {
            body = bodyContent.ToString(),
            expires = RefreshTimerController.GetRefreshTime(RefreshTimeType.Daily)
        }];
    }


    NotificationData? _shopNotif;
    NotificationData shopNotif => _shopNotif ??= new()
    {
        header = "New cosmetics",
        body = "[PH] Cosmetic Name, Cosmetic Name, Cosmetic Name, and X more",
        icon = shopIcon,
        sound = shopSound,
        flipbookSlice = new(6, 5),
        flipbookLength = 29,
        animDuration = 1,
    };

    async Task<NotificationData[]> CheckCosmetics(CancellationToken ct)
    {
        await Helpers.WaitForFrame();
        return [];
    }

    public void TestMissionNotif()
    {
        NotificationManager.Push([shopNotif with
        {
            expires = RefreshTimerController.GetRefreshTime(RefreshTimeType.Daily)
        }]);
    }
}
