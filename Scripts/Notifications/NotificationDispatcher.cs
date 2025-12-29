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

    class NotifTimeContainer
    {
        public DateTime lastDailyCheck { get; set; }
        public DateTime lastWeeklyCheck { get; set; }
        public bool lastWeeklySucceeded { get; set; }
        public DateTime lastHourlyCheck { get; set; }
    }

    NotifTimeContainer notifTimes;

    [Export]
    Texture2D llamaIcon;
    [Export]
    AudioStream llamaSound;
    [Export]
    AudioStream superLlamaSound;
    [Export]
    Texture2D missionIcon;
    [Export]
    AudioStream missionSound;
    [Export]
    Texture2D shopIcon;
    [Export]
    AudioStream shopSound;
    [Export]
    FrozenStringSetProxy eventLlamaIds = new();

    public override void _Ready()
    {
        notifTimes = new();
        if (FileAccess.FileExists(notifTimerPath))
        {
            //load times from file
            try
            {
                using var timerFile = FileAccess.Open(notifTimerPath, FileAccess.ModeFlags.Read);
                notifTimes = JsonSerializer.Deserialize<NotifTimeContainer>(timerFile.GetAsText());
            }
            catch { }
        }
        RefreshTimerController.OnHourChanged += HourlyNotifs;
        HourlyNotifs();
    }

    public override void _ExitTree()
    {
        RefreshTimerController.OnHourChanged -= HourlyNotifs;
    }

    public override void _ShortcutInput(InputEvent @event)
    {
        if(@event is InputEventKey keyEvt && !keyEvt.ShiftPressed && !keyEvt.CtrlPressed && keyEvt.AltPressed && keyEvt.Keycode == Key.N && keyEvt.Pressed)
        {
            GetViewport().SetInputAsHandled();
            HourlyNotifs(true);
        }
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
        if (notifTimes.lastHourlyCheck == hour && !isForced)
            return;
        notifTimes.lastHourlyCheck = hour;
        notifCTS = notifCTS.CancelAndRegenerate(out var ct);

        bool hasAuth = await GameAccount.activeAccount.Authenticate();
        if (!hasAuth || ct.IsCancellationRequested)
            return;

        Task<NotificationData[]>[] notifTasks =
        [
            CheckFreeLlamas(ct),
            ..DailyNotifs(ct)
        ];
        //save refresh times
        notifTimes.lastWeeklySucceeded = false;
        using (var timerFile = FileAccess.Open(notifTimerPath, FileAccess.ModeFlags.Write))
        {
            timerFile.StoreString(JsonSerializer.Serialize(notifTimes));
        }

        var notifs = (await Task.WhenAll(notifTasks)).SelectMany(n => n);
        if (ct.IsCancellationRequested)
            return;

        //save weekly success
        notifTimes.lastWeeklySucceeded = notifs.Any(n => (n.expires.Date - DateTime.UtcNow.Date).TotalDays > 1);
        using (var timerFile = FileAccess.Open(notifTimerPath, FileAccess.ModeFlags.Write))
        {
            timerFile.StoreString(JsonSerializer.Serialize(notifTimes));
        }

        NotificationManager.Push(notifs);
    }

    Task<NotificationData[]>[] DailyNotifs(CancellationToken ct)
    {
        if (notifTimes.lastDailyCheck == DateTime.UtcNow.Date && !isForced)
            return [];
        notifTimes.lastDailyCheck = DateTime.UtcNow.Date;
        return [
            CheckMissions(ct),
            CheckCosmetics(ct),
            CheckDailyLlamas(ct),
            //check calender for quests
            ..WeeklyNotifs(ct)
        ];
    }

    Task<NotificationData[]>[] WeeklyNotifs(CancellationToken ct)
    {
        int utcDayOfWeek = (int)DateTime.UtcNow.DayOfWeek;
        int daysSinceThursday = (3 + utcDayOfWeek) % 7;
        var thisThursday = DateTime.UtcNow.Date.AddDays(daysSinceThursday);
        if (notifTimes.lastWeeklyCheck >= thisThursday && !isForced)
            return [];
        notifTimes.lastWeeklyCheck = DateTime.UtcNow.Date;

        return [
            //if week is new, send 160 reward as notif
            CheckWeeklyLlamas(ct),
            //check weekly/event shop
        ];
    }

    NotificationData MakeReminderNotif(GameItem item)
    {
        return default;
    }

    NotificationData? llamaNotif;
    NotificationData LlamaNotif => llamaNotif ??= new()
    {
        header = "Llama",
        body = "Llama Notification",
        icon = llamaIcon,
        sound = llamaSound,
        urgent = true,
        firstAction = "View",
        itemColor = Color.FromHtml("#bf00ff"),
    };

    NotificationData? freeLlamaNotif;
    NotificationData FreeLlamaNotif => freeLlamaNotif ??= LlamaNotif with
    {
        header = "Free Llamas",
        body = "Random Free Llamas are only available for 1hr at at time",
        //superAction = "Claim All",
    };

    NotificationData? eventLlamaNotif;
    NotificationData EventLlamaNotif => eventLlamaNotif ??= LlamaNotif with
    {
        header = "Event Llamas",
        body = "These Llamas dont come by often, and contain rare weapons and heroes",
    };


    static readonly FrozenSet<string> excludeFreeLlamaIds = (new string[] { 
        monthlyFreeLlamaOfferId,
        firstFreeLlamaOfferId,
    }).ToFrozenSet();

    Func<NotifAction, bool> CreateLlamaHandler(GameOffer offer) => a =>
    {
        if (a == NotifAction.FirstAction)
        {
            GD.Print("View Llama");
            LlamaInterface.SelectLlamaTab(offer);
        }
        if (a == NotifAction.SuperAction)
            GD.Print("Claim All Free Llamas");
        return true;
    };

    async Task<NotificationData[]> CheckFreeLlamas(CancellationToken ct)
    {
        var xrayStorefront = await GameStorefront.GetStorefront(FnStorefrontTypes.XRayLlamaCatalog, RefreshTimeType.Hourly);
        if (ct.IsCancellationRequested)
            return [];

        //todo: evaluate llama contents for every account with notifications enabled

        if (xrayStorefront.Offers.FirstOrDefault(o => !excludeFreeLlamaIds.Contains(o.OfferId) && o.WeeklyLimit == -1 && o.Price?.quantity == 0) is GameOffer offer)
        {
            GD.Print($"Free Llamas: {string.Join(", ", xrayStorefront
                .Offers
                .Where(o => 
                    !excludeFreeLlamaIds.Contains(o.OfferId) && 
                    o.Price.quantity == 0
                ).Select(o => o.OfferId)
            )}");
            //deliver 1hr daily notif
            var contents = (await offer.GetXRayLlamaData(GameAccount.activeAccount))?.GetPrerollItems() ?? [];
            foreach (var item in contents)
            {
                item.SetRewardNotification();
            }
            return [FreeLlamaNotif with
            {
                expires = RefreshTimerController.GetRefreshTime(RefreshTimeType.Hourly),
                HandleAction = CreateLlamaHandler(offer),
                items = [.. contents.Select(i => new NotificationItemData() { item = i })]
            }];
        }
        return [];
    }

    async Task<NotificationData[]> CheckDailyLlamas(CancellationToken ct)
    {
        var xrayStorefront = await GameStorefront.GetStorefront(FnStorefrontTypes.XRayLlamaCatalog, RefreshTimeType.Hourly);
        if (ct.IsCancellationRequested)
            return [];

        //todo: evaluate llama contents for every account with notifications enabled

        List<NotificationData> notifs = [];

        if (xrayStorefront.Offers.FirstOrDefault(o => o.OfferId == monthlyFreeLlamaOfferId) is GameOffer freeOffer)
        {
            //deliver 24hr daily notif
            var contents = await freeOffer.GetXRayLlamaData(GameAccount.activeAccount);
            notifs.Add(FreeLlamaNotif with
            {
                body = "These Llamas are available for 24 hours, and return at the start of each month.",
                expires = RefreshTimerController.GetRefreshTime(RefreshTimeType.Daily),
                HandleAction = CreateLlamaHandler(freeOffer),
                items = [.. contents.GetPrerollItems().Select(i => new NotificationItemData() { item = i })]
            });
        }
        if (xrayStorefront.Offers.FirstOrDefault(o => eventLlamaIds.Contains(o.itemGrants[0].templateId)) is GameOffer evtOffer)
        {
            //deliver event llama notif
            //todo: list amount of llamas, and the event item in the current one
            var contents = await evtOffer.GetXRayLlamaData(GameAccount.activeAccount);
            if (ct.IsCancellationRequested)
                return [];
            notifs.Add(EventLlamaNotif with
            {
                icon = evtOffer.itemGrants[0].GetTexture(),
                expires = RefreshTimerController.GetRefreshTime(RefreshTimeType.Daily),
                HandleAction = CreateLlamaHandler(evtOffer),
                items = [.. contents.GetPrerollItems().Select(i => new NotificationItemData() { item = i })]
            });
        }
        return [.. notifs];
    }

    NotificationData? weeklyLlamaNotif;
    NotificationData WeeklyLlamaNotif => weeklyLlamaNotif ??= LlamaNotif with
    {
        sound = superLlamaSound,
        itemColor = Color.FromHtml("#bf00ff"),
    };

    async Task<NotificationData[]> CheckWeeklyLlamas(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return [];

        var xrayStorefront = await GameStorefront.GetStorefront(FnStorefrontTypes.XRayLlamaCatalog, RefreshTimeType.Hourly);
        if (ct.IsCancellationRequested)
            return [];
        if (xrayStorefront.Offers.FirstOrDefault(o => o.WeeklyLimit > 0) is GameOffer weeklyOffer)
        {
            //deliver bday llama notif
            //todo: if birthday llama contains item with reminder, show notification
            var contents = await weeklyOffer.GetXRayLlamaData(GameAccount.activeAccount);
            return [WeeklyLlamaNotif with
            {
                header = "{Weekly Llama}",
                body = "A free {Weekly Llama} is available until the next weekly reset",
                icon = weeklyOffer.itemGrants[0].GetTexture(),
                expires = RefreshTimerController.GetRefreshTime(RefreshTimeType.Weekly),
                items = [.. contents.GetPrerollItems().Select(i => new NotificationItemData() { item = i })]
            }];
        }
        return [];
    }

    NotificationData? missionNotif;
    NotificationData MissionNotif => missionNotif ??= new()
    {
        header = "Missions Updated",
        icon = missionIcon,
        sound = missionSound,
        flipbookSlice = new(6, 5),
        flipbookLength = 29,
        animDuration = 1,
        itemPrefix = "Main:",
        secondaryItemPrefix = "Ventures:",
    };

    static readonly FrozenSet<string> targetMissionRewardIds = FrozenSet.ToFrozenSet(
    [
        "AccountResource:currency_mtxswap", 
        "Worker:workerbasic_sr_t01"
    ], StringComparer.OrdinalIgnoreCase);

    async Task<NotificationData[]> CheckMissions(CancellationToken ct)
    {
        await GameMission.CheckMissions();
        if (GameMission.currentMissions is not GameMission[] missions || ct.IsCancellationRequested)
            return [];

        /* old text-based system
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
                    if (item.template.VBucksOrXRayTickets)
                        tid = "AccountResource:currency_mtxswap";
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
            totalStrings.Add($"XRay Tickets (& V-Bucks for Founders): {vbucks}");
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
        */

        List<NotificationItemData> mainItems = [];
        List<NotificationItemData> ventItems = [];

        await Task.Run(() =>
        {
            foreach (var mission in missions)
            {
                var list = mission.TheaterCat == "v" ? ventItems : mainItems;
                foreach (var item in mission.alertRewardItems ?? [])
                {
                    if (MissionRewardsController.ItemIsNotable(item))
                    {
                        list.Add(new()
                        {
                            item = item,
                            powerLabel = $"{mission.PowerLevel}",
                            powerTooltip = $"{mission.TheaterName}, Power Level {mission.PowerLevel}"
                        });
                    }
                }
            }
        }, ct);

        return [MissionNotif with {
            items = [..mainItems],
            secondaryItems = [..ventItems],
            expires = GameMission.missionReset
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
