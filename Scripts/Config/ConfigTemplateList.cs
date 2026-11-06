using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class ConfigTemplateList : Button
{
	[Export]
	string textPrefix = "Selector";

	[Export]
	PopupMenu menu;

	[Export]
	string templatePrefix;

	[Export]
	string[] templateList;

	//multi-template filters
	[Export]
	Godot.Collections.Dictionary<string, string> customOptions;

	[Export]
	string section;

	[Export]
	string key;

	[Export]
	bool tryBind = true;

	[Export]
	bool accountMode = false;

	[Export]
	TooltipMode tooltipMode;

	[Export]
	Texture2D swapIcon;

	enum TooltipMode
	{
		None,
		Description,
		FirstObjective
	}

	GameItemTemplate[] allOptions;
	List<string> selectedTemplateIds;

	const int listOffset = 2;
	int listEndOffset = 0;
	public override void _Ready()
	{
		menu ??= GetChildOrNull<PopupMenu>(0);
		if(menu is null)
		{
			menu = new();
			AddChild(menu);
		}

		allOptions = [
			..string.IsNullOrWhiteSpace(templatePrefix) ? [] : GameItemTemplate.GetTemplates(t => t.TemplateId.StartsWith(templatePrefix)),
			..(templateList?.Length ?? 0) == 0 ? [] : templateList.Select(GameItemTemplate.Get).Where(t => t is not null)
		];
		allOptions = [.. allOptions.OrderBy(t => t.SortingName)];
		menu.Clear();

		menu.AddIconItem(swapIcon, "Invert Selection");
		menu.AddSeparator();

		for (int i = 0; i < allOptions.Length; i++)
		{
			menu.AddCheckItem(allOptions[i].ItemName, i + listOffset);
			menu.SetItemMetadata(i + listOffset, allOptions[i].TemplateId);
			menu.SetItemTooltip(i + listOffset, tooltipMode switch
			{
				TooltipMode.Description => allOptions[i].ItemDescription,
				TooltipMode.FirstObjective => allOptions[i]["Objectives"]?.AsArray().FirstOrDefault()["Description"].ToString(),
				_ => null,
			});
		}
		menu.IndexPressed += ListChanged;

		if (accountMode)
		{
			GameAccount.LocalDataChanged += UpdateAccountValue;
			GameAccount.ActiveAccountChanged += UpdateAccount;
		}
		else
			AppConfig.OnConfigChanged += UpdateConfigValue;
		LoadList();

		Pressed += ShowPopup;
		menu.CloseRequested += CleanupMenu;
	}

	private async void ShowPopup()
	{
		var combinedTransform = GetWindow().GetFinalTransform() * GetGlobalTransformWithCanvas();
		var startPos = (Vector2I)(combinedTransform * (Size * Vector2.Down)); // bottom left
		var endPos = (Vector2I)(combinedTransform * Size); // bottom right
		Rect2I box = new(startPos, endPos - startPos);
		menu.PopupOnParent(box);
		//await Helpers.WaitForFrame();
		//menu.Size = Vector2I.Zero;
		//menu.PopupOnParent(box);
	}

	private async void CleanupMenu()
	{
		await Helpers.WaitForFrame();
		menu.Size = Vector2I.Zero;
	}

	private void UpdateAccountValue(string key)
	{
		if (key != this.key)
			return;
		LoadList();
	}

	private void UpdateAccount()
	{
		LoadList();
	}

	private void UpdateConfigValue(string section, string key, JsonNode val)
	{
		if (section != this.section || key != this.key)
			return;
		LoadList();
	}

	bool isChanging = false;

	void LoadList()
	{
		if (isChanging)
			return;
		isChanging = true;

		selectedTemplateIds = accountMode ? 
			[.. GameAccount.ActiveAccount.GetLocalData(key)?.Deserialize<string[]>() ?? []] :
			[.. AppConfig.Get<string[]>(section, key, [])];

		var allTIDs = allOptions.Select(t => t.TemplateId).ToHashSet();
		selectedTemplateIds = [.. selectedTemplateIds.Where(allTIDs.Contains)];

		ApplyList(true);
		isChanging = false;
	}

	private void ApplyList(bool applyCheckmarks)
	{
		Text = $"{textPrefix} ({selectedTemplateIds.Count})";

		if (accountMode)
			GameAccount.ActiveAccount.SetLocalData(key, JsonSerializer.SerializeToNode(selectedTemplateIds));
		else
			AppConfig.SetSerialised(section, key, selectedTemplateIds);

		if (applyCheckmarks)
		{
			for (int i = 0; i < allOptions.Length; i++)
			{
				string template = menu.GetItemMetadata(i).AsString();
				menu.SetItemChecked(i, selectedTemplateIds.Contains(template));
			}
		}
	}

	public void InvertList()
	{
		isChanging = true;
		selectedTemplateIds = [.. allOptions.Select(t => t.TemplateId).Except(selectedTemplateIds)];

		ApplyList(true);
		isChanging = false;
	}

	private void ListChanged(long longIndex)
	{
		if (longIndex < listOffset)
		{
			if (longIndex == 0)
				InvertList();
			return;
		}
		if (longIndex >= menu.ItemCount-listEndOffset)
		{
			return;
		}
		if (isChanging)
			return;
		isChanging = true;

		int index = (int)longIndex;
		bool isChecked = menu.IsItemChecked(index);
		string template = menu.GetItemMetadata(index).AsString();

		if (!isChecked)
		{
			selectedTemplateIds.Add(template);
			menu.SetItemChecked(index, true);
		}
		else
		{
			selectedTemplateIds.Remove(template);
			menu.SetItemChecked(index, false);
		}

		ApplyList(false);
		isChanging = false;
	}
}
