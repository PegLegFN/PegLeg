using Godot;
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Net.Http;
using System.Security.Principal;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

public static class CalenderRequests
{
    const string calenderCachePath = "user://calender.json";
    static bool hasCalender = false;
    static Calender currentCalender;

    public static event Action OnCalenderUpdate;

    struct Calender
    {
        public DateTime cacheExpire { get; set; }
        public EventState[] states { get; set; }
        public int latestCalenderIndex { get; set; }

        Dictionary<string, EventTimeRange> knownEvents;

        [JsonIgnore]
        public Dictionary<string, EventTimeRange> KnownEvents => knownEvents ??=
            states.Length > 1 ?
            states.Last().ActiveEvents.Union(states.First().ActiveEvents).DistinctBy(kvp=>kvp.Key).ToDictionary() :
            states.First().ActiveEvents;

        public int GetCurrentIndex(bool update = false)
        {
            //assumes that states are in chronological order
            int cur = 0;
            for (int i = states.Length - 1; i >= 1; i--)
            {
                if (states[i].validFrom < DateTime.UtcNow)
                {
                    cur = i;
                    break;
                }
            }
            if(update)
                latestCalenderIndex = cur;
            return cur;
        }

        public EventState GetLatestState() => states?[latestCalenderIndex] ?? default;
        public EventState GetCurrentState(bool update = false) => states?[GetCurrentIndex(update)] ?? default;
    }

    struct EventState
    {
        public DateTime validFrom { get; set; }
        public Dictionary<string, EventTimeRange> activeEvents { get; set; }
        public EventStateData state { get; set; }

        [JsonIgnore]
        public Dictionary<string, EventTimeRange> ActiveEvents => activeEvents ??= [];

        public bool Equals(EventState other)
        {
            if (activeEvents is null || other.activeEvents is null)
                return activeEvents is null== other.activeEvents is null;

            if (activeEvents?.Keys != other.activeEvents?.Keys)
                return false;

            if (activeEvents.Any(kvp => !other.activeEvents[kvp.Key].Equals(kvp.Value)))
                return false;

            return true;
        }
    }

    struct EventStateData
    {
        public int seasonNumber { get; set; }
        public DateTime seasonBegin { get; set; }
        public DateTime seasonEnd { get; set; }
    }

    struct EventTimeRange
    {
        public DateTime activeSince { get; set; }
        public DateTime activeUntil { get; set; }
        [JsonIgnore]
        public readonly TimeSpan Duration => activeUntil - activeSince;

        public readonly bool Equals(EventTimeRange other) =>
            activeSince == other.activeSince && 
            activeUntil == other.activeUntil;
    }

    static SemaphoreSlim calenderCheck = new(1);
    public static async Task CheckCalender() => await GameAccount.activeAccount.CheckCalender();
    public static async Task CheckCalender(this GameAccount account)
    {
        bool? notify = null;
        bool hadCalender = hasCalender;

        if (!hasCalender && FileAccess.FileExists(calenderCachePath))
        {
            try
            {
                using FileAccess calenderFile = FileAccess.Open(calenderCachePath, FileAccess.ModeFlags.Read);
                currentCalender = JsonSerializer.Deserialize<Calender>(calenderFile.GetAsText());
                notify = true;
                hasCalender = true;
            }
            catch (JsonException) { }
        }

        GD.Print($"cal: {currentCalender.cacheExpire}   now: {DateTime.UtcNow}   request:{currentCalender.cacheExpire < DateTime.UtcNow}");
        await calenderCheck.WaitAsync();
        try
        {
            if (currentCalender.cacheExpire < DateTime.UtcNow)
            {
                GD.Print("downloading latest calender");
                var shouldNotify = await RequestCalender(account);
                notify ??= shouldNotify;
            }
        }
        finally
        {
            calenderCheck.Release();
        }

        notify ??= currentCalender.latestCalenderIndex != currentCalender.GetCurrentIndex(true);

        if (notify == true)
        {
            if (hadCalender)
                GD.Print("Calender Update!");
            OnCalenderUpdate?.Invoke();
        }
    }

    static async Task<bool?> RequestCalender(GameAccount account)
    {
        var calResponse = await Helpers.MakeRequest(
            HttpMethod.Get,
            FnWebAddresses.game,
            "/fortnite/api/calendar/v1/timeline",
            "",
            account.AuthHeader,
            ""
        );
        var newData = calResponse?.AsObject();

        if (!newData.ContainsKey("channels"))
        {
            GD.Print("no channels: " + newData);
            return null;
        }

        var clientEvents = newData["channels"]["client-events"];
        var newCalender = new Calender()
        {
            cacheExpire = clientEvents["cacheExpire"].AsTime(),
            states = [..clientEvents["states"].AsArray().Select(s => new EventState
            {
                validFrom = s["validFrom"].AsTime(),
                activeEvents = s["activeEvents"].AsArray().ToDictionary(
                    e => e["eventType"].ToString(),
                    e => new EventTimeRange
                    {
                        activeSince = e["activeSince"].AsTime(),
                        activeUntil = e["activeUntil"].AsTime()
                    }
                ),
                state = s["state"].Deserialize<EventStateData>()
            })]
        };

        if (JsonSerializer.Serialize(newCalender) is not string calenderFileData)
            return false;
        using FileAccess calenderFile = FileAccess.Open(calenderCachePath, FileAccess.ModeFlags.Write);
        calenderFile.StoreString(calenderFileData);

        var oldState = currentCalender.GetLatestState();
        currentCalender = newCalender;
        var newState = currentCalender.GetCurrentState(true);
        return !oldState.Equals(newState) ? true : null;
    }

    public static bool HasCalender => hasCalender;

    public static bool EventFlagActive(string flag) => 
        hasCalender && flag is not null && currentCalender.GetLatestState().ActiveEvents.ContainsKey(flag);

    public static DateTime EventStart(string flag) =>
        flag is not null && currentCalender.KnownEvents.TryGetValue(flag, out var time) ? time.activeSince : default; //todo: estimate times of missing events

    public static DateTime EventEnd(string flag) =>
        flag is not null && currentCalender.KnownEvents.TryGetValue(flag, out var time) ? time.activeUntil : default; //todo: estimate times of missing events

    public static int BRSeasonNumber => currentCalender.GetCurrentState().state.seasonNumber;

    static EventTimeRange? BRSeasonRange => currentCalender.GetCurrentState().ActiveEvents.TryGetValue($"EventFlag.Event_S{BRSeasonNumber}_StoryQuests", out var range) ? range : null;
    public static DateTime? BRSeasonStart => BRSeasonRange?.activeSince;
    public static int? BRSeasonWeek => BRSeasonRange is EventTimeRange range ? Mathf.FloorToInt((DateTime.UtcNow - range.activeSince).TotalDays) / 7 : null;
    public static DateTime? BRSeasonEnd => BRSeasonRange?.activeUntil;
}
