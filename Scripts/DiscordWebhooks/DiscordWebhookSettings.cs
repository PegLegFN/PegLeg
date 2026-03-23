using Godot;
using System;

public partial class DiscordWebhookSettings : Node
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
        toggleEditor.Set("Text", displayName);
        toggleEditor.UpdateTargetSetting("webhooks", internalName + "_enabled");
        urlEditor.UpdateTargetSetting("webhooks", internalName+"_url");
        syncToggleEditor.UpdateTargetSetting("webhooks", internalName + "_useSync");
        syncEditor.UpdateTargetSetting("webhooks", internalName + "_sync");
        threadEditor.UpdateTargetSetting("webhooks", internalName + "_syncThread");
    }

    public async void TryCreateSyncMsg()
    {
        if(DiscordWebhookProxy.TryGetProxy(internalName, out var proxy))
            await proxy.CreateSyncMessage();
    }
}
