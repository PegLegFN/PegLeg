using Godot;
using LibGit2Sharp;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

#if TOOLS
[GlobalClass]
[Tool]
public partial class ReleaseTool : EditorScript
{
	Window window;
	Theme uiTheme;
	PackedScene uiScene;

	const string ghUser = "PegLegFN";
	const string ghRepo = "PegLeg";

	public override void _Run()
	{
		window?.QueueFree();
		window = new()
		{
			PopupWindow = true,
			Unresizable = true
		};

		uiScene ??= ResourceLoader.Load<PackedScene>("uid://hwjvtdiphc88");
		uiTheme ??= ResourceLoader.Load<Theme>("uid://cd7qoq3gay1op");

		var uiInstance = uiScene.Instantiate<Control>();
		window.AddChild(uiInstance);
		uiInstance.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		uiInstance.ResetOffsets();
		window.Theme = uiTheme;

		bool isReady = VerifyReadiness(out string status);

		var verLabel = uiInstance.GetNodeOrNull<Label>("%VerLabel");
		verLabel?.Text = $"Version: {AppConfig.PegLegVersion}";
		var statusLabel = uiInstance.GetNodeOrNull<Label>("%StatusLabel");
		statusLabel?.Text = $"Status: {status}";
		var skipExport = uiInstance.GetNodeOrNull<Button>("%SkipExport");
		var button = uiInstance.GetNodeOrNull<Button>("%Button");
		button?.Pressed += () =>
		{
			//button.Disabled = true;
			MakeRelease(skipExport?.ButtonPressed ?? false);
			window?.QueueFree();
			window = null;
		};
		button?.Disabled = !isReady;

		EditorInterface.Singleton.PopupDialog(window, new(200,200,300,150));
		window.CloseRequested += OnCloseRequested;
	}

	static IOrderedEnumerable<Tag> OrderTags(TagCollection tags) => tags.OrderByDescending(t => GithubHelper.ReleaseVersion.ParseOrDefault(t.FriendlyName));

	private static bool VerifyReadiness(out string status)
	{
		using var repo = new Repository("./");
		status = repo.Diff.Compare<TreeChanges>().Count.ToString();

		var repoStatus = repo.RetrieveStatus();

		int untracked = repoStatus.Untracked.Count();
		int added = repoStatus.Added.Count();
		int modified = repoStatus.Modified.Count();
		int removed = repoStatus.Removed.Count();
		int missing = repoStatus.Removed.Count();
		int totalChanges = untracked + added + modified + removed + missing;

		if (totalChanges > 0)
		{
			status = $"Uncommitted Changes ({untracked}+{added}+{modified}+{removed}+{missing} = {totalChanges})";
			GD.Print(status);
			//return false;
		}

		var latestCommit = repo.Head.Commits.FirstOrDefault();
		var latestTag = OrderTags(repo.Tags).FirstOrDefault();
		var latestTagVer = GithubHelper.ReleaseVersion.Parse(latestTag.FriendlyName, out var parsedVer) ? parsedVer : new(0, 0, 0);
		if (latestTag.Target == latestCommit)
		{
			//GD.Print("latestTargetIsLatestCommit");
			if (latestTagVer != AppConfig.PegLegVersion)
			{
				status = $"Incorrect tag ({latestTagVer}) on latest commit";
				GD.Print(status);
				return false;
			}
		}
		else
		{
			//GD.Print("latestTargetIsNOTLatestCommit");
			//GD.Print($"{latestTagVer} == {AppConfig.PegLegVersion} = {latestTagVer == AppConfig.PegLegVersion}");
			if (latestTagVer == AppConfig.PegLegVersion)
			{
				status = $"Version already exists";
				GD.Print(status);
				return false;
			}
		}
		if (latestTagVer > AppConfig.PegLegVersion)
		{
			status = $"Version went backwards? ({latestTagVer})";
			GD.Print(status);
			return false;
		}

		status = "Ready";
		return true;
	}

