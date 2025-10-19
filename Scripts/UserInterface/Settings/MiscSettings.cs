using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class MiscSettings : Control
{
	[Export(PropertyHint.File, "*.tscn")]
	string loginSceneFilePath;
    [Export]
    Control accountImportButton;
    [Export]
    SpinBox collectionBookSpinbox;

    TempDataTable data;
    public override void _Ready()
    {
        accountImportButton.Visible = DirAccess.DirExistsAbsolute("user://../accounts");
        using (var file = PegLegResourceManager.LoadResourceFile("CollectionBookXpLevelData.json"))
        {
            var text = file.GetAsText();
            //GD.Print(text);
            data = JsonSerializer.Deserialize<TempDataTable[]>(text)[0];
        }
    }

    void SetInterfaceScale(float newInterfaceScale)
    {
        GetWindow().ContentScaleFactor = newInterfaceScale;
    }
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && !keyEvent.IsEcho() && keyEvent.Pressed && keyEvent.Keycode == Key.M && keyEvent.CtrlPressed)
            VolumeController.ToggleBusMuted("Master");
    }

    void ImportAccounts()
    {
        foreach (var file in DirAccess.GetFilesAt("user://../accounts"))
        {
            DirAccess.CopyAbsolute($"user://../accounts/{file}", $"user://accounts/{file}");
        }
        GameAccount.UpdateAccountCache();
        accountImportButton.Visible = false;
    }

    void OpenAppData()
    {
        OS.ShellOpen(ProjectSettings.GlobalizePath("user://"));
    }

    void OpenInstallationFolder()
    {
        OS.ShellOpen(Helpers.GlobalisePath("res://"));
    }

    void CloseApp()
    {
        GetTree().Quit();
    }

    void ReturnToLogin()
    {
        GetTree().ChangeSceneToFile(loginSceneFilePath);
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
            (int lv, int targetXP) = GetRequiredXP(profile);
            string content = $$"""
            {
                "requiredXp": {{targetXP}},
                "selectedRewardIndex": {{(int)collectionBookSpinbox.Value}}
            }
            """;
            GD.Print(content);
            notifs = await profile.PerformOperation("ClaimCollectionBookRewards", content);
            collectionBookSpinbox.Value = -1;
            if (notifs is not null)
                GD.Print("Claimed Lv: " + lv);
            notifs = null;
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
