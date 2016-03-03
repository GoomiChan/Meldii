﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using Meldii.AddonProviders;
using Meldii.DataStructures;
using Microsoft.Win32;

namespace Meldii
{
    public class Statics
    {
        public static string UpdateCheckUrl = "https://raw.githubusercontent.com/GoomiChan/Meldii/master/Release/version.txt";
        public static string UpdateExeUrl = "https://raw.githubusercontent.com/GoomiChan/Meldii/master/Release/Meldii.exe";
        public static string UpdaterName = "Meldii.Updater.exe";
        public static string LaunchArgs = "";
        public static FirefallPatchData FirefallPatchData = null;
        private static Regex verStringRegex = new Regex(@"[^\d.]");

        public static string MeldiiAppData = "";
        public static string SettingsPath = "";
        public static string AddonsFolder = "";
        public static bool IsFirstRun = true;
        public static string DefaultAddonLocation = "gui\\components\\MainUI\\Addons";
        public static string AddonBackupPostfix = ".zip_backup.zip";
        public static string ModDataStoreReltivePath = "system\\melder_addons";
        public static string[] BlackListedPaths = 
        {
            "\\bin\\",
            "\\gui\\UISets\\",
            "\\gui\\components\\LoginUI\\"
        };
        public static string MelderProtcolRegex = "melder://(.*?)/(.*?):(.*)";

        public static AddonProviderType OneClickInstallProvider;
        public static string OneClickAddonToInstall = null; // the url of a forum attachment to install
        public static bool ShouldUpdate = false;

        public static AddonManager AddonManager = null;

        public static void InitStaticData()
        {
            MeldiiAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Meldii");
            SettingsPath = Path.Combine(MeldiiAppData, "settings.json");

            if (!Directory.Exists(MeldiiAppData))
            {
                Directory.CreateDirectory(MeldiiAppData);
            }

            IsFirstRun = !File.Exists(SettingsPath);

            if (Statics.IsFirstRun)
            {
                MeldiiSettings.Self.FirefallInstallPath = GetFirefallInstallPath();
                MeldiiSettings.Self.AddonLibaryPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + Properties.Settings.Default.AddonStorage;
                MeldiiSettings.Self.Save();
            }

            AddonsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Firefall\Addons");
        }

        public static string FixPathSlashes(string str)
        {
            return str.Replace("/", "\\");
        }

        // Get the base path to install the addon at
        public static string GetPathForMod(string addonDest)
        {
            if (addonDest != null)
            {
                return Path.Combine(new string[] 
                {
                    MeldiiSettings.Self.FirefallInstallPath,
                    "system",
                    addonDest
                });
            }
            else
            {
                return GetFirefallSystemDir();
            }
        }

        public static string GetFirefallSystemDir()
        {
            return Path.Combine(new string[] 
            {
                MeldiiSettings.Self.FirefallInstallPath,
                "system"
            });
        }

        public static bool IsFirefallInstallValid(string path)
        {
            string fp = Path.Combine(path, "system", "bin", "FirefallClient.exe");
            return File.Exists(fp);

        }

        public static bool IsFirefallInstallLauncher()
        {
            string install = String.Empty;
            bool launcher = false;

            var view = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using (var firefall = view.OpenSubKey(@"Software\Red 5 Studios\Firefall_Beta"))
            {
                if (firefall != null)
                {
                    install = Path.GetFullPath((string)firefall.GetValue("InstallLocation", String.Empty)).ToLowerInvariant();
                    if (!String.IsNullOrWhiteSpace(install))
                        launcher = true;
                }
            }

            if (!launcher)
                return false;

            // Find our Steam install location to see if our launcher might be a Steam version.
            // This should handle most cases.  Steam can have multiple library locations now, but I
            // have no idea how to test for that.
            view = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using (var steam = view.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (steam != null)
                {
                    string steamInstall = (string)steam.GetValue("SteamPath", String.Empty);
                    if (!String.IsNullOrWhiteSpace(steamInstall))
                    {
                        steamInstall = Path.GetFullPath(steamInstall).ToLowerInvariant();
                        if (!String.IsNullOrEmpty(steamInstall) && install.StartsWith(steamInstall))
                            launcher = false;
                    }
                }
            }

            return launcher;
        }

