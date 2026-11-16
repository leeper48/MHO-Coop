// Settings, kept in MhoCoop.ini: where the game and ProtoInput are, the server, the gamepads and timings.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace MhoCoop
{
    internal sealed class Config
    {
        public const string DefaultServer = "mhtahiti.com/SiteConfig.xml";
        public const string DefaultExtraArgs = "-robocopy -nosteam";
        private const string IniName = "MhoCoop.ini";
        private const string GameExeName = "MarvelHeroesOmega.exe";

        public string GameExe = "";
        public string ProtoDir = "";
        public string Server = DefaultServer;
        public string LeftArgs = DefaultExtraArgs;     // extra options for the left game
        public string RightArgs = DefaultExtraArgs;    // and for the right game
        public int LeftPad = 1;
        public int RightPad = 2;
        public int LoadWait = 18;                  // seconds given to each game to load before the next step
        public bool Borderless = true;
        public bool WheelPerWindow = true;
        public bool CheckUpdates = false;          // look for a new version at start (asked once, see Program)
        public bool UpdateCheckAsked = false;
        public DateTime LastUpdateCheck = DateTime.MinValue;   // UTC
        public string LatestKnownVersion = "";     // so the menu can show the update button without asking GitHub again
        public string SkipVersion = "";

        // ---- where things are kept ----
        public static string ExeDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }

        private static string dataDir;

        // Normally %AppData%\MhoCoop. If a MhoCoop.ini sits next to the exe, that folder is used instead (portable).
        public static string DataDir
        {
            get
            {
                if (dataDir == null)
                {
                    if (File.Exists(Path.Combine(ExeDir, IniName))) dataDir = ExeDir;
                    else dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MhoCoop");
                }
                return dataDir;
            }
        }

        public static string IniPath { get { return Path.Combine(DataDir, IniName); } }
        public static string LogPath { get { return Path.Combine(DataDir, "MhoCoop.log"); } }

        // ---- load / save ----
        public static Config Load()
        {
            Config c = new Config();
            string path = IniPath;
            if (File.Exists(path))
            {
                string section = "";
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';') continue;
                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (section.Equals("General", StringComparison.OrdinalIgnoreCase))
                        c.SetGeneral(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
                }
            }
            c.AutoDetect();
            return c;
        }

        private void SetGeneral(string key, string value)
        {
            switch (key.ToLowerInvariant())
            {
                case "gameexe": GameExe = value; break;
                case "protodir": ProtoDir = value; break;
                case "server": Server = value; break;
                case "extraargs": LeftArgs = value; RightArgs = value; break;   // 1.1.0 had one box for both games
                case "leftargs": LeftArgs = value; break;
                case "rightargs": RightArgs = value; break;
                case "leftpad": LeftPad = Clamp(value, 1, 4, 1); break;
                case "rightpad": RightPad = Clamp(value, 1, 4, 2); break;
                case "loadwait": LoadWait = Clamp(value, 3, 180, 18); break;
                case "borderless": Borderless = value != "0"; break;
                case "wheelperwindow": WheelPerWindow = value != "0"; break;
                case "checkupdates": CheckUpdates = value == "1"; break;
                case "updatecheckasked": UpdateCheckAsked = value == "1"; break;
                case "lastupdatecheck":
                    long ticks;
                    if (long.TryParse(value, out ticks) && ticks > 0 && ticks < DateTime.MaxValue.Ticks) LastUpdateCheck = new DateTime(ticks, DateTimeKind.Utc);
                    break;
                case "latestknownversion": LatestKnownVersion = value; break;
                case "skipversion": SkipVersion = value; break;
            }
        }

        private static int Clamp(string text, int min, int max, int fallback)
        {
            int n;
            if (!int.TryParse(text, out n)) return fallback;
            return Math.Max(min, Math.Min(max, n));
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; MHO Couch Co-op settings.");
            sb.AppendLine("[General]");
            sb.AppendLine("GameExe=" + GameExe);
            sb.AppendLine("ProtoDir=" + ProtoDir);
            sb.AppendLine("Server=" + Server);
            sb.AppendLine("LeftArgs=" + LeftArgs);
            sb.AppendLine("RightArgs=" + RightArgs);
            sb.AppendLine("LeftPad=" + LeftPad);
            sb.AppendLine("RightPad=" + RightPad);
            sb.AppendLine("LoadWait=" + LoadWait);
            sb.AppendLine("Borderless=" + (Borderless ? "1" : "0"));
            sb.AppendLine("WheelPerWindow=" + (WheelPerWindow ? "1" : "0"));
            sb.AppendLine("CheckUpdates=" + (CheckUpdates ? "1" : "0"));
            sb.AppendLine("UpdateCheckAsked=" + (UpdateCheckAsked ? "1" : "0"));
            sb.AppendLine("LastUpdateCheck=" + LastUpdateCheck.Ticks);
            sb.AppendLine("LatestKnownVersion=" + LatestKnownVersion);
            sb.AppendLine("SkipVersion=" + SkipVersion);
            Directory.CreateDirectory(DataDir);
            string temp = IniPath + ".tmp";
            File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(IniPath)) File.Delete(IniPath);
            File.Move(temp, IniPath);
        }

        // ---- first-run guesses ----
        private void AutoDetect()
        {
            if (GameExe.Length == 0)
            {
                const string tail = @"steamapps\common\Marvel Heroes\UnrealEngine3\Binaries\Win64\" + GameExeName;
                List<string> roots = new List<string>();
                roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
                try
                {
                    object steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null);
                    if (steam != null) roots.Add(steam.ToString().Replace('/', '\\'));
                }
                catch (Exception) { }
                foreach (string root in roots)
                {
                    string candidate = Path.Combine(root, tail);
                    if (File.Exists(candidate)) { GameExe = candidate; break; }
                }
            }

            if (ProtoDir.Length == 0)
            {
                List<string> places = new List<string>();
                places.Add(ExeDir);
                try
                {
                    places.AddRange(Directory.GetDirectories(ExeDir, "ProtoInput*"));
                    DirectoryInfo parent = Directory.GetParent(ExeDir.TrimEnd('\\'));
                    if (parent != null) places.AddRange(Directory.GetDirectories(parent.FullName, "ProtoInput*"));
                }
                catch (Exception) { }
                foreach (string place in places)
                    if (Proto.FolderLooksValid(place)) { ProtoDir = place; break; }
            }
        }
    }
}
