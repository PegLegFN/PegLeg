using Godot;
using System.Collections.Generic;

public partial class StatChangeEntry : Control
{
    [Export]
    Label statName;
    [Export]
    ProgressBar baseProgress;
    [Export]
    ProgressBar goodProgress;
    [Export]
    ProgressBar badProgress;
    [Export]
    Label changeAmount;
    [Export]
    Label changePercent;
    [Export]
    FrozenStringSetProxy normalisedStats;
    [Export]
    FrozenStringToStringProxy statNameLookup;
    [Export]
    Control rawStatIndicator;

    public void SetChange(KeyValuePair<string, StatChange> changeKVP)
    {
        bool hasMappedName = statNameLookup.FDict.TryGetValue(changeKVP.Key, out var mappedName);
        statName.Text = hasMappedName ? mappedName : changeKVP.Key;
        rawStatIndicator.Visible = !hasMappedName;
        var change = changeKVP.Value;
        bool isBuff = change.to > change.from;

        float progressMax = 1;
        //if stat is always within range 0 and 1, keep max at 1
        if (normalisedStats?.FSet.Contains(changeKVP.Key) != true)
        {
            float progressScale = 2;
            if (isBuff && change.from > 0)
            {
                while (change.to > change.from * progressScale)
                {
                    progressScale++;
                }
            }
            bool useFrom = (isBuff && change.from > 0) || change.to == 0;
            progressMax = (useFrom ? change.from : change.to) * progressScale;
        }

        if (isBuff)
        {
            baseProgress.Value = change.from / progressMax;
            goodProgress.Value = change.to / progressMax;
            badProgress.Value = 0;
        }
        else
        {
            baseProgress.Value = change.to / progressMax;
            goodProgress.Value = 0;
            badProgress.Value = change.from / progressMax;
        }

        if (change.from == 0 || change.to == 0)
        {
            changeAmount.Text = $"{change.from} => {change.to}";
            changePercent.Text = "";
            return;
        }
        changeAmount.Text = $"{change.from:0.###} => {change.to:0.###} ({(isBuff ? "+" : "-")}{Mathf.Abs(change.from - change.to):0.###})";
        changePercent.Text = $"{(isBuff ? "+" : "-")}{Mathf.RoundToInt(1000*Mathf.Abs(change.from - change.to) / change.from)*0.1:0.#}%";
    }
}
