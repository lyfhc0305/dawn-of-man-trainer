using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("人类黎明修改器安装程序")]
[assembly: AssemblyProduct("Dawn of Man Trainer")]
[assembly: AssemblyVersion("2.1.1.0")]
[assembly: AssemblyFileVersion("2.1.1.0")]

internal sealed class InstallerResult
{
    internal int ExitCode;
    internal string Output;
}

internal static class InstallerHost
{
    internal const string Version = "2.1.1";
    private const string ResourcePrefix = "DawnTrainer.Bundle.";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int standardHandle);
    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr handle);

    [STAThread]
    private static int Main(string[] args)
    {
        // Register before the separately JIT-compiled engine touches Mono.Cecil.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveBundledAssembly;
        if (args.Length > 0) return RunCommandLine(args);
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerWindow());
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show("安装程序无法打开：\r\n" + error.Message, "人类黎明修改器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static Assembly ResolveBundledAssembly(object sender, ResolveEventArgs args)
    {
        AssemblyName name = new AssemblyName(args.Name);
        if (name.Name != "Mono.Cecil") return null;
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + "Mono.Cecil.dll"))
        {
            if (stream == null) return null;
            using (MemoryStream bytes = new MemoryStream()) { stream.CopyTo(bytes); return Assembly.Load(bytes.ToArray()); }
        }
    }

    private static int RunCommandLine(string[] args)
    {
        // Keep explicit command-line calls available to scripts; a normal double-click never creates a console.
        IntPtr outputHandle = GetStdHandle(-11);
        if (outputHandle == IntPtr.Zero || outputHandle == new IntPtr(-1) || GetFileType(outputHandle) == 0)
            AttachConsole(UInt32.MaxValue);
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch (IOException) { }
        if (Array.Exists(args, delegate(string arg) { return arg == "--help" || arg == "-h"; }))
        {
            Console.WriteLine("双击 Installer.exe 打开图形安装界面。\n命令行：Installer.exe [--verify | --uninstall] [游戏目录]\n相对游戏目录以 Installer.exe 所在位置为起点；未指定时使用 ..\\DawnOfMan。");
            return 0;
        }
        foreach (string arg in args)
            if (arg.StartsWith("--") && arg != "--verify" && arg != "--uninstall")
            { Console.Error.WriteLine("未知选项：" + arg + "。使用 --help 查看说明。"); return 1; }
        int paths = Array.FindAll(args, delegate(string arg) { return !arg.StartsWith("--"); }).Length;
        if (paths > 1) { Console.Error.WriteLine("请只指定一个游戏目录。"); return 1; }
        InstallerResult result = ExecuteArguments(args);
        if (result.ExitCode == 0) Console.Write(result.Output); else Console.Error.Write(result.Output);
        return result.ExitCode;
    }

    internal static InstallerResult Execute(string gamePath, string operation)
    {
        string valid = GameDiscovery.NormalizeSelection(gamePath);
        if (valid == null)
            return new InstallerResult { ExitCode = 1, Output = "请选择包含 DawnOfMan.exe 和 DawnOfMan_Data 的游戏安装目录。" };
        if (operation != "install" && operation != "verify" && operation != "uninstall")
            return new InstallerResult { ExitCode = 1, Output = "未知操作。" };
        string[] args = operation == "install" ? new[] { valid } : new[] { "--" + operation, valid };
        return ExecuteArguments(args);
    }

    internal static InstallerResult ExecuteArguments(string[] args)
    {
        TextWriter oldOut = Console.Out, oldError = Console.Error;
        string temporaryBundle = null;
        using (StringWriter capture = new StringWriter())
        {
            try
            {
                Console.SetOut(capture); Console.SetError(capture);
                temporaryBundle = Path.Combine(Path.GetTempPath(), "DawnTrainer-setup-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryBundle);
                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + "DawnOfManTrainer.dll"))
                {
                    if (resource == null) throw new InvalidDataException("安装程序缺少内置功能库，请重新下载完整安装程序。");
                    using (FileStream file = new FileStream(Path.Combine(temporaryBundle, "DawnOfManTrainer.dll"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        resource.CopyTo(file);
                }
                int exitCode = RunEngine(args, temporaryBundle);
                return new InstallerResult { ExitCode = exitCode, Output = capture.ToString() };
            }
            catch (Exception error)
            {
                capture.WriteLine("操作停止：" + error.Message);
                return new InstallerResult { ExitCode = 1, Output = capture.ToString() };
            }
            finally
            {
                Console.SetOut(oldOut); Console.SetError(oldError);
                if (temporaryBundle != null)
                {
                    // Delete only the exact file extracted by this invocation; never recurse into a supplied path.
                    try { File.Delete(Path.Combine(temporaryBundle, "DawnOfManTrainer.dll")); Directory.Delete(temporaryBundle, false); }
                    catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunEngine(string[] args, string bundle)
    {
        return Installer.Run(args, bundle);
    }

    internal static string ReadGuide()
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + "Guide.txt"))
        {
            if (stream == null) return "进入地图后按 F11 打开修改器。请查看安装包中的《使用说明.txt》。";
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }
    }

    internal static void ShowGuide(IWin32Window owner)
    {
        using (Form guide = new Form())
        {
            guide.Text = "人类黎明修改器 · 使用说明 v" + Version;
            guide.StartPosition = FormStartPosition.CenterParent;
            guide.Size = new Size(820, 690);
            guide.MinimumSize = new Size(560, 420);
            guide.Font = new Font("Microsoft YaHei UI", 10F);
            guide.BackColor = Color.White;
            guide.ShowIcon = false;
            guide.AutoScaleMode = AutoScaleMode.Font;
            RichTextBox body = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
                BackColor = Color.White, ForeColor = Color.FromArgb(35, 46, 60), DetectUrls = false, Text = ReadGuide() };
            Panel content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22) };
            content.Controls.Add(body);
            Button close = new Button { Text = "关闭说明", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(12, 6, 12, 6) };
            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
            footer.Controls.Add(close);
            guide.Controls.Add(content); guide.Controls.Add(footer);
            guide.AcceptButton = close; guide.CancelButton = close;
            guide.ShowDialog(owner);
        }
    }
}
