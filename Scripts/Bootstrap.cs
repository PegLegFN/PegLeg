using Godot;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using FileAccess = Godot.FileAccess;


public partial class Bootstrap : Node
{
    const string processLockPath = "user://pid";
    const string pipeName = "PegLegPipe";
    const int majorPackageVersion = 1;
    const int minorPackageVersion = 1;

    [Export]
    Vector2I windowSize = new(1350, 720);
    [Export]
    Control curtain;
    [Export]
    Label progressLabel;
	[ExportGroup("Scenes")]
	[Export]
	PackedScene desktopOnboarding;
    [Export]
    PackedScene desktopInterface;

    static void DeleteContents(string path)
    {
        foreach (var dir in DirAccess.GetDirectoriesAt(path))
        {
            var fullPath = Path.Combine(path, dir);
            DeleteContents(fullPath);
        }
        foreach (var file in DirAccess.GetFilesAt(path))
        {
            var fullPath = Path.Combine(path, file);
            DirAccess.RemoveAbsolute(fullPath);
        }
        DirAccess.RemoveAbsolute(path);
    }

    public override async void _Ready()
    {
        var window = GetWindow();
        window.ContentScaleSize = window.Size;
        window.MoveToCenter();
        progressLabel.Text = "Preparing...";

        if (FileAccess.FileExists(processLockPath))
        {
            using var processFile = FileAccess.Open(processLockPath, FileAccess.ModeFlags.Read);
            var exeName = OS.GetExecutablePath().GetBaseName().GetFile();
            var existingPid = (int)processFile.Get64();
            Godot.Collections.Array output = [];
            var result = OS.Execute("cmd.exe", ["/c", "tasklist", "/fi", "pid eq " + existingPid], output);
            if
            (
                output.Count > 0 &&
                output[0].AsString()?.Split("\n") is string[] outLines &&
                outLines.Length >= 4 &&
                outLines[3].StartsWith(exeName)
            )
            {
                GD.Print("PegLeg already running, exiting process");
                try
                {
                    using NamedPipeClientStream pipeClient = new(pipeName);
                    GD.Print("Attempting pipe connection");
                    pipeClient.Connect(5000);
                    if (pipeClient.IsConnected)
                    {
                        GD.Print("Pipe connected");
                        using StreamWriter writer = new(pipeClient);
                        writer.WriteLine("showWindow");
                        writer.WriteLine("disconnect");
                        writer.Flush();
                        GD.Print("Sending: showWindow");
                    }
                    else
                    {
                        GD.Print("Pipe connection failed");
                    }
                    Thread.Sleep(2000);
                }
                catch { }

                GetTree().Quit();
                return;
            }
        }

        using (var processFile = FileAccess.Open(processLockPath, FileAccess.ModeFlags.Write))
        {
            var currentPid = OS.GetProcessId();
            processFile.Store64((ulong)currentPid);
        }


        try
        {
            NamedPipeContainer.OpenPipe();
            await Initialise(window);
        }
        catch(Exception e)
        {
            GD.Print(e);
            Thread.Sleep(5000);
            GetTree().Quit();
        }
    }

    async Task Initialise(Window window)
    {
        AppConfig.PreloadConfig();

        if (FileAccess.FileExists(Helpers.GlobalisePath("res://update.exe")))
            DirAccess.RemoveAbsolute(Helpers.GlobalisePath("res://update.exe"));

        var oldExternalFolder = Helpers.GlobalisePath("res://External");
        GD.Print("External: " + oldExternalFolder);
        if (!Engine.IsEditorHint() && DirAccess.DirExistsAbsolute(oldExternalFolder))
            DeleteContents(oldExternalFolder);

        //bool hasBanjoAssets = await PegLegResourceManager.ReadAllSources();
        await PegLegResourceManager.FetchAndLoadPackages(majorPackageVersion, minorPackageVersion, p => progressLabel.Text = $"{p}...");
        //await PegLegResourceManager.TempImportResources();
        await Helpers.WaitForFrame();

        var lastUsedId = AppConfig.Get<string>("account", "lastUsed");
        bool hasAccount = false;
        if (lastUsedId is not null)
        {
            GD.Print("last: " + lastUsedId);
            var lastUsedAccount = GameAccount.GetOrCreateAccount(lastUsedId);
            hasAccount = await lastUsedAccount.SetAsActiveAccount(p => progressLabel.Text = $"{p}...");
        }

        //TODO: if more than one account has device details, show account selector
        if (!hasAccount)
        {
            foreach (var a in GameAccount.OwnedAccounts)
            {
                progressLabel.Text = "Login Failed. Attempting another account...";
                await Helpers.WaitForTimer(1.5);
                if (!await a.SetAsActiveAccount(p => progressLabel.Text = $"{p}..."))
                    continue;
                hasAccount = true;
                break;
            }
            if (!hasAccount && GameAccount.OwnedAccounts.Length > 0)
            {

                progressLabel.Text = "Login Failed. No accounts remaining";
                await Helpers.WaitForTimer(1.5);
            }
        }
        if (GameAccount.activeAccount is not null)
        {
            progressLabel.Text = "Fetching missions...";
            await GameMission.UpdateMissions();
            progressLabel.Text = "Fetching catalog...";
            await GameStorefront.UpdateCatalog();
            progressLabel.Text = "Generating XRay Llama contents...";
            await GameAccount.activeAccount.GenerateXRayLlamaResults();
            progressLabel.Text = "Updating quests...";
            await GameAccount.activeAccount.ClientQuestLoginCampaign();
            await GameAccount.activeAccount.ClientQuestLoginAthena();
        }

        progressLabel.Text = "";
        curtain.Visible = true;
        await Helpers.WaitForFrame();
        window.Size = windowSize;
        window.MoveToCenter();
        window.Transparent = false;
        window.TransparentBg = true;
        window.Borderless = false;
        window.Unfocusable = false;

        var iconPath = ProjectSettings.GetSettingWithOverride("application/config/icon").ToString();
        DisplayServer.SetIcon(ResourceLoader.Load<Texture2D>(iconPath).GetImage());

        await Helpers.WaitForFrame();
        await Helpers.WaitForFrame();

        //todo: autoselect desktop/mobile scenes here
        if (hasAccount)
            GetTree().ChangeSceneToPacked(desktopInterface);
        else
            GetTree().ChangeSceneToPacked(desktopOnboarding);
    }

    static class NamedPipeContainer
    {
        static bool running = false;
        static Thread pipeThread;
        public static void OpenPipe()
        {
            if (running)
                return;
            running = true;
            pipeThread = new Thread(PipeLogic);
            pipeThread.Start();
        }

        private static void PipeLogic()
        {
            try
            {
                using NamedPipeServerStream pipeServer = new(pipeName);
                using StreamReader reader = new(pipeServer);
                GD.Print("Pipe server started");

                while (true)
                {
                    //todo: better disconnect handling?
                    pipeServer.WaitForConnection();
                    while (pipeServer.IsConnected)
                    {
                        var input = reader.ReadLine();
                        GD.Print("Pipe recieved " + input);
                        switch (input)
                        {
                            case "showWindow":
                                TrayIcon.UnminimiseDeferred();
                                break;
                            case "disconnect":
                                pipeServer.Disconnect();
                                break;
                        }
                    }
                }
            }
            catch
            {
                GD.Print("Pipe server failed");
                running = false;
            }
        }
    }
}