	static PushOptions pushOptions = new();
	private static async void MakeRelease(bool skipExport = false)
	{
		if (!VerifyReadiness(out _))
		{
			GD.Print("Booo...");
			return;
		}

		//todo: auto switch presets depending on if ver number is beta
		//start exports
		Task buildTasks = Task.CompletedTask;
		if (!skipExport)
		{
			GD.Print("Starting exports");
			buildTasks = Task.WhenAll(
				ExportWindows(true),
				ExportAndroid(true)
			);
		}

		string ghKey = null;
		const string keyPath = "res://Builds/bangersAndMash.txt";
		//GD.Print(ProjectSettings.GlobalizePath(keyPath));
		using (var keyFile = FileAccess.Open(keyPath, FileAccess.ModeFlags.Read))
		{
			ghKey = keyFile.GetAsText();
		}
		if (string.IsNullOrWhiteSpace(ghKey))
		{
			GD.Print("No GH Key");
			return;
		}
		pushOptions.CredentialsProvider = new((url, username, type) => new UsernamePasswordCredentials() { Username = ghUser, Password = ghKey });
		AuthenticationHeaderValue ghAuth = new("Bearer", ghKey);

		using var repo = new Repository("./");
		var headBranch = repo.Head;
		if (repo.Head.Commits.FirstOrDefault() is not Commit latestCommit)
		{
			GD.Print("missing commit");
			return;
		}

		GD.Print($"pushing branch {headBranch.FriendlyName}...");
		await Task.Run(() => repo.Network.Push(headBranch, pushOptions));

		/* Releases can store tag names & commit hashes without preemptively creating tags, so this bit is unneeded
		//if latest tag doesnt match, create and push it
		var latestTag = OrderTags(repo.Tags).FirstOrDefault();
		var latestVer = GithubHelper.ReleaseVersion.Parse(latestTag.FriendlyName, out var parsedVer) ? parsedVer : new(0, 0, 0);
		if (latestVer != AppConfig.PegLegVersion)
		{
			GD.Print("missing tag, creating...");
			latestTag = repo.ApplyTag(AppConfig.PegLegVersion.ToString(), latestCommit.Sha);
			latestVer = AppConfig.PegLegVersion;
			var refSpec = $"refs/tags/" + latestTag.FriendlyName;
			GD.Print("pushing tag...");
			await Task.Run(() => repo.Network.Push(repo.Network.Remotes[headBranch.RemoteName], refSpec, refSpec, pushOptions));
		}
		GD.Print("pushed");

		var prevTag = OrderTags(repo.Tags).FirstOrDefault(t => t != latestTag);
		*/

		var latestVer = AppConfig.PegLegVersion;

		//generate changelog out of commit descriptions between previous and current versions
		var prevTag = OrderTags(repo.Tags).FirstOrDefault(t => t.FriendlyName != latestVer.ToString());
		string releaseDescription = "";
		if (prevTag is null)
		{
			GD.Print($"Missing prev tag");
		}
		else
		{
			var prevCommit = (Commit)prevTag.Target;
			if (prevCommit is null)
			{
				GD.Print($"Not a commit ({prevTag.Target.Sha[..6]})");
				return;
			}
			GD.Print($"Finding commits between {prevTag.FriendlyName} and {latestVer} (ie. all commits until ({prevCommit.Sha[..6]}))");
			var betweenCommits = repo.Head.Commits.TakeWhile(c => c != prevCommit);
			releaseDescription = betweenCommits.Select(c => c.Message).JoinString("\n");
		}
		//GD.Print(releaseDescription);

		if (!skipExport)
			GD.Print("Waiting for exports to complete...");
		await buildTasks;
		if (buildTasks.IsFaulted)
		{
			GD.Print("Exception during export");
			GD.PushError(buildTasks.Exception);
			return;
		}

		GD.Print("Creating release...");
		//create draft release out of tag
		var releaseResponse = await GithubHelper.githubApi.MakeRequest($"/repos/{ghUser}/{ghRepo}/releases", HttpMethod.Post)
			.AddHeader("Accept", "application/vnd.github+json")
			.AddHeader("X-GitHub-Api-Version", "2026-03-10")
			.SetAuthorisation(ghAuth)
			.SetJsonContent(new JsonObject()
			{
				["tag_name"] = latestVer.ToString(),
				["target_commitish"] = latestCommit.Sha,
				["name"] = $"Release {latestVer}",
				["body"] = releaseDescription,
				["draft"] = true,
				["prerelease"] = true, // based on if version is beta
			})
			.Send();
		GD.Print("Release Created");

		if (await releaseResponse.CheckForError())
			return;

		var releaseData = await releaseResponse.ReadJson<GithubHelper.ReleaseData>();

		//upload builds to tag
		//.apk => application/vnd.android.package-archive
		//.7z => application/x-compressed
		//.msi => application/octet-stream
		//GD.Print(releaseData.upload_url);
		GD.Print("Uploading Builds...");
		bool[] successes = [
			await UploadBuild(releaseData.upload_url, "res://Builds/Windows/Beta/PegLegBeta-Windows.msi", "application/octet-stream", "Windows Installer (Preferred)", ghAuth),
			await UploadBuild(releaseData.upload_url, "res://Builds/Windows/Beta/PegLegBeta-Windows.7z", "application/x-compressed", "Compressed Windows Program", ghAuth),
			await UploadBuild(releaseData.upload_url, "res://Builds/Android/Beta/PegLegBeta-Android.apk", "application/vnd.android.package-archive", "Android App Package", ghAuth)
		];
		bool allSucceeded = successes.All(b => b);
		if (allSucceeded)
			GD.Print("All Uploads Complete");
		else
			GD.Print($"{successes.Count(b=>b)}/{successes.Length} Uploads Complete");

		OS.ShellOpen(releaseData.html_url.Replace("/tag/", "/edit/"));
	}

