using Godot;
using System;
using XmppDotNet.Xmpp.XHtmlIM;

public partial class CoreSettings : Control
{
    [Export]
    string bootScenePath = "res://Scenes/boot_scene.tscn";
    public void SetLiteMode(bool newValue)
    {
        //using var _ = LoadingOverlay.CreateToken();
        AppConfig.Set("core", "litemode", newValue);
        //await Helpers.WaitForFrames(10);
        GetTree().ChangeSceneToFile(bootScenePath);
    }
}
