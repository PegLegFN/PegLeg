using System;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;
using IODir = System.IO.Directory;
using WixSharp;
using WixSharp.Forms;
using System.Text.RegularExpressions;
using WixSharp.CommonTasks;

namespace WixSharpTesting
{
    public class Program
    {
        static void Main()
        {
			var currentDir = IODir.GetCurrentDirectory();
			string projectRoot = IOPath.Combine(currentDir, @"../");

            var gdProjSettings = IOFile.ReadAllText(IOPath.Combine(projectRoot, "project.godot"));
            var match = Regex.Match(gdProjSettings, @"config/version=""(\d+\.\d+\.\d+)""", RegexOptions.Multiline);
            
            if (!match.Success)
                throw new Exception("No Version Match");

            var vText = match.Groups[1].Value;
            var vNums = vText.Split('.');
            if (vNums.Length != 3)
                throw new Exception("Incorrect amount of Version numbers");

            Console.WriteLine($"PegLeg Version: {vText}");

            Version appVersion = new Version(int.Parse(vNums[0]), int.Parse(vNums[1]), int.Parse(vNums[2]));//betas increment the version by 1, patch releases increment by 100 and reset betas
             
            bool isBeta = appVersion.Build % 100 > 0;
            string icoName = isBeta ? "PegLegBeta.ico" : "PegLeg.ico"; 
            string projectName = isBeta ? "PegLeg Beta" : "PegLeg";
            string spacelessName = isBeta ? "PegLegBeta" : "PegLeg";
            string projectExportRoot = IOPath.Combine(projectRoot, "Builds", "Windows", isBeta ? "Beta" : "Release", "Build");
            
            var project = 
                new ManagedProject(projectName,
                    new Dir($@"%ProgramFiles%\Tomatech\{spacelessName}",
                        new File(new Id("PegLegExe"), IOPath.Combine(projectExportRoot, "PegLeg.exe"),
                            new FileAssociation("pegleg", "application/pegleg", "open", "\"%1\"")
                            {
                                Description = "PegLeg Portable Data"
                            },
                            new FileAssociation("fnprofile", "application/fnprofile", "open", "\"%1\"")
                            {
                                Description = "Fortnite Profile Data, handled by PegLeg"
                            },
                            new FileShortcut(projectName, "INSTALLDIR") //INSTALLDIR is the ID of "%ProgramFiles%\My Company\My Product"
                        ),
                        new File(IOPath.Combine(projectExportRoot, "PegLeg.console.exe")),
                        new Dir("./data_PegLegGD_windows_x86_64",
                            new Files(IOPath.Combine(projectExportRoot, "data_PegLegGD_windows_x86_64\\*"))
                        )
                    ),
                    new Dir(@"%ProgramMenu%",
                        new Dir("PegLeg",
                            new ExeFileShortcut(projectName, IOPath.Combine("[INSTALLDIR]", "PegLeg.exe"), "")
                            {
                                Description = "Unofficial companion app for Fortnite, primarily its Save The World mode",
                                WorkingDirectory = "[INSTALLDIR]"
                            }
                        )
                    ),
                    new InstalledFileAction("PegLegExe", "")
                    {
                        Id = "Start_App",
                        Step = Step.InstallFinalize,
                        When = When.After,
                        Return = Return.asyncNoWait,
                        Execute = Execute.immediate,
                        Impersonate = true,
                        Condition = Condition.Installed
                    }
                );

            

            Guid standardGUID = new Guid("c9e9f922-7506-4ac2-8d0d-d490407bad6c");
            Guid betaGUID = new Guid("cd29c24a-c157-4935-a88f-1285a0b06719");
            project.GUID = isBeta ? betaGUID : standardGUID;
            project.Version = appVersion;

            project.MajorUpgrade = new MajorUpgrade()
            {
                Schedule = UpgradeSchedule.afterInstallInitialize,
                DowngradeErrorMessage = "A later version of PegLeg is already installed. Uninstall it and restart the installer to downgrade",
            };

            project.ManagedUI = new ManagedUI();

            project.ManagedUI.InstallDialogs.Add(Dialogs.Welcome)
                                            .Add(Dialogs.InstallDir)
                                            .Add(Dialogs.Progress)
                                            .Add(Dialogs.Exit);

            project.ManagedUI.ModifyDialogs.Add(Dialogs.MaintenanceType)
                                           .Add(Dialogs.Progress)
                                           .Add(Dialogs.Exit);
             
            project.BackgroundImage = "Media/PLInstallBG3.png";
            project.BannerImage = "Media/PLInstallBanner64.png";

            project.SetProductIcon(icoName);

            project.OutFileName = spacelessName+"-Windows";
            project.OutDir = IOPath.GetFullPath(IOPath.Combine(projectExportRoot, "../"));

            project.BuildMsi();
        }
    }
}