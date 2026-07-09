using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ReleaseData = GithubHelper.ReleaseData;

public partial class UpdateChecker : Control
{
	[Export]
	Label failMsg;
	[Export]
	Control updateBtn;
	[Export]
	Control releaseParent;
	[Export]
	PackedScene releaseScene;
	[Export]
	Label currentVersionLabel;
	List<Control> releaseEntries = [];

	public override void _Ready()
	{
		CheckForUpdates();
		if (currentVersionLabel is not null)
			currentVersionLabel.Text = AppConfig.PegLegVersion.ToString();
	}

	bool isChecking = false;
	ReleaseData? possibleLatestRelease;
	bool latestIsBreakpoint = false;
	public async void CheckForUpdates()
	{
		if (isChecking)
			return;
		isChecking = true;
		try
		{
			failMsg.Text = "Loading...";
			failMsg.Visible = true;
			releaseParent.Visible = false;
			updateBtn.Visible = false;
			possibleLatestRelease = null;
			var currentVer = AppConfig.PegLegVersion;

			if (OS.HasFeature("editor"))
			{
				currentVer = currentVer with { patch = currentVer.patch - 2 };
				//failMsg.Text = "Auto updates disabled in editor builds.";
				//return;
			}

			//         using (var verFile = FileAccess.Open(Helpers.GlobalisePath("res://v.txt"), FileAccess.ModeFlags.Read))
			//{
			//	if(verFile is null || verFile.GetError() != Error.Ok)
			//	{
			//		failMsg.Text = "Failed to read current version.";
			//		return;
			//	}
			//	if(!GithubHelper.ReleaseVersion.Parse(verFile.GetAsText(), out currentVer))
			//             {
			//                 failMsg.Text = "Failed to parse current version.";
			//                 return;
			//             }
			//}

			//if (true)
			//{
			//    string[] verData = ProjectSettings.GetSetting("application/config/version").AsString().Split(".");
			//    if (verData.Length == 3)
			//    {
			//        int betaAndPatch = int.Parse(verData[2]);
			//        int patch = betaAndPatch / 1000;
			//        int beta = betaAndPatch % 1000;

			//        //release version
			//        currentVer = new(
			//            int.Parse(verData[0]),
			//            int.Parse(verData[1]),
			//            patch,
			//            beta
			//        );
			//    }
			//}

			bool useBeta = currentVer.IsBeta;
			try
			{
				var releases = await GithubHelper.FetchReleases("TomatechGames", "PegLeg");
				var filteredReleases = releases.Where(r => r.Version > currentVer && (useBeta || !r.Version.IsBeta)).ToArray();
				if (filteredReleases.Length == 0)
				{
					failMsg.Text = "Up to date :)";
					return;
				}
				UpdateNotificationHook.SetNotifVisible();
				possibleLatestRelease = filteredReleases[0];
				latestIsBreakpoint = false;
				if (filteredReleases.LastOrDefault(r => r.body.Contains("BREAKPOINT")) is ReleaseData breakpointRelease)
				{
					latestIsBreakpoint = possibleLatestRelease == breakpointRelease;
					possibleLatestRelease = breakpointRelease;
				}
				//show filtered releases
				for (int i = 0; i < filteredReleases.Length; i++)
				{
					if (i >= releaseEntries.Count)
					{
						var newRelease = releaseScene.Instantiate<Control>();
						releaseParent.AddChild(newRelease);
						releaseEntries.Add(newRelease);
					}
					var curReleaseEntry = releaseEntries[i];
					curReleaseEntry.Visible = true;
					curReleaseEntry.GetNode<Label>("%Name").Text = filteredReleases[i].name;
					//curReleaseEntry.GetNode<Label>("%Tag").Text = filteredReleases[i].tag_name;
					var link = curReleaseEntry.GetNode<LinkButton>("%TagAndLink");
					link.Text = filteredReleases[i].tag_name;
					link.Uri = filteredReleases[i].html_url;
					curReleaseEntry.GetNode<Label>("%Body").Text = filteredReleases[i].body.FixNewlines();
					curReleaseEntry.GetNode<Control>("%Separator").Visible = i != (filteredReleases.Length - 1);
				}
				for (int i = filteredReleases.Length; i < releaseEntries.Count; i++)
				{
					releaseEntries[i].Visible = false;
				}

				failMsg.Visible = false;
				updateBtn.Visible = true;
				releaseParent.Visible = true;
			}
			catch (Exception ex)
			{
				failMsg.Text = "An Error occured while fetching versions";
				GD.PushWarning(ex);
			}
		}
		finally
		{
			isChecking = false;
		}
	}

	[GeneratedRegex("`Flags:\\[(?:(\\w+(?:=\\w+)?),?)*\\]`")]
	private static partial Regex FlagParser();

