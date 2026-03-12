using Godot;
using System;
using System.Collections.Frozen;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileAccess = Godot.FileAccess;


public partial class Bootstrap : Node
{
    public const string processLockPath = "user://pid";
    const string pipeName = "PegLegPipe";
    const int majorPackageVersion = 3;
    const int minorPackageVersion = 0;

    [Export]
    Vector2I bootSize = new(300, 300);
    [Export]
    Vector2I windowSize = new(1350, 720);
    [Export]
    Control background;
    [Export]
    Control curtain;
    [Export]
    Control loadingContent;
    [Export]
    Label progressLabel;
    [Export]
    ProgressBar progressBar;
    [Export]
    bool shareInEditor;
    [Export]
    GpuParticles2D downloadParticles;

    [ExportGroup("Scenes")]
    [ExportSubgroup("Desktop")]
    [Export(PropertyHint.File, "*.tscn")]
    string testingSceneUid;
    [Export]
    PackedScene testingScene;
    [Export]
    bool testingRequiresAccount = true;
    [Export(PropertyHint.File, "*.tscn")]
    string desktopOnboardingUid;
    [Export]
	PackedScene desktopOnboarding;
    [Export(PropertyHint.File, "*.tscn")]
    string desktopInterfaceUid;
    [Export]
    PackedScene desktopInterface;
    [Export(PropertyHint.File, "*.tscn")]
    string liteInterfaceUid;
    [Export]
    PackedScene liteInterface;
    [Export(PropertyHint.File, "*.tscn")]
    string shareMenuUid;
    [Export]
    PackedScene shareMenu;
    [ExportSubgroup("Mobile")]
    [Export(PropertyHint.File, "*.tscn")]
    string mobileInterfaceUid;
    [Export]
    PackedScene mobileInterface;
    [Export(PropertyHint.File, "*.tscn")]
    string mobileLiteInterfaceUid;
    [Export]
    PackedScene mobileLiteInterface;

    [ExportGroup("UserPrefs")]
    [Export]
    Control liteContent;

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

    public static readonly FrozenSet<string> cmdLineArgs = OS.GetCmdlineArgs().ToFrozenSet();
    public static bool StartMinimised { get; private set; } = cmdLineArgs.Contains("--start-minimised");
    public static bool UseShareMenu { get; private set; } = cmdLineArgs.Contains("--share-menu");

    static bool hasBooted = false;

