using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class BufferPublishSettings : Control
{
	[Export]
	string internalName;
	[ExportGroup("Nodes")]
	[Export]
	Button toggleEditor;
	[Export]
	OptionButton orgSelector;
	[Export]
	Button channelMenuButton;
	[Export]
	PopupMenu channelMenu;

	public override void _Ready()
	{
		InitialiseSettings(internalName);
		AppConfig.OnConfigChanged += OnConfigChanged;
		toggleEditor.Toggled += OnEnableToggled;
		orgSelector.ItemSelected += OnOrgSelected;
		channelMenu.IndexPressed += OnChannelPressed;
	}

	private void OnConfigChanged(string section, string key, JsonNode value)
	{
		if (initialised && section == "buffer_publish" && (key == "organizations" || "key" == key))
			UpdateOrgs();
	}

	bool initialised = false;
	public void InitialiseSettings(string prefix)
	{
		internalName = prefix;
		UpdateOrgs();
		initialised = true;
	}

	void UpdateOrgs()
	{
		if(!BufferKeySetting.TryGetBufferKey(out _))
		{
			orgSelector.Visible = false;
			channelMenuButton.Visible = false;
			toggleEditor.Disabled = true;
			toggleEditor.Text = "Buffer Key Missing";
			toggleEditor.ButtonPressed = false;
			return;
		}
		toggleEditor.Text = "Enabled";
		toggleEditor.ButtonPressed = AppConfig.Get("buffer_publish", internalName + "_enabled", false);
		toggleEditor.Disabled = false;
		orgSelector.Visible = true;
		orgSelector.Visible = true;
		channelMenuButton.Visible = true;
		var orgs = BufferKeySetting.Organizations;
		var currentOrg = AppConfig.Get<string>("buffer_publish", internalName + "_org", null);

		orgSelector.Clear();
		orgSelector.AddItem("Select Organization");
		orgSelector.AddSeparator();
		var targetIdx = 0;
		for (int i = 0; i < orgs.Length; i++)
		{
			orgSelector.AddItem(orgs[i].name);
			orgSelector.SetItemMetadata(i + 2, orgs[i].id);
			if (currentOrg == orgs[i].id)
				targetIdx = i + 2;
		}
		orgSelector.Selected = targetIdx;
		UpdateChannels();
	}

	void UpdateChannels()
	{
		var currentOrg = AppConfig.Get<string>("buffer_publish", internalName + "_org", null);
		if (orgSelector.Selected == 0 || !BufferKeySetting.TryGetOrganizationFromId(currentOrg, out var orgData))
		{
			channelMenuButton.Text = "Select Organisation First";
			channelMenuButton.Disabled = true;
			return;
		}
		channelMenuButton.Disabled = false;

		var currentChannels = AppConfig.Get<string[]>("buffer_publish", internalName + "_channels", []).ToHashSet();
		channelMenu.Clear();
		int matchCount = 0;
		for (int i = 0; i < orgData.channels.Length; i++)
		{
			var channel = orgData.channels[i];
			channelMenu.AddCheckItem($"{channel.service} ({channel.name})");
			channelMenu.SetItemMetadata(i, channel.id);
			if (currentChannels.Contains(channel.id))
			{
				channelMenu.SetItemChecked(i, true);
				matchCount++;
			}
		}
		channelMenuButton.Text = $"{matchCount}/{channelMenu.ItemCount} Selected Channels";
	}

	private void OnEnableToggled(bool toggledOn)
	{
		var currentlyEnabled = AppConfig.Get("buffer_publish", internalName + "_enabled", false);
		if (currentlyEnabled == toggledOn)
			return;
		AppConfig.Set("buffer_publish", internalName + "_enabled", toggledOn);
	}

	private void OnOrgSelected(long index)
	{
		var currentOrg = AppConfig.Get<string>("buffer_publish", internalName + "_org", null);
		var newOrg = orgSelector.GetItemMetadata((int)index).AsString();
		if (currentOrg == newOrg)
			return;
		AppConfig.Set("buffer_publish", internalName + "_org", newOrg);
		AppConfig.SetSerialised<string[]>("buffer_publish", internalName + "_channels", []);
		UpdateChannels();
	}

	private void OnChannelPressed(long index)
	{
		channelMenu.ToggleItemChecked((int)index);
		bool checkedState = channelMenu.IsItemChecked((int)index);
		string channelId = channelMenu.GetItemMetadata((int)index).AsString();

		var currentChannelList = AppConfig.Get<string[]>("buffer_publish", internalName + "_channels", []);
		if (currentChannelList.Contains(channelId) == checkedState)
			return;
		currentChannelList = checkedState ? [.. currentChannelList, channelId] : [.. currentChannelList.Except([channelId])];
		channelMenuButton.Text = $"{currentChannelList.Length}/{channelMenu.ItemCount} Selected Channels";
		AppConfig.SetSerialised("buffer_publish", internalName + "_channels", currentChannelList);
	}
}
