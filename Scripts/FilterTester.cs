using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

public partial class FilterTester : Node
{
	[Export]
	ItemSourceMode itemSource;
	[Export]
	string itemSubSource;
	[Export]
	Node filterSource;
	[Export]
	Label result;

	enum ItemSourceMode
	{
		Profile,
		Missions,
		Storefront,
	}

	async Task<GameItem[]> ItemsFromProfile()
	{
		var profile = await GameAccount.ActiveAccount.GetProfile(itemSubSource).Query();
		return [.. profile?.GetItems() ?? []];
	}

	async Task<GameItem[]> ItemsFromMissions()
	{
		await GameMission.CheckMissions();
		return [.. GameMission.MissionList?.SelectMany(m => m.allItems) ?? []];
	}

	async Task<GameItem[]> ItemsFromStorefront()
	{
		var sf = await GameStorefront.GetStorefront(itemSubSource, RefreshTimeType.Hourly);
		return [.. sf.Offers.SelectMany(o => o.itemGrants) ?? []];
	}

	public async void Test()
	{
		result.Visible = false;

		var items = itemSource switch
		{
			ItemSourceMode.Profile => await ItemsFromProfile(),
			ItemSourceMode.Missions => await ItemsFromMissions(),
			ItemSourceMode.Storefront => await ItemsFromStorefront(),
			_ => []
		};

		var filterText = filterSource.Get("text").AsString();
		if (string.IsNullOrWhiteSpace(filterText))
			return;
		var filterRules = PLSearch.GenerateSearchInstructions(filterText);
		var filteredItems = items.Where(i => PLSearch.EvaluateInstructions(filterRules, i.RawData)).Select(i => i.template?.ItemName ?? $"<{i.templateId}>").ToArray();

		result.Visible = true;
		result.Text = result.TooltipText = filteredItems.JoinString();
	}
}