	Dictionary<string, string> ReadReleaseFlags(string description)
	{
		if (description is null)
			return [];
		var match = FlagParser().Match(description);
		if (!match.Success)
			return [];
		Dictionary<string, string> results = [];
		foreach (var group in match.Groups.Values)
		{
			if (group.Value.Contains('='))
			{
				var split = group.Value.Split('=');
				results.Add(split[0], split[1]);
			}
			else
				results.Add(group.Value, null);
		}
		return results;
	}

	public async void DownloadAndRunInstaller()
	{
		if (possibleLatestRelease is not ReleaseData release)
			return;

		var flags = ReadReleaseFlags(release.body);
		if (flags.ContainsKey("BREAKPOINT") && !latestIsBreakpoint)
		{
			await GenericConfirmationWindow.ShowInfo("Due to some breaking changes made in these updates, you will need to update multiple times to be fully up-to-date");
		}
		if (flags.ContainsKey("ForceManual"))
		{
			var goToURL = await GenericConfirmationWindow.ShowConfirmation("Manual Update Required", contextText: "This update must be installed manually from the Releases Page", postiveText: "Open");
			if (goToURL == true)
				OS.ShellOpen(release.html_url);
			return;
		}

#if GODOT_WINDOWS
		var asset = release.assets?.FirstOrDefault(a => a.name.EndsWith(".msi")) ?? default;
		if (asset == default)
		{
			await GenericConfirmationWindow.ShowError("No installer file found");
			return;
		}
		try
		{
            var updatePath = Helpers.GlobalisePath($"user://Updates/Update-{release.Version}.msi");
			var updateFolder = Helpers.GlobalisePath("user://Updates");
			try
			{
				using var overlay = LoadingOverlay.CreateToken();
				if (!FileAccess.FileExists(updatePath))
				{
					if (!DirAccess.DirExistsAbsolute(updateFolder))
						DirAccess.MakeDirAbsolute(updateFolder);
					using (FileAccessStream fileStream = new(Helpers.GlobalisePath(updatePath), FileAccess.ModeFlags.Write))
					{
						await asset.DownloadTo(fileStream, overlay);
					}
					await Helpers.WaitForTimer(1);
				}
			}
			catch (Exception ex)
			{
				GD.PushError("Error when downloading desktop update:\n"+ex);
				await GenericConfirmationWindow.ShowError("An error occured when downloading the update");
				return;
			}
			//await GenericConfirmationWindow.ShowInfo("The Update has been downloaded. PegLeg will close, and the \"Updates\" folder will be opened. Run the .msi file to Install the Update", "Update Downloaded");
			//await GenericConfirmationWindow.ShowInfo("The Update has been downloaded. PegLeg will close, and the Installer will be run", "Update Downloaded");
			OS.ShellOpen(updatePath);
			GetTree().Quit();
			/* Disabled until I figure out how to run MSI installers from within PegLeg
            var batchPath = Helpers.GlobalisePath("user://update.bat");
            using (var batFile = FileAccess.Open(batchPath, FileAccess.ModeFlags.Write))
            {
                batFile.StoreString("""
                rem Batch script to elevate permissions for installing updates to work
                rem checks if permissions 
                %SystemRoot%\System32\net.exe file 1>NUL 2>NUL
                if errorlevel 1 (
                    %SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe Start-Process -FilePath "%0" -ArgumentList "%cd%" -verb runas >NUL 2>&1
                    exit /b
                )
                cd /d %1
                msiexec /i update.msi
                """);
            }
            int pid = OS.CreateProcess(batchPath, []);
            if (pid != -1)
            {
                GetTree().Quit();
                return;
            }
            OS.ShellOpen(Helpers.GlobalisePath("user://"));
            await GenericConfirmationWindow.ShowError("Could not run update.msi automatically.\nPlease run update.msi manually.", "Update failed");
            */
		}
		catch (Exception e)
		{
			GD.PushError(e);
			await GenericConfirmationWindow.ShowError("Update failed");
		}
#elif GODOT_ANDROID
        var asset = release.assets.FirstOrDefault(a => a.name.EndsWith(".apk"));
		if (asset == default)
        {
            await GenericConfirmationWindow.ShowError("No App Package (.apk file) found");
            return;
        }
        try
        {
            OS.ShellOpen(asset.browser_download_url);
            await GenericConfirmationWindow.ShowError("The App Package (.apk file) is being downloaded in your browser. Running it will install the update", "Download Started     ");
        }
		catch(Exception e)
        {
            GD.PushError(e);
            await GenericConfirmationWindow.ShowError("Update failed");
        }
#else
        await GenericConfirmationWindow.ShowError("Update unavailable on this platform", "Error     ");
#endif
	}

}
