using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class InstallerUiChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1) throw new ArgumentException("Provide a new fixture directory.");
            string sandbox = Path.GetFullPath(args[0]);
            if (Directory.Exists(sandbox) || File.Exists(sandbox))
                throw new IOException("UI checks require a new fixture directory.");
            Directory.CreateDirectory(sandbox);
            string selectionGame = Path.Combine(sandbox, "中文 游戏目录 with spaces");
            Directory.CreateDirectory(Path.Combine(selectionGame, @"DawnOfMan_Data\Managed"));
            File.WriteAllText(Path.Combine(selectionGame, "DawnOfMan.exe"), "UI fixture only");
            File.WriteAllText(Path.Combine(selectionGame, @"DawnOfMan_Data\Managed\Assembly-CSharp.dll"), "UI fixture only");
            string gamePath = args.Length > 1 && !String.IsNullOrWhiteSpace(args[1]) ? Path.GetFullPath(args[1]) : null;
            bool environmentDiscovery = args.Length > 2 && args[2] == "--check-environment-discovery";

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            int exitCode = 0;
            using (ApplicationContext context = new ApplicationContext())
            using (System.Windows.Forms.Timer start = new System.Windows.Forms.Timer())
            {
                start.Interval = 1;
                start.Tick += delegate
                {
                    start.Stop();
                    try { RunChecks(sandbox, selectionGame, gamePath, environmentDiscovery); }
                    catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); exitCode = 1; }
                    finally { context.ExitThread(); }
                };
                start.Start();
                Application.Run(context);
            }
            return exitCode;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    private static void RunChecks(string sandbox, string selectionGame, string gamePath, bool environmentDiscovery)
    {
            using (InstallerWindow window = new InstallerWindow())
            {
                using (Bitmap preview = window.RenderPreview())
                    Check(preview.Width > 0 && preview.Height > 0 && Field<bool>(window, "startupHandled")
                        && !Field<bool>(window, "busy") && Field<ComboBox>(window, "pathBox").Text.Length == 0,
                        "preview creates controls without automatic discovery or a folder dialog");
                Application.DoEvents();

                Invoke(window, "SetSelectedPath", selectionGame);
                Check(ActionsEnabled(window, true) && Same(Field<string>(window, "currentGamePath"), selectionGame),
                    "valid Chinese path enables game actions");
                Invoke(window, "SetSelectedPath", Path.Combine(selectionGame, "DawnOfMan.exe"));
                Check(ActionsEnabled(window, true) && Same(Field<string>(window, "currentGamePath"), selectionGame),
                    "executable selection resolves to the game directory");

                Field<ComboBox>(window, "pathBox").Text = Path.Combine(sandbox, "不存在的目录");
                Check(ActionsEnabled(window, false) && Field<string>(window, "currentGamePath") == null,
                    "editing the path immediately invalidates actions for the old directory");
                Invoke(window, "SetSelectedPath", Path.Combine(sandbox, "不存在的目录"));
                Check(ActionsEnabled(window, false) && Field<string>(window, "statusKind") == "error",
                    "invalid directory remains disabled after validation");

                Invoke(window, "SetSelectedPath", selectionGame);
                Invoke(window, "SetBusy", true, false);
                FormClosingEventArgs searchingClose = new FormClosingEventArgs(CloseReason.UserClosing, false);
                Invoke(window, "WindowClosing", window, searchingClose);
                Check(ActionsEnabled(window, false) && !Field<ComboBox>(window, "pathBox").Enabled
                    && !Field<Button>(window, "browseButton").Enabled && !Field<Button>(window, "discoverButton").Enabled
                    && window.UseWaitCursor && !searchingClose.Cancel,
                    "searching disables input while allowing a harmless window close");
                Invoke(window, "SetBusy", false, false);
                Check(ActionsEnabled(window, true) && Field<ComboBox>(window, "pathBox").Enabled
                    && Field<Button>(window, "discoverButton").Enabled && !window.UseWaitCursor,
                    "finishing a search restores actions for the validated directory");

                Invoke(window, "SetBusy", true, true);
                Check(ActionsEnabled(window, false) && !Field<Button>(window, "discoverButton").Enabled
                    && Field<bool>(window, "operationRunning"),
                    "file operation locks actions and enables the close protection state");
                // Do not invoke WindowClosing in this state: it intentionally opens a modal warning.
                Invoke(window, "SetBusy", false, false);

                if (gamePath != null)
                {
                    try { VerificationChecks(window, sandbox, gamePath); }
                    catch (Exception error) { Console.Error.WriteLine("Verification failure before window cleanup: " + error); throw; }
                }
                else Console.WriteLine("SKIP: real-assembly verification (no GamePath supplied).");
                if (environmentDiscovery) EnvironmentDiscoveryCheck(gamePath);
            }
            Console.WriteLine("PASS: " + checks + " installer UI checks (all operations use temporary fixtures).");
    }

    private static void VerificationChecks(InstallerWindow window, string sandbox, string sourceGame)
    {
        string sourceManaged = Path.Combine(sourceGame, @"DawnOfMan_Data\Managed");
        string original = Path.Combine(sourceGame, @"DawnTrainer\Assembly-CSharp.dll.original");
        if (!File.Exists(original)) original = Path.Combine(sourceManaged, "Assembly-CSharp.dll");
        if (!File.Exists(original)) throw new FileNotFoundException("No original game assembly is available.", original);
        string sourceHash = Hash(original);
        string fixture = Path.Combine(sandbox, "只读验证 中文 game");
        string managed = Path.Combine(fixture, @"DawnOfMan_Data\Managed");
        Directory.CreateDirectory(managed);
        File.WriteAllText(Path.Combine(fixture, "DawnOfMan.exe"), "UI fixture only; not executable");
        File.Copy(original, Path.Combine(managed, "Assembly-CSharp.dll"));
        string[] dependencies = {
            "Assembly-CSharp-firstpass", "mscorlib", "System", "System.Core", "netstandard", "UnityEngine",
            "UnityEngine.CoreModule", "UnityEngine.IMGUIModule", "UnityEngine.InputLegacyModule",
            "UnityEngine.PhysicsModule", "UnityEngine.TextRenderingModule", "UnityEngine.ImageConversionModule"
        };
        foreach (string dependency in dependencies)
        {
            string file = Path.Combine(sourceManaged, dependency + ".dll");
            if (File.Exists(file)) File.Copy(file, Path.Combine(managed, dependency + ".dll"));
        }
        string before = Snapshot(fixture);
        // Fail before entering the asynchronous UI if verification cannot succeed, avoiding a modal error dialog.
        InstallerResult preflight = InstallerHost.Execute(fixture, "verify");
        if (preflight.ExitCode != 0) throw new InvalidOperationException("Verification preflight failed: " + preflight.Output);
        Invoke(window, "SetSelectedPath", fixture);
        Invoke(window, "BeginOperation", "verify");
        bool wasBusy = Field<bool>(window, "busy") && !Field<Button>(window, "verifyButton").Enabled;
        Stopwatch wait = Stopwatch.StartNew();
        while (Field<bool>(window, "busy") && wait.Elapsed < TimeSpan.FromSeconds(25))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Application.DoEvents();
        Check(wasBusy && !Field<bool>(window, "busy") && !Field<bool>(window, "operationRunning")
            && ActionsEnabled(window, true) && Field<string>(window, "statusKind") == "success"
            && Field<Label>(window, "statusTitle").Text == "验证已完成"
            && Field<StringBuilder>(window, "log").ToString().IndexOf("未修改游戏文件", StringComparison.Ordinal) >= 0,
            "real asynchronous verification completes and restores the UI");
        Check(before == Snapshot(fixture) && sourceHash == Hash(original),
            "verification leaves every fixture file and original source assembly unchanged");
    }

    private static void EnvironmentDiscoveryCheck(string expectedGame)
    {
        if (expectedGame == null || !GameDiscovery.IsGameDirectory(expectedGame))
            throw new ArgumentException("Environment discovery requires a valid supplied GamePath.");
        IList<string> candidates = GameDiscovery.FindCandidates(AppDomain.CurrentDomain.BaseDirectory);
        bool found = false;
        foreach (string path in candidates) if (Same(path, expectedGame)) found = true;
        Check(found, "optional real environment discovery finds the supplied game directory");
    }

    private static string Snapshot(string root)
    {
        string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        string[] directories = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
        StringBuilder result = new StringBuilder();
        foreach (string directory in directories) result.Append("D|").AppendLine(directory.Substring(root.Length));
        foreach (string file in files)
            result.Append("F|").Append(file.Substring(root.Length)).Append('|').AppendLine(Hash(file));
        return result.ToString();
    }

    private static string Hash(string file)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(file))
            return BitConverter.ToString(sha.ComputeHash(stream));
    }

    private static bool ActionsEnabled(InstallerWindow window, bool enabled)
    {
        return Field<Button>(window, "installButton").Enabled == enabled
            && Field<Button>(window, "verifyButton").Enabled == enabled
            && Field<Button>(window, "uninstallButton").Enabled == enabled
            && Field<LinkLabel>(window, "openDirectoryLink").Enabled == enabled;
    }

    private static T Field<T>(InstallerWindow window, string name)
    {
        FieldInfo field = typeof(InstallerWindow).GetField(name, PrivateInstance);
        if (field == null) throw new MissingFieldException(typeof(InstallerWindow).FullName, name);
        return (T)field.GetValue(window);
    }

    private static object Invoke(InstallerWindow window, string name, params object[] args)
    {
        MethodInfo method = typeof(InstallerWindow).GetMethod(name, PrivateInstance);
        if (method == null) throw new MissingMethodException(typeof(InstallerWindow).FullName, name);
        try { return method.Invoke(window, args); }
        catch (TargetInvocationException error) { throw new InvalidOperationException(name + " failed.", error.InnerException); }
    }

    private static bool Same(string left, string right)
    {
        return String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static void Check(bool passed, string name)
    {
        if (!passed) throw new InvalidOperationException(name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }
}
