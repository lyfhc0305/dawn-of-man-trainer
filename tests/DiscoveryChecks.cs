using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal static class DiscoveryChecks
{
    private static int checks;
    private static string sandbox;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Provide a new fixture directory.");
            sandbox = Path.GetFullPath(args[0]);
            if (Directory.Exists(sandbox) || File.Exists(sandbox))
                throw new IOException("Discovery fixtures require a new directory.");
            Directory.CreateDirectory(sandbox);
            SelectionChecks();
            NearbyChecks();
            SteamChecks();
            DefensiveChecks();
            CommonLocationChecks();
            Console.WriteLine("PASS: " + checks + " discovery checks (temporary fixtures only).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    private static void SelectionChecks()
    {
        string game = Game(@"selection\中文 游戏 with spaces");
        Check(GameDiscovery.IsGameDirectory(game), "game root is valid");
        Check(Same(GameDiscovery.NormalizeSelection(game), game), "root normalizes");
        Check(Same(GameDiscovery.NormalizeSelection(game + "\\"), game), "trailing separator normalizes");
        Check(Same(GameDiscovery.NormalizeSelection("\"" + Path.Combine(game, "DawnOfMan.exe") + "\""), game), "quoted exe normalizes");
        Check(Same(GameDiscovery.NormalizeSelection(Path.Combine(game, "DawnOfMan_Data")), game), "data folder normalizes");
        Check(Same(GameDiscovery.NormalizeSelection(Path.Combine(game, @"DawnOfMan_Data\Managed")), game), "managed folder normalizes");
        Check(!GameDiscovery.IsGameDirectory(Path.Combine(game, "DawnOfMan_Data")), "validation requires root");
        Check(GameDiscovery.NormalizeSelection(Path.Combine(game, "other.exe")) == null, "wrong executable is rejected");
        Check(GameDiscovery.NormalizeSelection(Path.Combine(game, "UnrelatedManaged")) == null, "unrelated managed folder is rejected");
        Check(GameDiscovery.NormalizeSelection(null) == null, "null is rejected");
        Check(GameDiscovery.NormalizeSelection("   ") == null, "empty selection is rejected");
        Check(GameDiscovery.NormalizeSelection("bad\0path") == null, "invalid path is rejected");
        Check(GameDiscovery.NormalizeSelection(@"\\nonexistent-server\share\DawnOfMan") == null, "UNC path is rejected");
        Check(GameDiscovery.NormalizeSelection(@"\\?\C:\DawnOfMan") == null, "device path is rejected");

        string noExe = Game(@"selection\no-exe");
        File.Delete(Path.Combine(noExe, "DawnOfMan.exe"));
        Check(!GameDiscovery.IsGameDirectory(noExe), "missing executable is rejected");
        string noAssembly = Game(@"selection\no-assembly");
        File.Delete(Path.Combine(noAssembly, @"DawnOfMan_Data\Managed\Assembly-CSharp.dll"));
        Check(!GameDiscovery.IsGameDirectory(noAssembly), "missing assembly is rejected");
        Directory.CreateDirectory(Path.Combine(noAssembly, @"DawnTrainer\.transaction"));
        Check(!GameDiscovery.IsGameDirectory(noAssembly), "empty transaction is rejected");
        Write(Path.Combine(noAssembly, @"DawnTrainer\.transaction\journal"), "DawnTrainer transaction 1");
        Check(GameDiscovery.IsGameDirectory(noAssembly), "published recovery transaction remains selectable");
        Check(Same(GameDiscovery.NormalizeSelection(Path.Combine(noAssembly, @"DawnOfMan_Data\Managed")), noAssembly), "recovery managed folder normalizes");
        string noManaged = Game(@"selection\no-managed");
        File.Delete(Path.Combine(noManaged, @"DawnOfMan_Data\Managed\Assembly-CSharp.dll"));
        Directory.Delete(Path.Combine(noManaged, @"DawnOfMan_Data\Managed"));
        Write(Path.Combine(noManaged, @"DawnTrainer\.transaction\journal"), "DawnTrainer transaction 1");
        Check(!GameDiscovery.IsGameDirectory(noManaged), "journal without managed folder is rejected");
    }

    private static void NearbyChecks()
    {
        string installer = Folder(@"nearby\安装 包");
        string sibling = Game(@"nearby\DawnOfMan");
        string child = Game(@"nearby\安装 包\Dawn of Man");
        string steam = Game(@"nearby\Steam\steamapps\common\Dawn of Man");
        AssertSet(GameDiscovery.FindCandidatesFromRoots(new string[] { installer }, null, null, null),
            "installer children and parent siblings", child, sibling, steam);
        AssertSet(GameDiscovery.FindCandidatesFromRoots(new string[] { sibling, sibling.ToUpperInvariant(), sibling + "\\" }, null,
            new string[] { sibling }, null), "duplicate paths are removed", sibling, steam);
        // A recovery-only game is also discoverable, not just manually selectable.
        string recovery = Game(@"recovery\DawnOfMan");
        File.Delete(Path.Combine(recovery, @"DawnOfMan_Data\Managed\Assembly-CSharp.dll"));
        Write(Path.Combine(recovery, @"DawnTrainer\.transaction\journal"), "DawnTrainer transaction 1");
        AssertSet(GameDiscovery.FindCandidatesFromRoots(new string[] { Folder(@"recovery\Installer") }, null, null, null),
            "recovery directory is discovered", recovery);
    }

    private static void SteamChecks()
    {
        string steam = Folder(@"modern\Steam 客户端");
        string library = Folder(@"modern\游戏库 中文 with spaces");
        string custom = Game(@"modern\游戏库 中文 with spaces\steamapps\common\自定义 游戏");
        string installedAtSteam = Game(@"modern\Steam 客户端\steamapps\common\Dawn of Man");
        Write(Path.Combine(steam, @"steamapps\libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n// modern Steam libraries\n\"0\" { \"path\" " + Quote(steam)
            + " \"apps\" { \"858810\" \"100\" } }\n\"1\" { \"path\" " + Quote(library)
            + " \"label\" \"中文\" }\n}");
        Write(Path.Combine(library, @"steamapps\appmanifest_858810.acf"),
            "\"AppState\" { \"appid\" \"858810\" \"installdir\" \"自定义 游戏\" }");
        AssertSet(GameDiscovery.FindCandidatesFromRoots(null, new string[] { steam }, null, null),
            "modern VDF and manifest handle Chinese and spaces", custom, installedAtSteam);

        string oldSteam = Folder(@"legacy\Steam");
        string oldLibrary = Folder(@"legacy\旧 游戏库");
        string oldGame = Game(@"legacy\旧 游戏库\steamapps\common\DawnOfMan");
        Write(Path.Combine(oldSteam, @"config\libraryfolders.vdf"),
            "\"LibraryFolders\" { \"TimeNextStatsReport\" \"0\" \"1\" " + Quote(oldLibrary) + " }");
        AssertSet(GameDiscovery.FindCandidatesFromRoots(null, new string[] { oldSteam }, null, null),
            "legacy VDF and config location work", oldGame);
        AssertSet(GameDiscovery.FindCandidatesFromRoots(null, new string[] { oldLibrary }, null, null),
            "direct Steam library works", oldGame);

        IList<string> parsed = GameDiscovery.ParseLibraryFolders("\"LibraryFolders\" { \"1\" " + Quote(oldLibrary)
            + " \"2\" { \"path\" " + Quote(library) + " } }");
        Check(parsed.Count == 2 && Same(parsed[0], oldLibrary) && Same(parsed[1], library), "mixed old and new entries parse");
        Check(GameDiscovery.ParseLibraryFolders("\"libraryfolders\" { \"1\" { \"path\" \"C:\\Games\" } }")[0]
            == @"C:\Games", "native backslashes are preserved");
        Check(GameDiscovery.ParseInstallDirectory("AppState { installdir \"Game With Spaces\" }") == "Game With Spaces", "unquoted VDF keys work");
        Check(GameDiscovery.ParseLibraryFolders("\"libraryfolders\" { \"1\" \"unterminated").Count == 0, "broken quotes are ignored");
        Check(GameDiscovery.ParseLibraryFolders("\"libraryfolders\" { \"1\" \"C:\\Games\"").Count == 0, "missing brace is ignored");
        Check(GameDiscovery.ParseInstallDirectory("\"AppState\" { \"installdir\" \"unterminated") == null, "broken manifest is ignored");
    }

    private static void DefensiveChecks()
    {
        string badSteam = Folder(@"defensive\bad-steam");
        string hiddenLibrary = Folder(@"defensive\hidden-library");
        Game(@"defensive\hidden-library\steamapps\common\Dawn of Man");
        Write(Path.Combine(badSteam, @"steamapps\libraryfolders.vdf"),
            "\"libraryfolders\" { \"1\" " + Quote(hiddenLibrary) + " }" + new string(' ', 1024 * 1024));
        AssertSet(GameDiscovery.FindCandidatesFromRoots(null, new string[] { badSteam }, null, null),
            "oversized VDF is ignored");
        string custom = Game(@"defensive\bad-steam\steamapps\common\Custom Game");
        Write(Path.Combine(badSteam, @"steamapps\appmanifest_858810.acf"),
            "\"AppState\" { \"installdir\" \"Custom Game\" }" + new string(' ', 1024 * 1024));
        AssertSet(GameDiscovery.FindCandidatesFromRoots(null, new string[] { badSteam }, null, null),
            "oversized manifest is ignored");
        Check(Directory.Exists(custom), "oversized metadata check retains fixture");

        Write(Path.Combine(badSteam, @"steamapps\appmanifest_858810.acf"),
            "\"AppState\" { \"installdir\" \"..\\..\\Escape Game\" }");
        Game(@"defensive\bad-steam\Escape Game");
        Write(Path.Combine(badSteam, @"steamapps\libraryfolders.vdf"),
            "\"libraryfolders\" { \"1\" \"relative-library\" \"2\" \"\\\\server\\share\" }");
        AssertSet(GameDiscovery.FindCandidatesFromRoots(null, new string[] { badSteam }, null, null),
            "relative and network libraries and manifest traversal are rejected");
        string goodSteam = Folder(@"defensive\good-steam");
        string goodGame = Game(@"defensive\good-steam\steamapps\common\Dawn of Man");
        Write(Path.Combine(badSteam, @"steamapps\libraryfolders.vdf"), "\"libraryfolders\" { invalid {");
        AssertSet(GameDiscovery.FindCandidatesFromRoots(new string[] { null, "bad\0path" },
            new string[] { @"\\server\share", "bad\0path", badSteam, goodSteam },
            new string[] { "bad\0path", "does-not-exist" }, null),
            "one bad source does not abort later sources", goodGame);
        Check(GameDiscovery.ParseLibraryFolders(new string(' ', 1024 * 1024 + 1)).Count == 0,
            "parser rejects oversized input");
        StringBuilder deeplyNested = new StringBuilder("libraryfolders {");
        for (int i = 0; i < 40; i++) deeplyNested.Append(" a {");
        deeplyNested.Append(" path \"C:\\Games\"");
        for (int i = 0; i < 41; i++) deeplyNested.Append(" }");
        Check(GameDiscovery.ParseLibraryFolders(deeplyNested.ToString()).Count == 0, "nesting depth is bounded");
        AssertSet(GameDiscovery.FindCandidatesFromRoots(null, null, null, null), "empty sources are safe");
    }

    private static void CommonLocationChecks()
    {
        string drive = Folder("fixed-drive");
        string direct = Game(@"fixed-drive\Dawn of Man");
        string games = Game(@"fixed-drive\Games\DawnOfMan");
        string library = Game(@"fixed-drive\SteamLibrary\steamapps\common\Dawn of Man");
        string programFiles = Game(@"fixed-drive\Program Files (x86)\Steam\steamapps\common\Dawn of Man");
        string programFiles64 = Game(@"fixed-drive\Program Files\Steam\steamapps\common\DawnOfMan");
        AssertSet(GameDiscovery.FindCandidatesFromRoots(null, null, null, new string[] { drive }),
            "common fixed-drive locations work", direct, games, library, programFiles, programFiles64);
        string uninstall = Game(@"uninstall\游戏 根");
        AssertSet(GameDiscovery.FindCandidatesFromRoots(null, null, new string[] { uninstall, uninstall.ToUpperInvariant() }, null),
            "uninstall locations are validated and deduplicated", uninstall);
        string noScan = Folder("no-recursion");
        Game(@"no-recursion\arbitrary\deep\DawnOfMan");
        AssertSet(GameDiscovery.FindCandidatesFromRoots(null, null, null, new string[] { noScan }),
            "arbitrary folders are not scanned recursively");
    }

    private static string Folder(string relative)
    {
        string path = Path.Combine(sandbox, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Game(string relative)
    {
        string path = Folder(relative);
        Write(Path.Combine(path, "DawnOfMan.exe"), "fixture only");
        Write(Path.Combine(path, @"DawnOfMan_Data\Managed\Assembly-CSharp.dll"), "fixture only");
        return path;
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static bool Same(string left, string right)
    {
        return String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSet(IList<string> actual, string name, params string[] expected)
    {
        HashSet<string> actualSet = new HashSet<string>(actual, StringComparer.OrdinalIgnoreCase);
        Check(actual.Count == actualSet.Count && actualSet.SetEquals(expected), name
            + "; expected [" + String.Join(", ", expected) + "] got [" + String.Join(", ", actual) + "]");
    }

    private static void Check(bool passed, string name)
    {
        if (!passed) throw new InvalidOperationException(name);
        checks++;
    }
}
