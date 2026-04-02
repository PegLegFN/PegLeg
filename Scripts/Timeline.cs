using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ShopItemTuple = (System.DateTime releaseDate, string templateId);

public static class Timeline
{
	static TLData timelineData;

	public static void LoadTimeline()
	{
		timelineData = PegLegResourceManager.LoadResourceObj<TLData>("timeline.json", options: Helpers.JsonOptions.Fields);
	}

	public static DateTime Anchor
	{
		get
		{
			if (timelineData is null)
				LoadTimeline();
			return timelineData?.anchor ?? DateTime.MinValue;
		}
	}

	public static Season[] Seasons
	{
		get
		{
			if (timelineData is null)
				LoadTimeline();
			return timelineData.seasons ?? [];
		}
	}

	public static int GetWeeksInYear()
	{
		if (timelineData is null)
			LoadTimeline();
		return timelineData?.seasons.Select(s => s.duration).Sum() ?? 0;
	}

	public static Season GetCurrentSeason() =>
		GetCurrentSeason(out _, out _);
	public static Season GetCurrentSeason(out DateTime seasonStartDate) =>
		GetCurrentSeason(out seasonStartDate, out _);
	public static Season GetCurrentSeason(out DateTime seasonStartDate, out int seasonIndex)
	{
		if (timelineData is null)
			LoadTimeline();
		if (timelineData is null)
		{
			seasonStartDate = DateTime.MinValue;
			seasonIndex = 0;
			return default;
		}

		var anchor = timelineData.anchor;
		var thisWeek = StartOfCurrentWeek;

		//increase anchor by a seasonal year until it is less than a seasonal year from now
		int weeksInYear = GetWeeksInYear();
		var compareDate = thisWeek.AddDays(-weeksInYear * 7);
		while (anchor <= compareDate)
		{
			anchor = anchor.AddDays(weeksInYear * 7);
		}

		//increase anchor until it passes the current season
		int currentSeasonIndex = 0;
		var currentSeason = timelineData.seasons[currentSeasonIndex];
		compareDate = anchor.AddDays(currentSeason.duration * 7);
		while (thisWeek >= compareDate)
		{
			anchor = anchor.AddDays(currentSeason.duration * 7);
			currentSeasonIndex += 1;
			currentSeason = timelineData.seasons[currentSeasonIndex];
			compareDate = anchor.AddDays(currentSeason.duration * 7);
		}

		seasonStartDate = anchor;
		seasonIndex = currentSeasonIndex;
		return currentSeason;
	}

	public static int GetCurrentSeasonWeek()
	{
		var _ = GetCurrentSeason(out DateTime seasonStartDate);
		return GetCurrentSeasonWeek(seasonStartDate);
	}

	public static int GetCurrentSeasonWeek(DateTime seasonStartDate) =>
		((int)(StartOfCurrentWeek - seasonStartDate).TotalDays) / 7;

	public static ShopItemTuple[] GetCurrentUpcomingItems()
	{
		var season = GetCurrentSeason(out var seasonStartDate);
		var week = GetCurrentSeasonWeek(seasonStartDate);
		return season.GetUpcomingItems(seasonStartDate, week);
	}

	public static DateTime StartOfCurrentWeek => EndOfCurrentWeek.AddDays(-7);
	public static DateTime EndOfCurrentWeek => RefreshTimerController.RightNow.WeeklyRefresh();

	public static DateTime BRWeeklyRefresh(this DateTime from) => from.WeeklyRefresh(DayOfWeek.Tuesday, 14);
	public static DateTime WeeklyRefresh(this DateTime from, DayOfWeek day = DayOfWeek.Thursday, int hour = 0, int minute = 0)
	{
		var today = from.ToUniversalTime().AddHours(-hour).AddMinutes(-minute).Date;
		int targetDay = (int)day;
		int utcDayOfWeek = (int)today.DayOfWeek;
		int daysUntilTarget = ((6 + targetDay) - utcDayOfWeek) % 7;
		return today.AddDays(daysUntilTarget + 1).AddHours(hour).AddMinutes(minute);
	}


	class TLData
	{
#pragma warning disable CS0649 //Field is never assigned to, and will always have its default value
		public DateTime anchor;
		public Season[] seasons;
#pragma warning restore CS0649 //Field is never assigned to, and will always have its default value
	}

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

		public ShopItemTuple[] GetUpcomingItems(DateTime seasonStartDate, int currentWeek)
		{
			List<ShopItemTuple> result = [];
			for (int i = currentWeek + 1; i < eventShop.Length; i++)
			{
				var date = seasonStartDate.AddDays(i * 7);
				if (eventShop[i].Length > 0)
					result.AddRange(eventShop[i].Select(item => (date, item)));
			}
			return [.. result];
		}
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
