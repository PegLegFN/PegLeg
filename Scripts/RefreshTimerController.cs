using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class RefreshTimerController : Node
{
    public static event Action OnSecondChanged;
    public static event Action OnHourChanged;
    public static event Action OnDayChanged;
    [Export]
    int daysToAddDebug = 0;
    [Export]
    int monthsToAddDebug = 0;
    [Export]
    int yearsToAddDebug = 0;

    static RefreshTimerController instance;

    public override void _Ready()
    {
        instance = this;

        Timer perSecondTimer = new()
        {
            OneShot = false,
            WaitTime = 1,
            Autostart = true,
            IgnoreTimeScale = true
        };
        AddChild(perSecondTimer);
        perSecondTimer.Timeout += UpdateTimers;

        Timer fifteenMinTimer = new()
        {
            OneShot = false,
            IgnoreTimeScale = true
        };
        AddChild(fifteenMinTimer);
        fifteenMinTimer.Timeout += UpdateCalender;
        var secondsSinceHour = (DateTime.Now.Minute*60) + DateTime.Now.Second;
        fifteenMinTimer.Start((secondsSinceHour % (60 * 15))+(60*7));
        fifteenMinTimer.WaitTime = 60 * 15;

        lastTime = DateTime.UtcNow;
        AppConfig.OnConfigChanged += OnConfigChanged;
        offset = AppConfig.Get("advanced", "offset_refresh", true) ? 0 : 2;
    }

    float offset = 2;
    private void OnConfigChanged(string section, string key, JsonValue value)
    {
        if (section == "advanced" && key == "offset_refresh")
            offset = value.TryGetValue(out bool val) && val ? 0 : 2;
    }

    private async void UpdateCalender()
    {
        var account = GameAccount.activeAccount;
        if (account is null || !await account.Authenticate())
            return;
        await account.CheckCalender();
    }

    DateTime lastTime;
    private void UpdateTimers()
    {
        OnSecondChanged?.Invoke();
        var currentTime = DateTime.UtcNow.AddSeconds(-offset);
        if (currentTime.Hour != lastTime.Hour)
            OnHourChanged?.Invoke();
        if (currentTime.Day != lastTime.Day)
            OnDayChanged?.Invoke();
        lastTime = currentTime;
    }

    static readonly DateTime referenceStartDate = new(2024, 1, 25);
    static readonly int[] seasonLengths =
    [
        10,
        11,
        11,
        11,
        9
    ];
    static readonly int weeksInSeasonalYear = seasonLengths.Sum();

    public static DateTime RightNow =>
        instance is null ?
            DateTime.UtcNow :
            DateTime.UtcNow
                .AddDays(instance.daysToAddDebug)
                .AddMonths(instance.monthsToAddDebug)
                .AddYears(instance.yearsToAddDebug)
                .AddSeconds(-instance.offset);

    public enum Season
    {
        FlannelFalls,
        ScurvyShoals,
        BlastedBadlands,
        Hexsylvania,
        FrozenFjords
    }

    public static DateTime GetRefreshTime(RefreshTimeType refreshType)
    {
        var rightNow = RightNow;
        var today = rightNow.Date;
        switch (refreshType)
        {
            case RefreshTimeType.Hourly:
                return today.AddHours(rightNow.Hour + 1);
            case RefreshTimeType.Daily:
                return today.AddDays(1);
            case RefreshTimeType.Weekly:
                int utcDayOfWeek = (int)rightNow.DayOfWeek;
                int daysUntilThursday = (10 - utcDayOfWeek) % 7;
                return today.AddDays(daysUntilThursday + 1);
            case RefreshTimeType.BRWeekly:
                int utcDayOfWeekBR = (int)rightNow.AddHours(14).DayOfWeek;
                int daysUntilTuesday = ((10 - utcDayOfWeekBR)) % 7;
                return today.AddDays(daysUntilTuesday + 1).AddHours(14);
        }
        int dayCount = (today - referenceStartDate).Days;
        dayCount %= (weeksInSeasonalYear * 7);
        int daysRemaining = 0;
        int startDayOffset = 0;
        for (int i = 0; i < seasonLengths.Length; i++)
        {
            int reducedDayCount = dayCount - startDayOffset;
            if (reducedDayCount < seasonLengths[i]*7)
            {
                daysRemaining = (seasonLengths[i] * 7) - reducedDayCount;
                break;
            }
            startDayOffset += seasonLengths[i] * 7;
        }
        var result = today.AddDays(daysRemaining);
        return result;
    }

    public static DateTime GetLastRefreshTime(RefreshTimeType refreshType)
    {
        var rightNow = RightNow;
        var today = rightNow.Date;
        switch (refreshType)
        {
            case RefreshTimeType.Hourly:
                return today.AddHours(rightNow.Hour);
            case RefreshTimeType.Daily:
                return today;
            case RefreshTimeType.Weekly:
                int utcDayOfWeek = (int)rightNow.DayOfWeek;
                int daysUntilThursday = ((10 - utcDayOfWeek)) % 7;
                return today.AddDays(daysUntilThursday - 6);
            case RefreshTimeType.BRWeekly:
                int utcDayOfWeekBR = (int)rightNow.AddHours(14).DayOfWeek;
                int daysUntilTuesday = ((10 - utcDayOfWeekBR)) % 7;
                return today.AddDays(daysUntilTuesday - 6).AddHours(14);
        }
        int dayCount = (today - referenceStartDate).Days;
        dayCount %= (weeksInSeasonalYear * 7);
        int startDayOffset = 0;
        for (int i = 0; i < seasonLengths.Length; i++)
        {
            int reducedDayCount = dayCount - startDayOffset;
            if (reducedDayCount < seasonLengths[i] * 7)
            {
                break;
            }
            startDayOffset += seasonLengths[i] * 7;
        }
        var result = today.AddDays(startDayOffset);
        return result;
    }
}
public enum RefreshTimeType
{
    Hourly,
    Daily,
    Weekly,
    BRWeekly,
    Event
}