        public static bool IsFirefallInstallSteam()
        {
            var view = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using (var firefall = view.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 227700")) // Firefall app id
            {
                return firefall != null;
            }
        }

        // Get the path to the addons files backup
        public static string GetBackupPathForMod(string addonName)
        {
            return Path.Combine(new string[] 
            {
                MeldiiSettings.Self.FirefallInstallPath,
                ModDataStoreReltivePath,
                addonName
            }) + AddonBackupPostfix;
        }

        public static bool IsPathSafe(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (fullPath.Contains(AddonsFolder) || fullPath.Contains(MeldiiSettings.Self.FirefallInstallPath)) // Only allow under the addons location or the game install
            {
                string relPath = fullPath.Replace(AddonsFolder, "");
                relPath = relPath.Replace(MeldiiSettings.Self.FirefallInstallPath, "");

                foreach (string badii in BlackListedPaths)
                {
                    if (relPath.StartsWith(AddonsFolder))
                    {
                        Debug.WriteLine("IsPathSafe check failed on: {0}", relPath);
                        return false;
                    }
                }

                return true;
            }
            else
            {
                Debug.WriteLine("IsPathSafe check failed on: {0} is outside the game install or the addons folder", fullPath);
                return false;
            }
        }

        // http://stackoverflow.com/a/5238116
        public static bool IsFileLocked(FileInfo file)
        {
            FileStream stream = null;
            try
            {
                stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return true;
            }
            finally
            {
                if (stream != null)
                    stream.Close();
            }
            return false;
        }

        public static void EnableMelderProtocol()
        {
            // Set some process start info.
            // We want to launch as administrator so we can set some registry keys.
            ProcessStartInfo proc = new ProcessStartInfo();
            proc.UseShellExecute = true;
            proc.WorkingDirectory = Environment.CurrentDirectory;
            proc.FileName = System.Reflection.Assembly.GetEntryAssembly().Location;
            proc.Arguments = "--enable-one-click";
            proc.CreateNoWindow = false;
            proc.Verb = "runas";

            // Create the process object and bind some events.
            Process p = new Process();
            p.EnableRaisingEvents = true;
            p.StartInfo = proc;
            p.Exited += (sender, e) =>
            {
                // Check for failure.  If so, reset our settings.
                if (p.ExitCode == 1)
                {
                    MainWindow.Self.ViewModel.SettingsView.IsMelderProtocolEnabled = false;
                    MeldiiSettings.Self.IsMelderProtocolEnabled = false;
                    MeldiiSettings.Self.Save();
                }
            };

            // Kick it off!
            p.Start();
        }

        public static void DisableMelderProtocol()
        {
            // Set some process start info.
            // We want to launch as administrator so we can set some registry keys.
            ProcessStartInfo proc = new ProcessStartInfo();
            proc.UseShellExecute = true;
            proc.WorkingDirectory = Environment.CurrentDirectory;
            proc.FileName = System.Reflection.Assembly.GetEntryAssembly().Location;
            proc.Arguments = "--disable-one-click";
            proc.CreateNoWindow = false;
            proc.Verb = "runas";

            // Create the process object and bind some events.
            Process p = new Process();
            p.EnableRaisingEvents = true;
            p.StartInfo = proc;
            p.Exited += (sender, e) =>
            {
                // Check for failure.  If so, reset our settings.
                if (p.ExitCode == 1)
                {
                    MainWindow.Self.ViewModel.SettingsView.IsMelderProtocolEnabled = true;
                    MeldiiSettings.Self.IsMelderProtocolEnabled = true;
                    MeldiiSettings.Self.Save();
                }
            };

            // Kick it off!
            p.Start();
        }

        public static FirefallPatchData GetFirefallPatchData()
        {
            if (FirefallPatchData == null)
            {
                using (WebClient wc = new WebClient())
                {
                    try
                    {
                        using (Stream s = GenerateStreamFromString(wc.DownloadString("http://operator.firefall.com/api/v1/products/Firefall_Beta")))
                        {
                            FirefallPatchData = FirefallPatchData.Create(s);
                        }
                    }
                    catch (System.Net.WebException)
                    {
                        FirefallPatchData = FirefallPatchData.CreateError();
                    }
                }
            }

            return FirefallPatchData;
        }

        public static Stream GenerateStreamFromString(string s)
        {
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        public static string GetFirefallInstallPath()
        {
            string ffpath = String.Empty;

            using (var view = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            {
                using (var firefall = view.OpenSubKey(@"Software\Red 5 Studios\Firefall_Test"))
                {
                    if (firefall != null)
                    {
                        // Get the install location and unbox.
                        var loc = firefall.GetValue("InstallLocation");
                        if (loc != null) ffpath = (string)loc;
                    }
                }
            }

            // Kinda hackish but it works ~freak
            if (String.IsNullOrEmpty(ffpath))
            {
                using (var view = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Default))
                {
                    using (var firefall = view.OpenSubKey(@"firefall\shell\open\command"))
                    {
                        string path = firefall == null ? String.Empty : (string)firefall.GetValue("");
                        if (!String.IsNullOrEmpty(path))
                        {
                            path = path.Split(new string[] { "\"firefall\" /D\"" }, StringSplitOptions.None)[1].Split('"')[0].Trim();
                            path = path.Substring(0, path.Length - 11);
                            ffpath = path;
                        }
                    }
                }
            }

            return ffpath;
        }

        public static bool NeedAdmin()
        {
            try
            {
                if (IsFirefallInstallValid(MeldiiSettings.Self.FirefallInstallPath))
                {
                    string path = Path.Combine(MeldiiSettings.Self.FirefallInstallPath, "Meldii Admin Check.test");
                    File.WriteAllText(path, "Medlii admin check");
                    File.Delete(path);
                    return false;
                }
            }
            catch (Exception)
            {
                return true;
            }
            return false;
        }

        public static void RunAsAdmin(string args)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("Meldii.exe", args);
                info.Verb = "runas";
                Process.Start(info);

                App.Current.Dispatcher.Invoke((Action)delegate()
                {
                    Application.Current.Shutdown();
                });
            }
            catch { }
        }

        public static string CleanVersionString(string verStr)
        {
            string str = verStringRegex.Replace(verStr, "");

            if (!str.Contains("."))
                str += ".0";

            Debug.WriteLine("CleanVersionString: " + verStr + " " + str);

            return str;
        }

        public static bool CheckIfFileIsReadOnly(string filePath)
        {
            System.IO.FileInfo fi = new System.IO.FileInfo(filePath);

            return (fi.Attributes == System.IO.FileAttributes.ReadOnly);
        }
    }

}
