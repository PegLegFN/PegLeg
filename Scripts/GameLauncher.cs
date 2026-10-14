
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

public static class GameLauncher
{
	struct LaunchData
	{
		public DateTime QuickLaunchExpiry { get; set; }
		public string TargetAccount { get; set; }
		public bool IsSteam { get; set; }
	}

	static string LaunchDataFilePath => field ??= Bootstrap.IsEditor ?
				System.IO.Path.Combine(Helpers.GlobalisePath("user://"), $"../../../Tomatech Games/PegLeg{(AppConfig.PegLegVersion.IsBeta ? "/Beta" : "")}/launchData.json") :
				Helpers.GlobalisePath("user://launchData.json");

	static bool IsSteam = false;

	public static bool TryQuickLaunch(Action showSelector)
	{
		LaunchData launchData = default;
		GD.Print($"Checking for Launch Data at path: \"{LaunchDataFilePath}\"");
		if (FileAccess.FileExists(LaunchDataFilePath))
		{
			using (var launchDataFile = FileAccess.Open(LaunchDataFilePath, FileAccess.ModeFlags.Read))
			{
				launchData = JsonSerializer.Deserialize<LaunchData>(launchDataFile.GetAsText());
			}
			IsSteam |= launchData.IsSteam;
			DirAccess.RemoveAbsolute(LaunchDataFilePath);
		}
		if (launchData.QuickLaunchExpiry < DateTime.UtcNow && (Bootstrap.cmdLineArgs.Contains("--launchGame") || Bootstrap.cmdLineArgs.Any(a => a.StartsWith("--launchAccount="))))
		{
			launchData.QuickLaunchExpiry = DateTime.UtcNow.AddSeconds(1);
			if (Bootstrap.cmdLineArgs.FirstOrDefault(a => a.StartsWith("--launchAccount=")) is string launchAccountArg)
				launchData.TargetAccount = launchAccountArg[16..];
		}
		if (launchData.QuickLaunchExpiry < DateTime.UtcNow)
			return false;
		GD.Print("Quicklaunching Fortnite...");
		QuickLaunch(launchData, showSelector);
		return true;
	}

	static async void QuickLaunch(LaunchData launchData, Action showSelector)
	{
		GameAccount targetAccount = null;
		if (launchData.TargetAccount is not null)
			targetAccount = GameAccount.OwnedAccounts.FirstOrDefault(a => a.accountId == launchData.TargetAccount);
		if (targetAccount is null)
		{
			showSelector?.Invoke();
			return;
		}
		if (targetAccount is not null)
			await LaunchGame(targetAccount, true);
		Helpers.MainLoopSceneTree.Quit();
	}

	struct ManifestData
	{
		public string LaunchCommand { get; init; }
		public string LaunchExecutable { get; init; }
		public string InstallLocation { get; init; }
	}

	public static (string, string) ReadManifest()
	{
		var manifestFolder = System.IO.Path.Join(OS.GetEnvironment("PROGRAMDATA"), "Epic/EpicGamesLauncher/Data/Manifests");
		var manifestFiles = DirAccess.GetFilesAt(manifestFolder);
		foreach (var filePath in manifestFiles)
		{
			using var file = FileAccess.Open(System.IO.Path.Join(manifestFolder, filePath), FileAccess.ModeFlags.Read);
			var data = JsonSerializer.Deserialize<ManifestData>(file.GetAsText());
			if (data.LaunchExecutable?.StartsWith("FortniteGame") != true)
				continue;

			string launchExe = System.IO.Path.Combine(data.InstallLocation, data.LaunchExecutable);
			string obfId = data.LaunchCommand?.StartsWith(" -obfuscationid=") == true ? data.LaunchCommand[16..] : null;
			AppConfig.Set("launcher", "launchExe", launchExe);
			AppConfig.Set("launcher", "obfuscationId", obfId);
			return (launchExe, obfId);
		}

		return (null, null);
	}

	public static async void LaunchGamePrompt()
	{
		if (!AppConfig.Get("launcher", "skipPrompt", false))
		{
			var confirm = await GenericConfirmationWindow.ShowConfirmation("Launch Fortnite?", "Launch", contextText: $"Launch Fortnite as {GameAccount.ActiveAccount.DisplayName}?");
			if (confirm != true)
				return;
		}
		await LaunchGame(GameAccount.ActiveAccount, false);
	}