    public override void _Ready()
    {
        var window = GetWindow();

        window.Mode = Window.ModeEnum.Windowed;
        window.ContentScaleSize = window.Size = bootSize;
        window.Transparent = true;
        window.TransparentBg = true;
        window.Borderless = true;
        window.Unfocusable = false;
        window.MoveToCenter();

#if GODOT_ANDROID
        background.Visible = true;
        //DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Portrait);
#endif

        if (hasBooted)
        {
            Initialise();
            return;
        }
        hasBooted = true;

        liteContent.Visible = false;
        loadingContent.Visible = true;
        progressLabel.Text = "Preparing...";

        if (StartMinimised)
            window.Mode = Window.ModeEnum.Minimized;
        UseShareMenu |= OS.HasFeature("editor") && shareInEditor;

#if GODOT_WINDOWS
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
                window.Mode = Window.ModeEnum.Minimized;
                try
                {
                    using NamedPipeClientStream pipeClient = new(pipeName);
                    GD.Print("Attempting pipe connection");
                    pipeClient.Connect(5000);
                    if (pipeClient.IsConnected)
                    {
                        GD.Print("Pipe connected");
                        using StreamWriter writer = new(pipeClient);
                        //if this instance was trying to open a file, forward the file to the running instance
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
#endif

#if GODOT_WINDOWS
        var updatePath = Helpers.GlobalisePath("user://updateTest.msi");
        if (FileAccess.FileExists(updatePath))
        {
            //var batchPath = Helpers.GlobalisePath("user://update.bat");
            //using (var batFile = FileAccess.Open(batchPath, FileAccess.ModeFlags.Write))
            //{
            //    batFile.StoreString("""
            //    rem Batch script to elevate permissions for installing updates to work
            //    rem checks if permissions 
            //    %SystemRoot%\System32\net.exe file 1>NUL 2>NUL
            //    if errorlevel 1 (
            //        %SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe Start-Process -FilePath "%0" -ArgumentList "%cd%" -verb runas >NUL 2>&1
            //        exit /b
            //    )
            //    cd /d %1
            //    msiexec /i update.msi
            //    pause
            //    """);
            //}
            //int pid = OS.CreateProcess(batchPath, [], true);
            Godot.Collections.Array output = [];
            var aList = $"'/i \"{updatePath}\"'";
            GD.Print(aList);
            GD.Print("");
            OS.Execute("powershell.exe", ["-Command", "Start-Process", "msiexec", "-ArgumentList", aList], output);
            foreach (var a in output)
            {
                GD.Print(a.AsString());
            }

        }
        if (FileAccess.FileExists("user://update.msi"))
            DirAccess.RemoveAbsolute("user://update.msi");
        if (FileAccess.FileExists("user://update.bat"))
            DirAccess.RemoveAbsolute("user://update.bat");
#endif

        try
        {
#if GODOT_WINDOWS
            NamedPipeContainer.OpenPipe();
#endif
            Initialise();
        }
        catch(Exception e)
        {
            GD.Print(e);
            Thread.Sleep(5000);
            GetTree().Quit();
        }
    }

    async void Initialise()
    {
        liteContent.Visible = false;
        loadingContent.Visible = false;

        if (!AppConfig.TryGet("core", "litemode", out bool lite))
        {
            liteContent.Visible = true;
            return;
        }

        loadingContent.Visible = true;

        AppConfig.PreloadConfig();
        //GetWindow().ContentScaleFactor = OS.HasFeature("mobile") ? 3 : 1;

        //bool hasBanjoAssets = await PegLegResourceManager.ReadAllSources();
        await PegLegResourceManager.FetchAndLoadPackages(majorPackageVersion, minorPackageVersion, (text, prog) => {
            progressLabel.Text = text;
            downloadParticles.Emitting = prog > 0 && prog < 1;
            progressBar.Indeterminate = prog < 0;
            progressBar.Visible = prog <= 1;
            progressBar.Value = prog;
        });
        downloadParticles.Emitting = false;


        await Helpers.WaitForFrame();
        bool showCachingProgress = false;
        var preloadTexturesTask = PegLegResourceManager.PreloadTemplateTextures((text, prog) => {
            if (!showCachingProgress)
                return;
            progressBar.Value = prog;
        });

        if (lite || (OS.HasFeature("editor") && testingScene is not null && !testingRequiresAccount))
        {
            GameAccount.ClearActiveAccount();

            progressBar.Visible = true;
            progressBar.Indeterminate = true;
            progressLabel.Text = "Fetching missions";
            await GameMission.UpdateMissions();

            if (preloadTexturesTask is not null)
            {
                progressBar.Indeterminate = false;
                progressBar.Visible = true;
                progressLabel.Text = "Caching textures";
                showCachingProgress = true;
                await preloadTexturesTask;
            }
            LoadSceneWithPrefs();
            return;
        }

        //migrate notable filter to accounts
        if (AppConfig.TryGet<string>("missions", "notable_filter", out var filter))
        {
            foreach (var a in GameAccount.OwnedAccounts)
            {
                a.SetLocalData("notable_mission_filter", filter);
            }
            AppConfig.Set("missions", "lite_notable_filter", filter);
            AppConfig.Clear("missions", "notable_filter");
        }

        progressBar.Indeterminate = true;
        progressBar.Visible = true;
        progressBar.Value = 0;

        bool hasAccount = false;
        var lastUsedId = AppConfig.Get<string>("account", "lastUsed");
        if (lastUsedId is not null)
        {
            GD.Print("last: " + lastUsedId);
            var lastUsedAccount = GameAccount.GetOrCreateAccount(lastUsedId);
            hasAccount = await lastUsedAccount.SetAsActiveAccount(p => progressLabel.Text = p);
            GD.Print("hasAccount: " + hasAccount);
        }

        //TODO: if more than one account has device details, show account selector
        if (!hasAccount)
        {
            foreach (var a in GameAccount.OwnedAccounts)
            {
                progressLabel.Text = "Login Failed\nWill try another account";
                progressBar.Indeterminate = false;
                await Helpers.WaitForTimer(1.5, t => progressBar.Value = t / 1.5);
                progressBar.Indeterminate = true;
                if (!await a.SetAsActiveAccount(p => progressLabel.Text = p))
                    continue;
                hasAccount = true;
                break;
            }
            if (!hasAccount && GameAccount.OwnedAccounts.Length > 0)
            {
                progressLabel.Text = "Login Failed\nAll accounts attempted";
                progressBar.Indeterminate = false;
                await Helpers.WaitForTimer(1.5, t => progressBar.Value = t / 1.5);
            }
        }

        if (GameAccount.ActiveAccount.isAuthed)
        {
            progressBar.Visible = true;
            progressBar.Indeterminate = true;
            progressLabel.Text = "Fetching missions";
            await GameMission.UpdateMissions();
            progressLabel.Text = "Fetching catalog";
            await GameStorefront.UpdateCatalog();
            progressLabel.Text = "Updating XRay Llamas";
            await GameAccount.ActiveAccount.GenerateXRayLlamaResults();
            progressLabel.Text = "Updating quests";
            await GameAccount.ActiveAccount.ClientQuestLoginCampaign();
            await GameAccount.ActiveAccount.ClientQuestLoginAthena();
        }

        if (preloadTexturesTask is not null)
        {
            progressBar.Indeterminate = false;
            progressBar.Visible = true;
            progressLabel.Text = "Caching textures";
            showCachingProgress = true;
            await preloadTexturesTask;
        }

        LoadSceneWithPrefs();
    }

    void LoadSceneWithPrefs()
    {
        loadingContent.Visible = false;
        if (!AppConfig.TryGet("core", "litemode", out bool lite))
        {
            liteContent.Visible = true;
            return;
        }
        LoadScene(lite);
    }

    public void SetLiteMode(bool lite)
    {
        liteContent.Visible = false;
        AppConfig.Set("core", "litemode", lite);
        Initialise();
    }

    async void LoadScene(bool lite)
    {
        loadingContent.Visible = true;
        Window window = GetWindow();
        curtain.Visible = true;
        await Helpers.WaitForFrame();
        window.Size = windowSize;
        window.ContentScaleSize = windowSize;
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
        GetWindow().ContentScaleFactor = 1;

        
        PackedScene targetScene = null;
        if (OS.HasFeature("mobile") && !AppConfig.Get("core", "disable_mobile", false))
        {
            if (lite)
                targetScene = mobileLiteInterface;
            else if (!GameAccount.ActiveAccount.isOwned)
                targetScene = desktopOnboarding;
            else
                targetScene = mobileInterface;
        }
        else
        {
            if (lite)
                targetScene = liteInterface;
            else if (!GameAccount.ActiveAccount.isOwned)
                targetScene = desktopOnboarding;
            else if (OS.HasFeature("editor") && testingScene is not null)
                targetScene = testingScene;
            else if (UseShareMenu)
                targetScene = shareMenu;
            else
                targetScene = desktopInterface;
        }

        if(targetScene is not null)
            GetTree().ChangeSceneToPacked(targetScene);
        
        /*
        string targetUID = null;

        if (lite)
            targetUID = liteInterfaceUid;
        else if (!GameAccount.ActiveAccount.isOwned)
            targetUID = desktopOnboardingUid;
        else if (OS.HasFeature("editor") && testingScene is not null)
            targetUID = testingSceneUid;
        else if (UseShareMenu)
            targetUID = shareMenuUid;
        else
            targetUID = desktopInterfaceUid;

        if (targetUID is not null)
            await GetTree().ChangeSceneAsync(targetUID);
        */
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
