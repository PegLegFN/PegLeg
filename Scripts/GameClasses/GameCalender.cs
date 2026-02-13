using Godot;
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

public static class GameCalender
{
    const string calenderCachePath = "user://calender.json";
    static bool hasCalender = false;
    static CalenderState currentState;

    public static event Action OnCalenderUpdate;

    struct CalenderState
    {
        public DateTime cacheExpire { get; set; }
        [JsonPropertyName("states")]
        public EventState[] eventStates { get; set; }
        public int latestCalenderIndex { get; set; }

        Dictionary<string, EventTimeRange> knownEvents;

        [JsonIgnore]
        public Dictionary<string, EventTimeRange> KnownEvents => knownEvents ??=
            eventStates.Length > 1 ?
            eventStates.Last().ActiveEvents.Union(eventStates.First().ActiveEvents).DistinctBy(kvp=>kvp.Key).ToDictionary() :
            eventStates.First().ActiveEvents;

        public int GetCurrentIndex(bool update = false)
        {
            //assumes that states are in chronological order
            int cur = 0;
            for (int i = eventStates.Length - 1; i >= 1; i--)
            {
                if (eventStates[i].validFrom < DateTime.UtcNow)
                {
                    cur = i;
                    break;
                }
            }
            if(update)
                latestCalenderIndex = cur;
            return cur;
        }

        public EventState GetLatestState() => eventStates?[latestCalenderIndex] ?? default;
        public EventState GetCurrentState(bool update = false) => eventStates?[GetCurrentIndex(update)] ?? default;
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
    public static async Task Check(GameAccount asAccount = null) => await (asAccount ?? GameAccount.ActiveAccount).CheckCalender();
    public static async Task CheckCalender(this GameAccount account)
    {
        bool? notify = null;
        bool hadCalender = hasCalender;

        if (!hasCalender && FileAccess.FileExists(calenderCachePath))
        {
            try
            {
                using FileAccess calenderFile = FileAccess.Open(calenderCachePath, FileAccess.ModeFlags.Read);
                currentState = JsonSerializer.Deserialize<CalenderState>(calenderFile.GetAsText());
                notify = true;
                hasCalender = true;
            }
            catch (JsonException) { }
        }

        //GD.Print($"Calender: {{expiresUTC: {currentCalender.cacheExpire}, currentlyUTC: {DateTime.UtcNow}, fetch: {currentCalender.cacheExpire < DateTime.UtcNow}}}");
        await calenderCheck.WaitAsync();
        try
        {
            if (currentState.cacheExpire < DateTime.UtcNow && account.isOwned)
            {
                var shouldNotify = await FetchCalender(account);
                notify ??= shouldNotify;
            }
        }
        finally
        {
            calenderCheck.Release();
        }

        notify ??= currentState.latestCalenderIndex != currentState.GetCurrentIndex(true);

        if (notify == true)
        {
            OnCalenderUpdate?.Invoke();
        }
    }

    static async Task<bool?> FetchCalender(GameAccount account)
    {
        var calenderResponse = await FnWebAddresses.FortGame
            .MakeRequest("/fortnite/api/calendar/v1/timeline")
            .SetAccount(account)
            .Send();

        if (await calenderResponse.CheckForError())
            return null;

        var newData = await calenderResponse.ReadJson<JsonObject>();

        if (!newData.ContainsKey("channels"))
        {
            GD.Print("no channels: " + newData);
            return null;
        }

        var clientEvents = newData["channels"]["client-events"];
        var newCalender = new CalenderState()
        {
            cacheExpire = clientEvents["cacheExpire"].AsTime(),
            eventStates = [..clientEvents["states"].AsArray().Select(s => new EventState
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

        var oldState = currentState.GetLatestState();
        currentState = newCalender;
        var newState = currentState.GetCurrentState(true);
        return !oldState.Equals(newState) ? true : null;
    }

    public static bool HasCalender => hasCalender;

    public static bool EventFlagActive(string flag) => 
        hasCalender && flag is not null && currentState.GetLatestState().ActiveEvents.ContainsKey(flag);

    public static bool TryGetFlagRange(string flag, out DateTime activeSince, out DateTime activeUntil)
    {
        activeSince = default;
        activeUntil = default;
        if (!hasCalender || flag is null)
            return false;
        currentState.KnownEvents.TryGetValue(flag, out var timeRange);
        activeSince = timeRange.activeSince;
        activeUntil = timeRange.activeUntil;
        return true;
    }

    public static int BRSeasonNumber => currentState.GetCurrentState().state.seasonNumber;

    static EventTimeRange? BRSeasonRange => currentState.GetCurrentState().ActiveEvents.TryGetValue($"EventFlag.Event_S{BRSeasonNumber}_Discovery", out var range) ? range : null;
    public static DateTime? BRSeasonStart => BRSeasonRange?.activeSince;
    public static int? BRSeasonWeek => BRSeasonRange is EventTimeRange range ? Mathf.FloorToInt((DateTime.UtcNow - range.activeSince).TotalDays) / 7 : null;
    public static DateTime? BRSeasonEnd => BRSeasonRange?.activeUntil;
}
