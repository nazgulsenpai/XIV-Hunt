﻿using FFXIV_GameSense.Properties;
using FFXIV_GameSense.UI;
using Splat;
using Squirrel;
using System;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FFXIV_GameSense
{
    public class Updater
    {
        public static Task Create(CancellationToken token)
        {
            return new Task(() => { CheckAndApplyUpdates(); }, token, TaskCreationOptions.LongRunning);
        }

        private static void CheckAndApplyUpdates()
        {
            bool shouldRestart = false;
            try
            {
                using (var mgr = new UpdateManager(Settings.Default.UpdateLocation))
                {
                    var updateInfo = mgr.CheckForUpdate().Result;
                    if (updateInfo.ReleasesToApply.Any())
                    {
                        BackupSettings();
                        shouldRestart = true;
                        DeleteOldVersions();
                        mgr.UpdateApp().Wait();
                    }
                }
            }
            catch (Exception ex) { App.WriteExceptionToErrorFile(ex); }
            if (shouldRestart)
                UpdateManager.RestartApp();
        }

        internal static void OnAppUpdate()
        {
            using (var mgr = new UpdateManager(Settings.Default.UpdateLocation))
            {
                mgr.RemoveUninstallerRegistryEntry();
                mgr.CreateUninstallerRegistryEntry();
            }
        }

        internal static void OnFirstRun()
        {
            BackupLastStandaloneSettings();
            RestoreSettings();
            Settings.Default.Reload();
        }

        /// <summary>
        /// Make a backup of our settings.
        /// Used to persist settings across updates.
        /// </summary>
        private static void BackupSettings()
        {
            string settingsFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
            string destination = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\..\\last.config";
            File.Copy(settingsFile, destination, true);
        }

        private static void BackupLastStandaloneSettings()
        {
            string gsDir = Directory.GetParent(Path.GetDirectoryName(ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath)).Parent.FullName;
            if (!Directory.Exists(gsDir))
                return;
            DirectoryInfo di = new DirectoryInfo(gsDir);
            string mostrecent = di.EnumerateDirectories().OrderByDescending(x => x.CreationTime).FirstOrDefault().FullName;
            di = new DirectoryInfo(mostrecent);
            string settings = Path.Combine(di.EnumerateDirectories().OrderByDescending(x => x.CreationTime).FirstOrDefault().FullName, "user.config");
            if (File.Exists(settings))
            {
                string destination = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\..\\last.config";
                File.Copy(settings, destination, true);
            }
        }

        internal static void RestartApp()
        {
            Settings.Default.Save();
            if(!App.IsSquirrelInstall())
            {
                System.Windows.Forms.Application.Restart();
                Application.Current.Shutdown();
            }
            else
                UpdateManager.RestartApp();
        }

        private static void DeleteOldVersions()
        {
            DirectoryInfo appDir = new DirectoryInfo(Assembly.GetExecutingAssembly().Location).Parent.Parent;
            var olderDirs = appDir.EnumerateDirectories("app-*").OrderByDescending(x => x.CreationTimeUtc).Skip(2);
            foreach (DirectoryInfo oldDir in olderDirs)
                try
                {
                    oldDir.Delete(true);
                }
                catch { }
            var packagesDir = Path.Combine(appDir.FullName, "packages");
            if (!Directory.Exists(packagesDir))
                return;
            DirectoryInfo packDir = new DirectoryInfo(packagesDir);
            var olderPackages = packDir.EnumerateFiles("*.nupkg").OrderByDescending(x => x.CreationTimeUtc).Skip(4);
            foreach (var oldPack in olderPackages)
                try
                {
                    oldPack.Delete();
                }
                catch { }
        }

        internal static void RestoreSettings()
        {
            //Restore settings after application update            
            string destFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
            string sourceFile = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\..\\last.config";
            // Check for settings that may be needed to restore
            if (!File.Exists(sourceFile))
            {
                return;
            }
            // Create directory as needed
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destFile));
            }
            catch { }
            // Copy backup file in place 
            try
            {
                File.Copy(sourceFile, destFile, true);
            }
            catch { }
            // Delete backup file
            try
            {
                File.Delete(sourceFile);
            }
            catch { }
        }
    }

    public class Logger : ILogger
    {
        public LogLevel Level { get; set; } = LogLevel.Info;
        private readonly LogView LogView;

        public Logger(LogView lv) => LogView = lv;

        public void Write(string message, LogLevel level) => LogView.AddLogLine(message.Remove(0, nameof(LogHost).Length), level);

        public void Write([Localizable(false)] string message, [Localizable(false)] Type type, LogLevel logLevel)
        {
            throw new NotImplementedException();
        }
    }
}
