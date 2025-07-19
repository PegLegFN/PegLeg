using Godot;
using System;

public partial class MiscSettings : Control
{
	[Export(PropertyHint.File, "*.tscn")]
	string loginSceneFilePath;
    [Export]
    Control accountImportButton;

    public override void _Ready()
    {
        accountImportButton.Visible = DirAccess.DirExistsAbsolute("user://../accounts");
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

    void CloseApp()
    {
        GetTree().Quit();
    }

    void ReturnToLogin()
    {
        GetTree().ChangeSceneToFile(loginSceneFilePath);
    }

    //async void ProcessMeta()
    //{
    //    LoadingOverlay.AddLoadingKey("meta");
    //    using var metaFile = FileAccess.Open("res://MetaRecipes.json", FileAccess.ModeFlags.Read);
    //    JsonObject rows = JsonNode.Parse(metaFile.GetAsText())[0]["Rows"].AsObject();
    //    List<int> knownHashes = new();
    //    JsonObject keyedUpgrades = new();
    //    JsonObject failedUpgrades = new();
    //    int total = rows.Count;
    //    int current = 0;
    //    foreach (var item in rows)
    //    {
    //        if (current % 20 == 0)
    //            await Helpers.WaitForFrame();
    //        current++;
    //        LoadingOverlay.SetProgress("meta", (float)current/total);
    //        if (!item.Key.StartsWith("Evolve."))
    //            continue;
    //        var target = item.Value["RecipeCosts"].AsArray();
    //        var hash = string.GetHashCode(target.ToString());
    //        var splitRecipeKey = item.Key.Split('.');
    //        bool isTrap = item.Key.Contains(".Floor.") || item.Key.Contains(".Wall.") || item.Key.Contains(".Ceiling.");
    //        var newKey = splitRecipeKey[1] switch
    //        {
    //            "HID" => $"Hero.{splitRecipeKey[^2]}.{splitRecipeKey[^1]}",
    //            "SID" when isTrap => $"Trap.{splitRecipeKey[^2]}.{splitRecipeKey[^1]}",
    //            "SID" => $"Weapon.{splitRecipeKey[^3]}.{splitRecipeKey[^1]}",
    //            "Worker" => $"Worker.{splitRecipeKey[^2]}.{splitRecipeKey[^1]}",
    //            "WorkerHalloween" => $"Worker.{splitRecipeKey[^2]}.{splitRecipeKey[^1]}",
    //            "WorkerBasic" => $"Worker.{splitRecipeKey[^2]}.{splitRecipeKey[^1]}",
    //            "DID" => $"Defender.{splitRecipeKey[^2]}.{splitRecipeKey[^1]}",
    //            "AID" => $"Alteration.{splitRecipeKey[^1]}",
    //            _ when splitRecipeKey[1].StartsWith("Manager") => $"Manager.{splitRecipeKey[2]}.{splitRecipeKey[^1]}",
    //            _ => "Unknown"
    //        };
    //        if (newKey!="Unknown" && !keyedUpgrades.ContainsKey(newKey))
    //        {
    //            target.Add(hash);
    //            knownHashes.Add(hash);
    //            keyedUpgrades.Add(newKey, target.Reserialise());
    //        }
    //        else if(!knownHashes.Contains(hash))
    //        {
    //            failedUpgrades.Add(item.Key+" | "+newKey+" | "+hash, target.Reserialise());
    //        }
    //    }
    //    if (failedUpgrades.Count > 0)
    //        keyedUpgrades["Failures"] = failedUpgrades;
    //    using var metaOutputFile = FileAccess.Open("res://MetaRecipesResult.json", FileAccess.ModeFlags.Write);
    //    metaOutputFile.StoreString(keyedUpgrades.ToString());
    //    LoadingOverlay.RemoveLoadingKey("meta");
    //}
}