	private static async Task ExportWindows(bool isBeta)
	{
		const string baseOutPath = "C:\\Users\\Tomatech\\Repositories\\TomatechGames\\Godot Projects\\PegLeg\\Builds\\Windows";
		int exportStatus = 0;
		await Task.Run(() => exportStatus = OS.Execute(OS.GetExecutablePath(), ["--headless", "--export-release", isBeta ? "Windows (Test)" : "Windows", $"{baseOutPath}\\Beta\\Build\\PegLeg.exe"], openConsole: true));
		if (exportStatus != 0)
			throw new ApplicationException($"Windows Export Failed: {exportStatus}");
		int compressStatus = 0;
		int installerStatus = 0;
		await Task.WhenAll(
			Task.Run(() => compressStatus = OS.Execute("7z", ["a", "-t7z", $"{baseOutPath}\\Beta\\PegLegBeta-Windows.7z", $"{baseOutPath}\\Beta\\Build\\*"], openConsole: true)),
			Task.Run(() => installerStatus = OS.Execute($"{baseOutPath}\\buildInstaller.bat", [], openConsole:true))
		);
		if (compressStatus != 0)
			throw new ApplicationException($"Windows 7Z Failed: {compressStatus}");
		if (installerStatus != 0)
			throw new ApplicationException($"Windows Installer Failed: {installerStatus}");
	}

	private static async Task ExportAndroid(bool isBeta)
	{
		const string outPath = "C:\\Users\\Tomatech\\Repositories\\TomatechGames\\Godot Projects\\PegLeg\\Builds\\Android\\Beta\\PegLegBeta-Android.apk";
		int exportStatus = 0;
		await Task.Run(() => exportStatus = OS.Execute(OS.GetExecutablePath(), ["--headless", "--export-release", isBeta ? "Android (Test)" : "Android", outPath], openConsole: true));
		if (exportStatus != 0)
			throw new ApplicationException($"Android Export Failed: {exportStatus}");
	}

	static async Task<bool> UploadBuild(string uploadURL, string path, string type, string label, AuthenticationHeaderValue auth)
	{
		GD.Print($"- Uploading \"{label}\"...");
		var globalPath = ProjectSettings.GlobalizePath(path);
		var content = new ByteArrayContent(System.IO.File.ReadAllBytes(globalPath));
		content.Headers.Add("Content-Type", type);
		var name = globalPath.Split('/')[^1];
		var uploadResponse = await WebHelpers.MakeRequest(uploadURL.Replace("{?name,label}", $"?name={name}&label={label}"), HttpMethod.Post)
			.AddHeader("Accept", "application/vnd.github+json")
			.AddHeader("X-GitHub-Api-Version", "2026-08-17")
			.SetAuthorisation(auth)
			.SetContent(content)
			.Send();
		return !await uploadResponse.CheckForError();
	}

	private void OnCloseRequested()
	{
		window?.QueueFree();
		window = null;
	}
}
#endif