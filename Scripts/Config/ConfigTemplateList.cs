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
	string section;

	[Export]
	string key;

	[Export]
	bool tryBind = true;

	[Export]
	bool accountMode = false;

	[Export]
	TooltipMode tooltipMode;


	enum TooltipMode
	{
		None,
		Description,
		FirstObjective
	}

	GameItemTemplate[] allOptions;
	List<string> selectedTemplateIds;

	public override void _Ready()
	{
		menu ??= GetChildOrNull<PopupMenu>(0);
		if(menu is null)
		{
			menu = new();
			AddChild(menu);
		}

		allOptions = [.. GameItemTemplate.GetTemplates(t => t.TemplateId.StartsWith(templatePrefix)).OrderBy(t => t.SortingName)];
		menu.Clear();
		for (int i = 0; i < allOptions.Length; i++)
		{
			menu.AddCheckItem(allOptions[i].ItemName, i);
			menu.SetItemMetadata(i, allOptions[i].TemplateId);
			menu.SetItemTooltip(i, tooltipMode switch
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
		menu.PopupOnParent(new(startPos, endPos - startPos));
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

	string[] GetCurrentValues()
	{
		if (accountMode)
			return GameAccount.ActiveAccount.GetLocalData(key)?.Deserialize<string[]>() ?? [];
		return AppConfig.Get<string[]>(section, key, []);
	}

	bool isChanging = false;

	void LoadList()
	{
		if (isChanging)
			return;
		isChanging = true;

		if (accountMode)
			selectedTemplateIds = [.. GameAccount.ActiveAccount.GetLocalData(key)?.Deserialize<string[]>() ?? []];
		else
			selectedTemplateIds = [.. AppConfig.Get<string[]>(section, key, [])];

		Text = $"{textPrefix} ({selectedTemplateIds.Count})";

		for (int i = 0; i < allOptions.Length; i++)
		{
			string template = menu.GetItemMetadata(i).AsString();
			menu.SetItemChecked(i, selectedTemplateIds.Contains(template));
		}
		isChanging = false;
	}

	private void ListChanged(long longIndex)
	{
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

		Text = $"{textPrefix} ({selectedTemplateIds.Count})";

		if (accountMode)
			GameAccount.ActiveAccount.SetLocalData(key, JsonSerializer.SerializeToNode(selectedTemplateIds));
		else
			AppConfig.SetSerialised(section, key, selectedTemplateIds);
		isChanging = false;
	}
}
