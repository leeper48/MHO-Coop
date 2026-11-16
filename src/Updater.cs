// Self-update from GitHub Releases, built the way MHO Extended Mod Manager does it:
//  - ask GitHub which releases exist (no account or token, nothing about the user is sent);
//  - take the newest one that is not a draft or pre-release and carries a signed zip;
//  - download the zip, check its SHA-256 and the developer's signature before touching anything;
//  - replace the program's files one by one, keeping each old file as <name>.old, and put everything
//    back if any step fails;
//  - restart; the .old files are removed at the next start.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MhoCoop
{
    internal sealed class ReleaseInfo
    {
        public Version Version;
        public string Notes = "";
        public string PageUrl = "";
        public string ZipUrl = "";
        public string ShaUrl = "";
        public string SigUrl = "";
    }

    internal static class Updater
    {
        public const string Repo = "leeper48/MHO-Coop";
        public const string TagPrefix = "v";                  // tags look like v1.2.0
        public const string RestartVariable = "MHOCOOP_RESTART";
        private const string ProgramName = "MhoCoop.exe";     // the program's name inside a release zip
        private const string FeedVariable = "MHOCOOP_UPDATE_FEED";   // tests only: a folder holding releases.json and the files

        // This program's version, three parts: 1.2.0
        public static Version Current
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        private static string Feed
        {
            get { return Environment.GetEnvironmentVariable(FeedVariable) ?? ""; }
        }

        // Updates are possible only in a build that carries the developer's public key.
        public static bool Available { get { return ReleaseSigning.HasKey; } }

        // Only https links on GitHub's own hosts are ever followed.
        public static bool Trusted(string url)
        {
            if (Feed.Length > 0) return url.IndexOfAny(new char[] { '/', '\\', ':' }) < 0;   // tests: a plain file name in the feed folder
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != Uri.UriSchemeHttps) return false;
            string host = u.Host.ToLowerInvariant();
            return host == "github.com" || host == "api.github.com" || host.EndsWith(".githubusercontent.com");
        }

        // ---- talking to GitHub ----
        private static WebClient NewClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;   // older defaults fail against GitHub
            WebClient w = new WebClient();
            w.Encoding = Encoding.UTF8;
            w.Headers[HttpRequestHeader.UserAgent] = "MHO-Coop/" + Current;       // GitHub rejects requests without one
            return w;
        }

        private static string ReadText(string url)
        {
            if (Feed.Length > 0) return File.ReadAllText(Path.Combine(Feed, url), Encoding.UTF8);
            using (WebClient w = NewClient()) return w.DownloadString(url);
        }

        private static void Download(string url, string toFile)
        {
            if (Feed.Length > 0) { File.Copy(Path.Combine(Feed, url), toFile, true); return; }
            using (WebClient w = NewClient()) w.DownloadFile(url, toFile);
        }

        // The newest usable release, whatever its version (the caller compares it with Current).
        // Returns null with error "" when there is none, or null with a reason when the check failed.
        public static ReleaseInfo Check(out string error)
        {
            error = "";
            if (!Available) { error = "This build cannot verify updates, so it does not look for them."; return null; }
            try
            {
                string json = Feed.Length > 0
                    ? File.ReadAllText(Path.Combine(Feed, "releases.json"), Encoding.UTF8)
                    : ReadText("https://api.github.com/repos/" + Repo + "/releases?per_page=30");

                JavaScriptSerializer reader = new JavaScriptSerializer();
                reader.MaxJsonLength = int.MaxValue;
                object[] releases = reader.DeserializeObject(json) as object[];
                if (releases == null) { error = "GitHub's answer was not a list of releases."; return null; }

                ReleaseInfo best = null;
                foreach (object item in releases)
                {
                    Dictionary<string, object> r = item as Dictionary<string, object>;
                    if (r == null || Flag(r, "draft") || Flag(r, "prerelease")) continue;
                    string tag = Text(r, "tag_name");
                    Version version;
                    if (!tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase) ||
                        !Version.TryParse(tag.Substring(TagPrefix.Length), out version)) continue;

                    ReleaseInfo info = new ReleaseInfo();
                    info.Version = Three(version);
                    info.Notes = Text(r, "body");
                    info.PageUrl = Text(r, "html_url");
                    object[] assets = r.ContainsKey("assets") ? r["assets"] as object[] : null;
                    if (assets == null) continue;
                    foreach (object a in assets)
                    {
                        Dictionary<string, object> asset = a as Dictionary<string, object>;
                        if (asset == null) continue;
                        string name = Text(asset, "name").ToLowerInvariant();
                        string link = Text(asset, "browser_download_url");
                        if (name.EndsWith(".zip.sha256")) info.ShaUrl = link;
                        else if (name.EndsWith(".zip.sig")) info.SigUrl = link;
                        else if (name.EndsWith(".zip")) info.ZipUrl = link;
                    }
                    // An unsigned release is never offered.
                    if (info.ZipUrl.Length == 0 || info.ShaUrl.Length == 0 || info.SigUrl.Length == 0) continue;
                    if (!Trusted(info.ZipUrl) || !Trusted(info.ShaUrl) || !Trusted(info.SigUrl)) continue;
                    if (best == null || info.Version > best.Version) best = info;
                }
                return best;
            }
            catch (WebException ex) { error = ex.Message; }
            catch (IOException ex) { error = ex.Message; }
            catch (ArgumentException ex) { error = "GitHub's answer could not be read: " + ex.Message; }
            catch (InvalidOperationException ex) { error = "GitHub's answer could not be read: " + ex.Message; }
            return null;
        }

        private static string Text(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }

        private static bool Flag(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v is bool && (bool)v;
        }

        private static Version Three(Version v) { return new Version(v.Major, v.Minor, Math.Max(v.Build, 0)); }

        // ---- installing ----
        // Downloads, checks and installs a release. Returns false with a reason; in that case the program on
        // disk is exactly as it was.
        public static bool Install(ReleaseInfo release, Action<string> say, out string error)
        {
            error = "";
            string work = Path.Combine(Config.DataDir, "update", release.Version.ToString());
            try
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);

                say("Downloading version " + release.Version + "...");
                string zip = Path.Combine(work, "release.zip");
                Download(release.ZipUrl, zip);
                string expectedHash = FirstWord(ReadText(release.ShaUrl));
                string signature = ReadText(release.SigUrl);

                byte[] data = File.ReadAllBytes(zip);
                string hash;
                using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
                if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    error = "The download arrived damaged (its checksum does not match). Nothing was changed.";
                    return false;
                }
                if (!ReleaseSigning.Verify(data, signature))
                {
                    error = "The download is not signed by the developer. Nothing was changed.";
                    return false;
                }

                say("Unpacking...");
                string files = Path.Combine(work, "files");
                ZipFile.ExtractToDirectory(zip, files);
                string[] top = Directory.GetFileSystemEntries(files);
                if (top.Length == 1 && Directory.Exists(top[0])) files = top[0];   // a zip holding one folder

                string newProgram = Path.Combine(files, ProgramName);
                if (!File.Exists(newProgram))
                {
                    error = "The release does not contain " + ProgramName + ". Nothing was changed.";
                    return false;
                }
                Version inside;
                string stated = FileVersionInfo.GetVersionInfo(newProgram).ProductVersion ?? "";
                if (!Version.TryParse(stated, out inside) || Three(inside) != release.Version)
                {
                    error = "The release says " + release.Version + " but its program is " + stated + ". Nothing was changed.";
                    return false;
                }

                say("Installing...");
                if (!Swap(files, out error)) return false;
                say("Updated to " + release.Version + ".");
                return true;
            }
            catch (WebException ex) { error = "The download failed: " + ex.Message + " Nothing was changed."; }
            catch (IOException ex) { error = ex.Message + " Nothing was changed."; }
            catch (UnauthorizedAccessException ex) { error = ex.Message + " Nothing was changed."; }
            catch (InvalidDataException ex) { error = "The download could not be unpacked: " + ex.Message + " Nothing was changed."; }
            return false;
        }

        private static string FirstWord(string text)
        {
            string[] parts = text.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : "";
        }

        // Replaces the program's files with the ones in newFiles. Windows lets a running program be renamed,
        // so each old file becomes <name>.old and the new one takes its place. If any step fails, every step
        // already done is undone in reverse.
        private static bool Swap(string newFiles, out string error)
        {
            error = "";
            string home = Path.GetDirectoryName(Application.ExecutablePath);
            List<string[]> done = new List<string[]>();          // { target, "1" if the old file was moved aside }
            try
            {
                foreach (string source in Directory.GetFiles(newFiles, "*", SearchOption.AllDirectories))
                {
                    string relative = source.Substring(newFiles.Length).TrimStart('\\', '/');
                    if (IsUserData(relative)) continue;
                    // The program itself may have been renamed by the user: replace whatever is running.
                    string target = string.Equals(relative, ProgramName, StringComparison.OrdinalIgnoreCase)
                        ? Application.ExecutablePath
                        : Path.Combine(home, relative);

                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string old = target + ".old";
                    string[] step = { target, "0" };
                    if (File.Exists(target))
                    {
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(target, old);
                        step[1] = "1";
                    }
                    done.Add(step);
                    File.Copy(source, target);
                }
                return true;
            }
            catch (IOException ex) { error = ex.Message; }
            catch (UnauthorizedAccessException ex) { error = ex.Message; }

            for (int i = done.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (File.Exists(done[i][0])) File.Delete(done[i][0]);
                    if (done[i][1] == "1") File.Move(done[i][0] + ".old", done[i][0]);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            error += " The old version was put back.";
            return false;
        }

        // Settings and logs are the user's: an update never writes over them.
        private static bool IsUserData(string relative)
        {
            string lower = relative.ToLowerInvariant().Replace('/', '\\');
            return lower == "mhocoop.ini" || lower == "mhocoop.log" || lower.StartsWith("update\\");
        }

        // Starts the new program. The flag tells it to wait for this copy to close first.
        public static void Restart()
        {
            ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath);
            start.UseShellExecute = false;
            start.EnvironmentVariables[RestartVariable] = "1";
            Process.Start(start);
        }

        // At every start: remove what the last update left behind. Files still in use are left for next time.
        public static void CleanUp()
        {
            string home = Path.GetDirectoryName(Application.ExecutablePath);
            string[] leftovers = { Application.ExecutablePath + ".old", Path.Combine(home, "README.txt.old") };
            foreach (string file in leftovers)
            {
                try { if (File.Exists(file)) File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            try
            {
                string work = Path.Combine(Config.DataDir, "update");
                if (Directory.Exists(work)) Directory.Delete(work, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
