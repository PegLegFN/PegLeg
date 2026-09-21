using Godot;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class TempCompendiumInterface : Control, IRecyclableElementProvider<GameItem>
{
	[Export]
	RecycleListContainer itemList;
	[Export]
	Control newItemListNode;
	[Export]
	LineEdit searchBox;
	[Export]
	Control loadingIcon;

	IListHandler newItemList;

	static readonly string[] includedSources =
	[
		"Hero",
		"Schematic"
	];
	public override void _Ready()
	{
		VisibilityChanged += GenerateCompendiumEntries;
		searchBox.TextChanged += FilterItems;
		itemList?.SetProvider(this);
		if (newItemListNode is IListHandler newListHandler)
		{
			newItemList = newListHandler;
			newItemList.LinkListProvider(filteredEntries);
		}
		//itemList. += InspectItem;
	}


	bool generated = false;
	List<GameItem> compendiumEntries = [];
	EntryList<GameItem> filteredEntries = [];
	public GameItem GetRecycleElement(int index) => filteredEntries?[index];

	public int GetRecycleElementCount() => filteredEntries?.Count ?? 0;

	void ProcessTemplate(GameItemTemplate template, ref ConcurrentDictionary<string, GameItemTemplate> uniqueTemplates)
	{
		if (template.Tier != 1)
			return;

		template.GenerateSearchTags();

		uniqueTemplates.AddOrUpdate(
			template.TemplateId, //use DisplayName for deduplication, or TemplateId to prevent deduplication
			template,
			(k, v) => v.RarityLevel < template.RarityLevel ? template : v
		);
	}

	async void GenerateCompendiumEntries()
	{
		if (generated)
			return;
		generated = true;
		searchBox.Editable = false;
		loadingIcon.Visible = true;
		itemList?.Visible = false;
		newItemListNode?.Visible = false;

		ConcurrentDictionary<string, GameItemTemplate> uniqueTemplates = new();
		List<GameItem> orderedItems = null;
		await Task.Run(() =>
		{
			//Parallel.ForEach(
			//includedSources.SelectMany(source => GameItemTemplate.GetTemplatesOfType(source)),
			//    t => ProcessTemplate(t, ref uniqueTemplates)
			//);
			foreach (var t in includedSources.SelectMany(source => GameItemTemplate.GetTemplatesOfType(source)))
			{
				ProcessTemplate(t, ref uniqueTemplates);
			}

			orderedItems = [.. uniqueTemplates.Values
				.OrderBy(item => item.Type == "Hero" ? 0 : 1)
				.ThenBy(item => -item.RarityLevel)
				.ThenBy(item => $"""
					{item.Category} 
					{item.SubType}
					{item.SortingName}
					""")
				.Select(template => {
					var item=template.CreateInstance();
					item.SetRewardNotification();
					return item;
				})
			];
		});
		compendiumEntries = orderedItems ?? [];
		//compendiumTemplates.ForEach(i => i.GenerateSearchTags());
		FilterItems("");
		searchBox.Editable = true;
		loadingIcon.Visible = false;
		itemList?.Visible = true;
		newItemListNode?.Visible = true;
	}

	void FilterItems(string _)
	{
		var instructions = PLSearch.GenerateSearchInstructions(searchBox.Text);
		filteredEntries.Clear();
		filteredEntries.AddRange(string.IsNullOrWhiteSpace(searchBox.Text) ? 
			compendiumEntries : 
			compendiumEntries.Where(item => PLSearch.EvaluateInstructions(instructions, item.RawData))
		);

		//GD.Print("filteredEntries: " + filteredEntries.Count);
		itemList?.UpdateList(true);
		newItemList?.UpdateList();
	}
}
