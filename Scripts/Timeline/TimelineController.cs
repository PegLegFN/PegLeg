using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public partial class TimelineController : Node
{
    TimelineData timelineData;

    int questlineLanes = 0;
    int eventLanes = 0;
    Markers markers = new();
    [Export]
    TimelineMinimap minimap;
    [Export]
    TimelineStripPlacer stripPlacer;
    [Export]
    bool debug;

    public override void _Ready()
	{
        timelineData = PegLegResourceManager.LoadResourceObj<TimelineData>("timeline.json", options: Helpers.JsonOptions.Fields);
        GenerateTimelineMarkers();
        //RefreshTimerController.OnDayChanged += GenerateTimelineMarkers;
    }

    public override void _ExitTree()
    {
        //RefreshTimerController.OnDayChanged -= GenerateTimelineMarkers;
    }

    DateTime lastStartDate;
    void GenerateTimelineMarkers()
    {
        //TODO: if markers have been generated already, reuse existing markers instead of creating new ones

        //var sampleDate = timelineData.anchor;
        var sampleDate = new DateTime(2024, 1, 25, 0, 0, 0, DateTimeKind.Utc);
        //start timeline at previous weekly reset
        var newStartDate = RefreshTimerController.RightNow.WeeklyRefresh(DayOfWeek.Thursday).AddDays(-7);

        if (lastStartDate == newStartDate)
            return;

        markers.startDate = newStartDate;

        //increase sample date by a seasonal year until it is less than a seasonal year fom the start date
        int weeksInYear = timelineData.seasons.Select(s => s.duration).Sum();
        var compareDate = markers.startDate.AddDays(-weeksInYear * 7);
        while (sampleDate <= compareDate)
        {
            sampleDate = sampleDate.AddDays(weeksInYear * 7);
        }

        //increase sample date until it reaches the start of the current season
        int currentSeasonIndex = 0;
        var currentSeason = timelineData.seasons[currentSeasonIndex];
        compareDate = sampleDate.AddDays(currentSeason.duration * 7);
        while (markers.startDate > compareDate)
        {
            sampleDate = sampleDate.AddDays(currentSeason.duration * 7);
            currentSeasonIndex += 1;
            currentSeason = timelineData.seasons[currentSeasonIndex];
            compareDate = sampleDate.AddDays(currentSeason.duration * 7);
        }
        var seasonStartDate = sampleDate;

        //get current season week, and set sample date to start date
        int currentWeekIndex = ((int)(markers.startDate - sampleDate).TotalDays) / 7;
        sampleDate = markers.startDate;

        var limitDate = sampleDate.AddDays(weeksInYear * 7);

        //if (lastStartDate.AddDays(weeksInYear * 7) > sampleDate && lastStartDate < sampleDate)
        //{
        //    //remove existing markers timed before sampleDate
        //    //remove markers timed after the start of the season of lastStartDate
        //    //increment sampledate up to the season of lastStartDate
        //}
        //if (limitDate < lastStartDate && lastStartDate > sampleDate)
        //{
        //    //remove existing markers timed after the end of the sample year
        //    //remove existing markers timed before the first season after lastStartDate
        //    //set limitdate to end of lastStartdate season
        //    limitDate = lastStartDate;
        //}

        //markers.seasons.Clear();
        //markers.shops.Clear();
        //markers.questlines.Clear();
        //markers.events.Clear();

        List<QuestlineMarker> seasonQuestlines = [];
        List<EventMarker> seasonEvents = [];

        List<QuestlineMarker> concurrentQuestlines = [];
        List<EventMarker> concurrentEvents = [];

        //markers.maxQuestlineLanes = 0;
        //markers.maxEventLanes = 0;
        markers.weekIndexes = new int[weeksInYear];

        for (int i = 0; i < weeksInYear; i++)
        {
            concurrentQuestlines.RemoveAll(q => q.toDate <= sampleDate);
            concurrentEvents.RemoveAll(e => e.toDate <= sampleDate);
            if (currentWeekIndex == 0 || i == 0)
            {
                //if currentWeekIndex or i==0, create a SeasonMarker and pregenerate QuestlineMarkers and EventMarkers
                markers.seasons.Add(new()
                {
                    fromDate = seasonStartDate,
                    toDate = seasonStartDate.AddDays(currentSeason.duration * 7),
                    displayName = currentSeason.displayName,
                    llamaType = currentSeason.llamaType,
                    commonModifier = currentSeason.commonModifier,
                    style = currentSeason.style,
                    color = Color.HtmlIsValid(currentSeason.color??"") ? Color.FromHtml(currentSeason.color) : Colors.White,
                    venturesModifiers = currentSeason.venturesModifiers,
                });

                seasonQuestlines.Clear();
                for (int j = 0; j < currentSeason.questlines.Length; j++)
                {
                    var qData = currentSeason.questlines[j];
                    DateTime start;
                    DateTime end;

                    start = seasonStartDate.AddDays((qData.startWeek ?? 0)*7);
                    end = seasonStartDate.AddDays((qData.endWeek ?? currentSeason.duration)*7);

                    qData.style ??= currentSeason.style;
                    qData.color ??= currentSeason.color;

                    string[] qGroups = null;
                    if (qData.questGroup.ValueKind == JsonValueKind.Array)
                    {
                        qGroups = qData.questGroup.Deserialize<string[]>();
                    }
                    else if (qData.questGroup.ValueKind == JsonValueKind.String)
                    {
                        qGroups = [qData.questGroup.Deserialize<string>()];
                    }

                    seasonQuestlines.Add(new()
                    {
                        priority = qData.priority,
                        eventFlag = qData.eventFlag,
                        questGroups = qGroups,
                        displayName = qData.displayName,
                        description = qData.description,
                        keyItems = qData.keyItems,
                        style = qData.style,
                        color = Color.HtmlIsValid(qData.color ?? "") ? Color.FromHtml(qData.color) : Colors.White,
                        fromDate = start,
                        toDate = end,
                    });
                }
                seasonQuestlines = 
                [.. seasonQuestlines
                    .OrderBy(q => q.fromDate)
                    .ThenBy(q => -q.Duration)//longer questlines take priority over short ones
                    .ThenBy(q => q.eventFlag)
                ];

                seasonEvents.Clear();
                for (int j = 0; j < currentSeason.events.Length; j++)
                {
                    var eData = currentSeason.events[j];
                    DateTime start;
                    DateTime end;

                    if (eData.duringWeek is int dWeek)
                    {
                        eData.startWeek = dWeek;
                        eData.endWeek = dWeek + 1;
                    }

                    if (eData.targetMonth is int tMonth && eData.targetDay is int tDay)
                    {
                        start = seasonStartDate;
                        while (start.Month != tMonth)
                            start = start.AddMonths(1);
                        while (start.Day != tDay)
                            start = start.AddDays(1);
                        end = start.AddDays(1);
                        if (eData.weekOfTarget)
                        {
                            start = start.WeeklyRefresh(DayOfWeek.Thursday).AddDays(-7);
                            end = start.AddDays(7);
                        }
                        else if (eData.restOfWeek)
                        {
                            end = start.WeeklyRefresh(DayOfWeek.Thursday);
                        }
                        else if(eData.weekdayOfTarget is int wDay)
                        {
                            start = start.WeeklyRefresh(DayOfWeek.Thursday).AddDays(-7+wDay);
                            end = start.AddDays(1);
                        }
                    }
                    else
                    {
                        start = seasonStartDate.AddDays((eData.startWeek ?? 0)*7);
                        end = seasonStartDate.AddDays((eData.endWeek ?? currentSeason.duration)*7);
                    }

                    eData.style ??= currentSeason.style;
                    eData.color ??= currentSeason.color;

                    string[] qGroups = null;
                    if (eData.questGroup.ValueKind==JsonValueKind.Array)
                    {
                        qGroups = eData.questGroup.Deserialize<string[]>();
                    }
                    else if (eData.questGroup.ValueKind==JsonValueKind.String)
                    {
                        qGroups = [eData.questGroup.Deserialize<string>()];
                    }

                    var col = Color.HtmlIsValid(eData.color ?? "") ? Color.FromHtml(eData.color) : Colors.White;
                    if (eData.daily)
                    {
                        for (DateTime d = start; d < end; d = d.AddDays(1))
                        {
                            seasonEvents.Add(new()
                            {
                                fromDate = d,
                                toDate = d.AddDays(1),
                                priority = eData.priority,
                                eventFlag = eData.eventFlag,
                                displayName = eData.displayName,
                                description = eData.description,
                                eventQuest = eData.eventQuest,
                                keyItems = eData.keyItems,
                                style = eData.style,
                                color = col,
                                free = eData.free,
                                questGroups = qGroups,
                            });
                        }
                    }
                    else
                    {
                        seasonEvents.Add(new()
                        {
                            fromDate = start,
                            toDate = end,
                            priority = eData.priority,
                            eventFlag = eData.eventFlag,
                            displayName = eData.displayName,
                            description = eData.description,
                            eventQuest = eData.eventQuest,
                            keyItems = eData.keyItems,
                            style = eData.style,
                            color = col,
                            free = eData.free,
                            questGroups = qGroups,
                        });
                    }
                }

                seasonEvents =
                [.. seasonEvents
                    .OrderBy(q => q.fromDate)
                    .ThenBy(q => -q.Duration)//longer events take priority over short ones
                    .ThenBy(q => -q.priority)
                    .ThenBy(q => q.displayName)
                ];
            }

            QuestlineMarker[] questlinesToAdd = null;
            EventMarker[] eventsToAdd = null;
            if (i == 0)
            {
                // handle events that began before the starting week that are still active
                questlinesToAdd = [.. seasonQuestlines.Where(q => q.fromDate < sampleDate.AddDays(7) && q.toDate > sampleDate)];
                eventsToAdd = [.. seasonEvents.Where(q => q.fromDate < sampleDate.AddDays(7) && q.toDate > sampleDate)];
            }
            else
            {
                // handle events that began this week
                questlinesToAdd = [.. seasonQuestlines.Where(q => q.fromDate >= sampleDate && q.fromDate < sampleDate.AddDays(7))];
                eventsToAdd = [.. seasonEvents.Where(q => q.fromDate >= sampleDate && q.fromDate < sampleDate.AddDays(7))];
            }

            int lane = 0;
            for (int j = 0; j < questlinesToAdd.Length; j++)
            {
                while (concurrentQuestlines.Any(q => q.lane == lane))
                    lane++;
                questlinesToAdd[j].lane = lane;
                markers.maxQuestlineLanes = Mathf.Max(markers.maxQuestlineLanes, lane + 1);
                concurrentQuestlines.Add(questlinesToAdd[j]);
                markers.questlines.Add(questlinesToAdd[j]);
                lane++;
            }

            int shortLane = -1;
            lane = 0;
            for (int j = 0; j < eventsToAdd.Length; j++)
            {
                int thisLane = 0;
                if (eventsToAdd[j].Duration < 1.01)
                {
                    //assume that one-day events can all fit on one lane
                    if (shortLane == -1)
                    {
                        while (concurrentEvents.Any(q => q.lane == lane))
                            lane++;
                        shortLane = lane;
                    }
                    thisLane = shortLane;
                }
                else
                {
                    while (concurrentEvents.Any(q => q.lane == lane))
                        lane++;
                    thisLane = lane;
                }
                eventsToAdd[j].lane = thisLane;
                markers.maxEventLanes = Mathf.Max(markers.maxEventLanes, thisLane + 1);
                concurrentEvents.Add(eventsToAdd[j]);
                markers.events.Add(eventsToAdd[j]);
            }

            var shopItems = currentSeason.eventShop[currentWeekIndex];
            if (shopItems.Length > 0)
                markers.shops.Add(new()
                {
                    fromDate = sampleDate,
                    toDate = sampleDate.AddDays(7),
                    isReset = currentWeekIndex == 0,
                    newItems = shopItems,
                });

            markers.weekIndexes[i] = currentWeekIndex;

            currentWeekIndex++;
            sampleDate = sampleDate.AddDays(7);
            if (currentWeekIndex >= currentSeason.duration)
            {
                currentWeekIndex = 0;
                currentSeasonIndex++;
                currentSeasonIndex %= timelineData.seasons.Length;
                currentSeason = timelineData.seasons[currentSeasonIndex];
                seasonStartDate = sampleDate;
            }
        }

        minimap.SpawnMarkers(markers);
        stripPlacer.SetMarkers(markers);
    }

    public class Markers
    {
        public List<SeasonMarker> seasons = [];
        public List<ShopMarker> shops = [];
        public List<QuestlineMarker> questlines = [];
        public List<EventMarker> events = [];
        public int maxQuestlineLanes = 0;
        public int maxEventLanes = 0;
        public int[] weekIndexes;
        public DateTime startDate;
    }

    public abstract class BaseMarker
    {
        public DateTime fromDate;
        public DateTime toDate;
        public int lane;
        public string style;
        public Color color = Colors.White;
        public int Duration => (int)(toDate - fromDate).TotalDays;
    }

    public class SeasonMarker : BaseMarker
    {
        public string displayName;
        public string llamaType = "CardPack:cardpack_bronze";
        GameItem llamaItem;
        public GameItem LlamaItem => llamaItem ??= GameItemTemplate.Get(llamaType).CreateInstance();
        public string commonModifier;
        GameItem commonModifierItem;
        public GameItem CommonModifierItem => commonModifierItem ??= GameItemTemplate.Get(commonModifier).CreateInstance();
        public string[] venturesModifiers;
        GameItem[] venturesModifierItems;
        public GameItem[] VenturesModifierItems => venturesModifierItems ??= [.. venturesModifiers.Select(m => GameItemTemplate.Get(m).CreateInstance())];
    }

    public class ShopMarker : BaseMarker
    {
        public bool isReset;
        public string[] newItems;
        GameItem[] shopItems;
        public GameItem[] ShopItems => shopItems ??= [.. newItems?.Select(m => GameItemTemplate.Get(m).CreateInstance()) ?? []];
    }

    public abstract class BaseEventMarker : BaseMarker
    {
        public int priority;
        public string eventFlag;
        public string[] keyItems;
        GameItem[] keyGameItems;
        public GameItem[] KeyGameItems => keyGameItems ??= [.. keyItems?.Select(m => GameItemTemplate.Get(m).CreateInstance()) ?? []];
        public string displayName;
        public string DisplayName => displayName ?? GeneratedDisplayName;
        public string description;
        protected abstract string GeneratedDisplayName { get; }
        public string[] questGroups;
        public virtual bool Free => false;
    }

    public class QuestlineMarker : BaseEventMarker
    {
        protected override string GeneratedDisplayName =>
            PegLegResourceManager
            .EventQuestLines.FirstOrDefault
            (
                n => n.Value["EventTag"]?
                    .ToString()
                    .Equals(eventFlag, StringComparison.OrdinalIgnoreCase) == true
            ).Key;
    }

    public class EventMarker : BaseEventMarker
    {
        public string eventQuest;
        GameItem eventQuestItem;
        bool hasCheckedQuestItem = false;
        public GameItem EventQuestItem
        {
            get
            {
                if (hasCheckedQuestItem)
                    return eventQuestItem;
                hasCheckedQuestItem = true;
                return eventQuestItem ??= GameItemTemplate.Get(eventQuest)?.CreateInstance();
            }
        }
        protected override string GeneratedDisplayName => EventQuestItem?.template?.DisplayName ?? eventFlag;
        public bool free;
        public override bool Free => free;
    }

    public class TimelineData
	{
        public DateTime anchor;
        public Season[] seasons;

        public struct Season
        {
            public string displayName;
            public int duration;
            public string llamaType;
            public string commonModifier;
            public string[] venturesModifiers;
            public string[][] eventShop;
            public string style;
            public string color;
            public Event[] questlines;
            public Event[] events;
        }

        public struct Event
        {
            public string eventFlag;
            public string eventQuest;
            public string displayName;
            public string description;
            public string[] keyItems;
            public string style;
            public string color;
            public int priority;
            public int? startWeek;
            public int? endWeek;
            public int? duringWeek;
            public int? targetMonth;
            public int? targetDay;
            public bool weekOfTarget;
            public int? weekdayOfTarget;
            public bool restOfWeek;
            public bool daily;
            public bool free;
            public JsonElement questGroup;
        }
    }
}
