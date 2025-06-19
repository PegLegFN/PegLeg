using Godot;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class EpicFixesInterface : Node
{
	TempDataTable data;
    [Export]
    SpinBox spinbox;
    public override async void _Ready()
    {
		using (var file = PegLegResourceManager.LoadResourceFile("CollectionBookXpLevelData.json"))
		{
			var text = file.GetAsText();
			//GD.Print(text);
            data = JsonSerializer.Deserialize<TempDataTable[]>(text)[0];
        }
        var profile = await GameAccount.activeAccount.GetProfile(FnProfileTypes.AccountItems).Query();
        (var level, var xp) = GetRequiredXP(profile);
        GD.Print($"Next XP ({level}+1): {xp.Notate()}");
    }

	(int, int) GetRequiredXP(GameProfile campaignProfile)
    {
        var level = campaignProfile.statAttributes?["collection_book"]?["maxBookXpLevelAchieved"]?.GetValue<int>() ?? 0;
        return (level, data.Rows.TryGetValue((level + 1).ToString(), out var row) ? row.TotalXpToGetToThisLevel : 0);
    }

    public async void ClaimCollectionRewards()
	{
        using var loadToken = LoadingOverlay.CreateToken();
        var profile = GameAccount.activeAccount.GetProfile(FnProfileTypes.AccountItems);
        JsonArray notifs = [];
        while (notifs is not null)
        {
            (var lv, var targetXP) = GetRequiredXP(profile);
            string content = $$"""
            {
                "requiredXp": {{targetXP}},
                "selectedRewardIndex": {{(int)spinbox.Value}}
            }
            """;
            GD.Print(content);
            notifs = await profile.PerformOperation("ClaimCollectionBookRewards", content);
            spinbox.Value = -1;
            if (notifs is not null)
                GD.Print("Claimed Lv: " + lv);
        }
	}

    record TempDataTable
	{
		public Dictionary<string, XPRow> Rows { get; init; }
	}

    record struct XPRow
	{
        public int TotalXpToGetToThisLevel { get; init; }
    }
}
