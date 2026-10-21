using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

// Search only known locations. Do not enumerate entire drives or follow network paths.
internal static class GameDiscovery
{
    private const int MaximumMetadataBytes = 1024 * 1024;
    private static readonly string[] GameFolderNames = { "DawnOfMan", "Dawn of Man" };

    internal static IList<string> FindCandidates(string installerDirectory)
    {
        List<string> locations = new List<string>();
        List<string> steamLocations = new List<string>();
        List<string> uninstallLocations = new List<string>();
        List<string> driveRoots = new List<string>();
        locations.Add(installerDirectory);
        try { locations.Add(Environment.CurrentDirectory); } catch (Exception) { }
        try { locations.Add(AppDomain.CurrentDomain.BaseDirectory); } catch (Exception) { }
        ReadRegistryLocations(steamLocations, uninstallLocations);
        try
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                        driveRoots.Add(drive.RootDirectory.FullName);
                }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
        return FindCandidatesFromRoots(locations, steamLocations, uninstallLocations, driveRoots);
    }

    internal static bool IsGameDirectory(string path)
    {
        string fullPath = LocalPath(path);
        if (fullPath == null) return false;
        try
        {
            return Directory.Exists(fullPath)
                && File.Exists(Path.Combine(fullPath, "DawnOfMan.exe"))
                && Directory.Exists(Path.Combine(fullPath, @"DawnOfMan_Data\Managed"))
                && (File.Exists(Path.Combine(fullPath, @"DawnOfMan_Data\Managed\Assembly-CSharp.dll"))
                    || File.Exists(Path.Combine(fullPath, @"DawnTrainer\.transaction\journal")));
        }
        catch (Exception) { return false; }
    }

    // All accepted selections resolve to a verified game root; invalid selections return null.
    internal static string NormalizeSelection(string path)
    {
        string fullPath = LocalPath(path);
        if (fullPath == null) return null;
        try
        {
            if (String.Equals(Path.GetFileName(fullPath), "DawnOfMan.exe", StringComparison.OrdinalIgnoreCase))
                fullPath = Path.GetDirectoryName(fullPath);
            else if (String.Equals(Path.GetFileName(fullPath), "Managed", StringComparison.OrdinalIgnoreCase))
            {
                string dataPath = Path.GetDirectoryName(fullPath);
                if (!String.Equals(Path.GetFileName(dataPath), "DawnOfMan_Data", StringComparison.OrdinalIgnoreCase))
                    return null;
                fullPath = Path.GetDirectoryName(dataPath);
            }
            else if (String.Equals(Path.GetFileName(fullPath), "DawnOfMan_Data", StringComparison.OrdinalIgnoreCase))
                fullPath = Path.GetDirectoryName(fullPath);
            return IsGameDirectory(fullPath) ? fullPath : null;
        }
        catch (Exception) { return null; }
    }

    // Pure input seam for repeatable tests: no registry access and no real-drive enumeration.
    internal static IList<string> FindCandidatesFromRoots(
        IEnumerable<string> installerDirectories, IEnumerable<string> steamDirectories,
        IEnumerable<string> uninstallLocations, IEnumerable<string> fixedDriveRoots)
    {
        List<string> result = new List<string>();
        HashSet<string> found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> visitedSteam = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (installerDirectories != null)
        {
            foreach (string path in installerDirectories)
            {
                string fullPath = LocalPath(path);
                if (fullPath == null) continue;
                ProbeNearby(fullPath, result, found, visitedSteam);
                try
                {
                    DirectoryInfo parent = Directory.GetParent(fullPath);
                    if (parent != null) ProbeNearby(parent.FullName, result, found, visitedSteam);
                }
                catch (Exception) { }
            }
        }
        if (steamDirectories != null)
            foreach (string path in steamDirectories)
                ProbeSteam(path, result, found, visitedSteam);
        if (uninstallLocations != null)
            foreach (string path in uninstallLocations)
                AddCandidate(path, result, found);
        if (fixedDriveRoots != null)
        {
            foreach (string path in fixedDriveRoots)
            {
                string root = LocalPath(path);
                if (root == null) continue;
                ProbeNearby(root, result, found, visitedSteam);
                ProbeNearby(Combine(root, "Games"), result, found, visitedSteam);
                ProbeSteam(Combine(root, @"Program Files\Steam"), result, found, visitedSteam);
                ProbeSteam(Combine(root, @"Program Files (x86)\Steam"), result, found, visitedSteam);
            }
        }
        return result;
    }

    private static void ProbeNearby(string directory, List<string> result, HashSet<string> found,
        HashSet<string> visitedSteam)
    {
        if (directory == null) return;
        AddCandidate(directory, result, found);
        foreach (string name in GameFolderNames)
            AddCandidate(Combine(directory, name), result, found);
        ProbeSteam(directory, result, found, visitedSteam);
        ProbeSteam(Combine(directory, "Steam"), result, found, visitedSteam);
        ProbeSteam(Combine(directory, "SteamLibrary"), result, found, visitedSteam);
    }

    private static void ProbeSteam(string directory, List<string> result, HashSet<string> found,
        HashSet<string> visitedSteam)
    {
        string fullPath = LocalPath(directory);
        if (fullPath == null || !visitedSteam.Add(fullPath)) return;
        ProbeLibrary(fullPath, result, found);
        // The steamapps location is current; config also supports older Steam installations.
        string[] metadataPaths = {
            Combine(fullPath, @"steamapps\libraryfolders.vdf"),
            Combine(fullPath, @"config\libraryfolders.vdf")
        };
        foreach (string metadataPath in metadataPaths)
        {
            string text = ReadMetadata(metadataPath);
            if (text == null) continue;
            foreach (string library in ParseLibraryFolders(text))
            {
                // Steam library paths must be absolute; never resolve untrusted metadata against CWD.
                if (!IsAbsoluteLocalPath(library)) continue;
                string libraryPath = LocalPath(library);
                if (libraryPath != null) ProbeLibrary(libraryPath, result, found);
            }
        }
    }

    private static void ProbeLibrary(string library, List<string> result, HashSet<string> found)
    {
        foreach (string name in GameFolderNames)
            AddCandidate(Combine(library, @"steamapps\common\" + name), result, found);
        string manifest = ReadMetadata(Combine(library, @"steamapps\appmanifest_858810.acf"));
        if (manifest == null) return;
        string installDirectory = ParseInstallDirectory(manifest);
        if (!ValidInstallDirectoryName(installDirectory)) return;
        AddCandidate(Combine(library, @"steamapps\common\" + installDirectory), result, found);
    }

    private static void AddCandidate(string path, List<string> result, HashSet<string> found)
    {
        string gamePath = NormalizeSelection(path);
        if (gamePath != null && found.Add(gamePath)) result.Add(gamePath);
    }

    private static string Combine(string root, string relative)
    {
        if (root == null) return null;
        try { return Path.Combine(root, relative); }
        catch (Exception) { return null; }
    }

    private static bool IsAbsoluteLocalPath(string path)
    {
        return path != null && path.Length >= 3 && Char.IsLetter(path[0])
            && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
    }

    private static string LocalPath(string path)
    {
        if (String.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = path.Trim();
            if (path.Length >= 2 && path[0] == '"' && path[path.Length - 1] == '"')
                path = path.Substring(1, path.Length - 2);
            // Reject UNC, device namespaces and mapped network drives before touching game files.
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
                return null;
            string fullPath = Path.GetFullPath(path);
            if (!IsAbsoluteLocalPath(fullPath)) return null;
            string root = Path.GetPathRoot(fullPath);
            DriveType type = new DriveInfo(root).DriveType;
            if (type == DriveType.Network || type == DriveType.Unknown || type == DriveType.NoRootDirectory)
                return null;
            while (fullPath.Length > root.Length && (fullPath.EndsWith("\\", StringComparison.Ordinal)
                || fullPath.EndsWith("/", StringComparison.Ordinal)))
                fullPath = fullPath.Substring(0, fullPath.Length - 1);
            return fullPath;
        }
        catch (Exception) { return null; }
    }

    private static string ReadMetadata(string path)
    {
        if (path == null) return null;
        try
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length > MaximumMetadataBytes) return null;
            byte[] buffer = new byte[MaximumMetadataBytes + 1];
            int count = 0;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                while (count < buffer.Length)
                {
                    int read = stream.Read(buffer, count, buffer.Length - count);
                    if (read == 0) break;
                    count += read;
                }
            }
            if (count > MaximumMetadataBytes) return null;
            using (MemoryStream memory = new MemoryStream(buffer, 0, count, false))
            using (StreamReader reader = new StreamReader(memory, Encoding.UTF8, true))
                return reader.ReadToEnd();
        }
        catch (Exception) { return null; }
    }

    internal static IList<string> ParseLibraryFolders(string text)
    {
        List<string> result = new List<string>();
        List<VdfEntry> root = ParseVdf(text);
        List<VdfEntry> folders = ChildEntries(root, "libraryfolders");
        if (folders == null) return result;
        foreach (VdfEntry entry in folders)
        {
            if (!IsNumber(entry.Key)) continue;
            string path = entry.Children == null ? entry.Value : EntryValue(entry.Children, "path");
            if (!String.IsNullOrWhiteSpace(path)) result.Add(path);
        }
        return result;
    }

    internal static string ParseInstallDirectory(string text)
    {
        return EntryValue(ChildEntries(ParseVdf(text), "AppState"), "installdir");
    }

    private static bool ValidInstallDirectoryName(string name)
    {
        if (String.IsNullOrWhiteSpace(name) || name == "." || name == "..") return false;
        return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && name.IndexOf('\\') < 0 && name.IndexOf('/') < 0 && name.IndexOf(':') < 0;
    }

    private static bool IsNumber(string value)
    {
        if (String.IsNullOrEmpty(value)) return false;
        foreach (char c in value) if (c < '0' || c > '9') return false;
        return true;
    }

    private sealed class VdfEntry
    {
        internal string Key;
        internal string Value;
        internal List<VdfEntry> Children;
    }

    private static List<VdfEntry> ChildEntries(List<VdfEntry> entries, string key)
    {
        if (entries != null)
            foreach (VdfEntry entry in entries)
                if (String.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)) return entry.Children;
        return null;
    }

    private static string EntryValue(List<VdfEntry> entries, string key)
    {
        if (entries != null)
            foreach (VdfEntry entry in entries)
                if (String.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)) return entry.Value;
        return null;
    }

    private static List<VdfEntry> ParseVdf(string text)
    {
        if (String.IsNullOrEmpty(text) || text.Length > MaximumMetadataBytes) return null;
        List<string> tokens = TokenizeVdf(text);
        if (tokens == null) return null;
        int position = 0;
        List<VdfEntry> result = ParseEntries(tokens, ref position, 0);
        return position == tokens.Count ? result : null;
    }

    private static List<VdfEntry> ParseEntries(List<string> tokens, ref int position, int depth)
    {
        if (depth > 32) return null;
        List<VdfEntry> entries = new List<VdfEntry>();
        while (position < tokens.Count)
        {
            string key = tokens[position++];
            if (key == "}") return depth == 0 ? null : entries;
            if (key == "{" || position >= tokens.Count) return null;
            string value = tokens[position++];
            if (value == "}") return null;
            VdfEntry entry = new VdfEntry();
            entry.Key = key;
            if (value == "{")
            {
                entry.Children = ParseEntries(tokens, ref position, depth + 1);
                if (entry.Children == null) return null;
            }
            else entry.Value = value;
            entries.Add(entry);
        }
        return depth == 0 ? entries : null;
    }

    private static List<string> TokenizeVdf(string text)
    {
        List<string> tokens = new List<string>();
        int position = 0;
        while (position < text.Length)
        {
            char c = text[position];
            if (Char.IsWhiteSpace(c) || c == '\uFEFF') { position++; continue; }
            if (c == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n') position++;
                continue;
            }
            if (c == '{' || c == '}') { tokens.Add(c.ToString()); position++; }
            else if (c == '"')
            {
                position++;
                StringBuilder value = new StringBuilder();
                bool closed = false;
                while (position < text.Length)
                {
                    c = text[position++];
                    if (c == '"') { closed = true; break; }
                    if (c == '\\' && position < text.Length && (text[position] == '\\' || text[position] == '"'))
                        c = text[position++];
                    value.Append(c);
                }
                if (!closed) return null;
                tokens.Add(value.ToString());
            }
            else
            {
                int start = position;
                while (position < text.Length && !Char.IsWhiteSpace(text[position])
                    && text[position] != '{' && text[position] != '}') position++;
                tokens.Add(text.Substring(start, position - start));
            }
            if (tokens.Count > 65536) return null;
        }
        return tokens;
    }

    private static void ReadRegistryLocations(List<string> steamLocations, List<string> uninstallLocations)
    {
        RegistryHive[] hives = { RegistryHive.CurrentUser, RegistryHive.LocalMachine };
        RegistryView[] views = { RegistryView.Registry32, RegistryView.Registry64 };
        foreach (RegistryHive hive in hives)
        {
            foreach (RegistryView view in views)
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                    {
                        ReadSteamKey(root, steamLocations);
                        ReadUninstallKeys(root, uninstallLocations);
                    }
                }
                catch (Exception) { }
            }
        }
    }

    private static void ReadSteamKey(RegistryKey root, List<string> locations)
    {
        try
        {
            using (RegistryKey key = root.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (key == null) return;
                foreach (string name in new string[] { "SteamPath", "InstallPath", "SteamExe" })
                {
                    string value = key.GetValue(name) as string;
                    if (String.IsNullOrWhiteSpace(value)) continue;
                    if (String.Equals(name, "SteamExe", StringComparison.Ordinal))
                    {
                        string fullPath = LocalPath(value);
                        if (fullPath != null) locations.Add(Path.GetDirectoryName(fullPath));
                    }
                    else locations.Add(value);
                }
            }
        }
        catch (Exception) { }
    }

    private static void ReadUninstallKeys(RegistryKey root, List<string> locations)
    {
        try
        {
            using (RegistryKey uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
            {
                if (uninstall == null) return;
                foreach (string subKeyName in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using (RegistryKey app = uninstall.OpenSubKey(subKeyName))
                        {
                            if (app == null) continue;
                            string displayName = app.GetValue("DisplayName") as string;
                            if ((displayName != null && (displayName.IndexOf("Dawn of Man", StringComparison.OrdinalIgnoreCase) >= 0
                                || displayName.IndexOf("DawnOfMan", StringComparison.OrdinalIgnoreCase) >= 0))
                                || String.Equals(subKeyName, "Steam App 858810", StringComparison.OrdinalIgnoreCase))
                                locations.Add(app.GetValue("InstallLocation") as string);
                        }
                    }
                    catch (Exception) { }
                }
            }
        }
        catch (Exception) { }
    }
}
