using Godot;
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
		GetWindow().ContentScaleFactor = newInterfaceScale * (OS.HasFeature("mobile") ? 1 : 1);
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && !keyEvent.IsEcho() && keyEvent.Pressed && keyEvent.Keycode == Key.M && keyEvent.CtrlPressed)
			VolumeController.ToggleBusMuted("Master");
	}

	void ImportAccounts()
	{
		bool isBeta = AppConfig.PegLegVersion.IsBeta;
		string fromPath = isBeta ? "user://../accounts" : "user://Beta/accounts";
		foreach (var file in DirAccess.GetFilesAt(fromPath))
		{
			DirAccess.CopyAbsolute($"{fromPath}/{file}", $"user://accounts/{file}");
		}
		GameAccount.UpdateAccountCache();
		accountImportButton.Visible = false;
	}

	async void CopyExchange()
	{
		if (!GameAccount.ActiveAccount.isOwned)
			return;
		string exchangeCode = null;
		using (var _ = LoadingOverlay.CreateToken())
		{
			exchangeCode = await GameAccount.ActiveAccount.GenerateExchangeCode();
		}
		if (exchangeCode is null)
		{
			GenericConfirmationWindow.ShowError("Failed to generate Exchange Code").StartTask();
			return;
		}
		DisplayServer.ClipboardSet(exchangeCode);
		GenericConfirmationWindow.ShowInfo("Exchange Code copied").StartTask();
	}

	void OpenIssuePage()
	{
		OS.ShellOpen("https://github.com/PegLegFN/PegLeg/issues");
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

	void ForceHourlySignal()
	{
		RefreshTimerController.ForceHourChanged();
	}

	(int, int) GetRequiredXP(GameProfile campaignProfile)
	{
		var level = campaignProfile.statAttributes?["collection_book"]?["maxBookXpLevelAchieved"]?.GetValue<int>() ?? 0;
		return (level, data.Rows.TryGetValue((level + 1).ToString(), out var row) ? row.TotalXpToGetToThisLevel : 0);
	}

	public async void ClaimCollectionRewards()
	{
		using var loadToken = LoadingOverlay.CreateToken();
		var profile = GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems);
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