	public static async Task LaunchGame(GameAccount asAccount, bool quicklaunch = false)
	{
		GD.Print($"Launcher: Attempting to launch as {asAccount.DisplayName}");
		string launchExe = AppConfig.Get<string>("launcher", "launchExe");
		string obfId = AppConfig.Get<string>("launcher", "obfuscationId");

		if (launchExe is null || !FileAccess.FileExists(launchExe))
		{
			(launchExe, obfId) = ReadManifest();
			if (launchExe is null || !FileAccess.FileExists(launchExe))
			{
				if (!quicklaunch)
					GenericConfirmationWindow.ShowError("Fortnite install location could not be found", "Launch Error").StartTask();
				GD.PushWarning("Launcher: Fortnite install location could not be found");
				return;
			}
		}

		//check if Fortnite already running
		Godot.Collections.Array tasklistOutput = [];
		OS.Execute("tasklist.exe", ["/fo", "csv", "/nh"], tasklistOutput);
		string outputText = tasklistOutput.FirstOrDefault().AsString();
		HashSet<string> tasks = [.. outputText.Split("\r\n").Select(r => r.Split(',').FirstOrDefault()).Where(s => (s?.Length ?? 0) > 1).Select(s => s[1..^1])];

		if (tasks.Any(s => s.Contains("FortniteClient-Win64-Shipping", StringComparison.OrdinalIgnoreCase)))
		{
			if (!quicklaunch)
				GenericConfirmationWindow.ShowError("Game already running", "Launch Error").StartTask();
			GD.PushWarning($"Launcher: Game already running");
			return;
		}

		//if steam is set up and we are not running through steam, set quickstart settings and launch via steam
		IsSteam |= Bootstrap.cmdLineArgs.Contains("--steam");
		if (AppConfig.Get<string>("launcher", "steamAppId") is string steamAppId && !IsSteam && steamAppId.Length > 5)
		{
			if (steamAppId.StartsWith("steam://rungameid/"))
			{
				steamAppId = steamAppId[18..];
				AppConfig.Set("launcher", "steamAppId", steamAppId);
			}
			var error = OS.ShellOpen($"steam://rungameid/{steamAppId}");
			if (error == Error.Ok)
			{
				LaunchData newLaunchData = new()
				{
					QuickLaunchExpiry = DateTime.UtcNow.AddSeconds(60),
					TargetAccount = asAccount.accountId,
					IsSteam = true
				};

				using var launchDataFile = FileAccess.Open(LaunchDataFilePath, FileAccess.ModeFlags.Write);
				launchDataFile.StoreString(JsonSerializer.Serialize(newLaunchData));
				return;
			}
			else
			{
				if (!quicklaunch)
					await GenericConfirmationWindow.ShowError("Failed to launch via Steam, attempting to launch normally", "Launch Error");
				GD.PushWarning($"Failed to launch via steam: {error} ({steamAppId}). Attempting to launch normally");
				//AppConfig.Clear("launcher", "steamAppId");
			}
		}

		string initExchangeCode = await asAccount.GenerateExchangeCode();
		if (initExchangeCode is null)
		{
			if (!quicklaunch)
				GenericConfirmationWindow.ShowError("Could not create exchange code", "Launch Error").StartTask();
			GD.PushWarning($"Launcher: Could not create exchange code for {asAccount.DisplayName}");
			return;
		}

		var launcherAuthResponse = await GameClient.LauncherClient.LoginWithExchangeCode(initExchangeCode);
		if (await launcherAuthResponse.CheckForError())
		{
			if (!quicklaunch)
				GenericConfirmationWindow.ShowError("Failed to login to launcher", "Launch Error").StartTask();
			GD.PushWarning("Launcher: Failed to login to launcher");
			return;
		}

		AuthenticationHeaderValue launcherAuthHeader = new("Bearer", (await launcherAuthResponse.ReadJson())["access_token"].ToString());
		string launcherExchangeCode = await GameAccount.GenerateExchangeCode(launcherAuthHeader);

		List<string> args = [
			"-AUTH_LOGIN=unused",
			$"-AUTH_PASSWORD={launcherExchangeCode}",
			"-AUTH_TYPE=exchangecode",
			"-epicapp=Fortnite",
			"-EpicPortal",
		];
		if (obfId is not null)
			args.Add($"-obfuscationid={obfId}");

		launchExe = launchExe.Replace('\\', '/');
		var launchDir = launchExe.Split('/')[..^1].JoinString("\\");
		string[] startCommand = ["start", "/d", $"\"{launchDir}\"", "\"\"", $"\"{launchExe}\"", .. args];

		string fullCommand = $"C:\\Windows\\system32\\cmd.exe /d /s /c \"{startCommand.JoinString(" ")}\"";
		GD.Print($"Launcher: Running command \"{fullCommand.Replace(initExchangeCode, "{exchangeCode}")}\"");
		OS.CreateProcess("C:\\Windows\\system32\\cmd.exe", ["/d", "/s", "/c", startCommand.JoinString(" ")]);
	}

	const string steamEXE = "C:\\Program Files (x86)\\Steam\\steam.exe";

	public static void CreateSteamShortcut()
	{
		//not implemented :|
	}
}
