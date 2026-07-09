using Godot;

public partial class DiscordWebhookSettings : Control
{
	[Export]
	string internalName;
	[Export]
	string displayName;
	[ExportGroup("Nodes")]
	[Export]
	ConfigToggleHook toggleEditor;
	[Export]
	ConfigTextHook urlEditor;
	[Export]
	ConfigToggleHook syncToggleEditor;
	[Export]
	ConfigTextHook syncEditor;
	[Export]
	ConfigTextHook threadEditor;

	public override void _Ready()
	{
		if (!string.IsNullOrWhiteSpace(displayName))
			toggleEditor?.Set("text", displayName);
		InitialiseSettings(internalName);
	}

	public void InitialiseSettings(string prefix)
	{
		toggleEditor?.UpdateTargetSetting("webhooks", prefix + "_enabled");
		urlEditor?.UpdateTargetSetting("webhooks", prefix + "_url");
		syncToggleEditor?.UpdateTargetSetting("webhooks", prefix + "_useSync");
		syncEditor?.UpdateTargetSetting("webhooks", prefix + "_sync");
		threadEditor?.UpdateTargetSetting("webhooks", prefix + "_syncThread");
	}

	public async void TryCreateSyncMsg()
	{
		if (DiscordWebhookProxy.TryGetProxy(internalName, out var proxy))
			await proxy.CreateSyncMessage();
	}
}
