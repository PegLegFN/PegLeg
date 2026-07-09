using Godot;
using System;

public partial class PublishSettings : Node
{
	[Export]
	string internalName;
	[Export]
	string displayName;
	[ExportGroup("Nodes")]
	[Export]
	ConfigToggleHook toggleEditor;
	[Export]
	DiscordWebhookSettings discordSettings;
	[Export]
	BufferPublishSettings bufferSettings;

	public override void _Ready()
	{
		toggleEditor?.Set("text", displayName);
		toggleEditor?.UpdateTargetSetting("publishing", internalName + "_enabled");
		discordSettings?.InitialiseSettings(internalName);
		bufferSettings?.InitialiseSettings(internalName);
	}
}
