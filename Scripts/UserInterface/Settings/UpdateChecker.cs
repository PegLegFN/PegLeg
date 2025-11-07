using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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
    List<Control> releaseEntries = [];

	public override void _Ready()
	{
        CheckForUpdates();
	}

	bool isChecking = false;
	GithubHelper.ReleaseData? latestRelease;
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
			latestRelease = null;
            GithubHelper.ReleaseVersion currentVer;

            if (OS.HasFeature("editor"))
            {
                failMsg.Text = "Auto updates disabled in editor builds.";
                return;
            }

            using (var verFile = FileAccess.Open(Helpers.GlobalisePath("res://v.txt"), FileAccess.ModeFlags.Read))
			{
				if(verFile is null || verFile.GetError() != Error.Ok)
				{
					failMsg.Text = "Failed to read current version.";
					return;
				}
				if(!GithubHelper.ReleaseVersion.Parse(verFile.GetAsText(), out currentVer))
                {
                    failMsg.Text = "Failed to parse current version.";
                    return;
                }
			}

            if (false)
            {
                string[] verData = ProjectSettings.GetSetting("application/config/version").AsString().Split(".");
                if (verData.Length >= 3)
                {
                    //release version
                    currentVer = new(
                        int.Parse(verData[0]),
                        int.Parse(verData[1]),
                        int.Parse(verData[2]),
                        0
                    );
                    if (verData.Length >= 4)
                    {
                        currentVer = currentVer with { prerelease = int.Parse(verData[3]) };
                    }
                }
            }

			bool useBeta = currentVer.prerelease > 0;
            try
            {
                var releases = await GithubHelper.FetchReleases("TomatechGames", "PegLeg");
                var filteredReleases = releases.Where(r => r.Version > currentVer && (useBeta || r.Version.prerelease == 0)).ToArray();
                if (filteredReleases.Length == 0)
                {
                    failMsg.Text = "Up to date :)";
                    return;
                }
                latestRelease = filteredReleases[0];
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
                    curReleaseEntry.GetNode<Label>("%Tag").Text = filteredReleases[i].tag_name;
                    curReleaseEntry.GetNode<Label>("%Body").Text = filteredReleases[i].body;
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

	public async void DownloadAndRunInstaller()
	{
		if (latestRelease is null)
			return;
		var realRelease = latestRelease ?? default;
		var asset = realRelease.assets.FirstOrDefault(a => a.name == "PegLegInstaller-Win64.exe");
		if (asset == default)
			return;
		using var _ = LoadingOverlay.CreateToken();
		try
        {
            using (FileAccessStream fileStream = new(Helpers.GlobalisePath("res://update.exe"), FileAccess.ModeFlags.Write))
            {
                await asset.DownloadTo(fileStream);
            }
			int pid = OS.CreateProcess(Helpers.GlobalisePath("res://update.exe"), [$"--update={Helpers.GlobalisePath("res://")}", $"--ver={realRelease.Version}"]);
            if(pid!=-1)
                GetTree().Quit();
        }
		catch
		{
			GD.PushWarning("Failed to update");
		}
    }
}
