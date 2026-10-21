using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal sealed class InstallerWindow : Form
{
    private static readonly Color Ink = Color.FromArgb(31, 42, 57);
    private static readonly Color Muted = Color.FromArgb(100, 113, 130);
    private static readonly Color Blue = Color.FromArgb(36, 98, 224);
    private static readonly Color Green = Color.FromArgb(32, 137, 92);
    private static readonly Color Red = Color.FromArgb(190, 67, 67);
    private readonly ComboBox pathBox;
    private readonly Button browseButton;
    private readonly Button discoverButton;
    private readonly Button installButton;
    private readonly Button verifyButton;
    private readonly Button uninstallButton;
    private readonly LinkLabel openDirectoryLink;
    private readonly LinkLabel guideLink;
    private readonly LinkLabel logLink;
    private readonly Label statusTitle;
    private readonly Label statusDescription;
    private readonly PictureBox statusBadge;
    private readonly ProgressBar progressBar;
    private readonly Timer pathTimer;
    private readonly StringBuilder log = new StringBuilder();
    private bool busy;
    private bool operationRunning;
    private bool startupHandled;
    private bool startupPromptShown;
    private bool ignorePathChange;
    private string currentGamePath;
    private Color statusColor = Blue;
    private string statusKind = "info";

    internal InstallerWindow()
    {
        Text = "人类黎明修改器 · 安装向导";
        Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
        ForeColor = Ink;
        BackColor = Color.FromArgb(244, 247, 251);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(920, 570);
        MinimumSize = new Size(820, 610);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        TableLayoutPanel layout = new TableLayoutPanel();
        layout.Dock = DockStyle.Fill;
        layout.AutoScroll = true;
        layout.Padding = new Padding(28, 22, 28, 22);
        layout.ColumnCount = 1;
        layout.RowCount = 8;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 146F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(layout);

        Panel heading = new Panel();
        heading.Dock = DockStyle.Fill;
        heading.Margin = Padding.Empty;
        Label eyebrow = CreateLabel("DAWN OF MAN", 9F, FontStyle.Bold, Blue);
        eyebrow.SetBounds(0, 0, 400, 20);
        Label title = CreateLabel("《人类黎明》修改器安装", 22F, FontStyle.Bold, Ink);
        title.SetBounds(0, 21, 700, 37);
        title.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        Label subtitle = CreateLabel("自动查找游戏目录，完成安装后进入地图，按 F11 打开修改器。", 10F, FontStyle.Regular, Muted);
        subtitle.SetBounds(0, 63, 820, 23);
        subtitle.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        heading.Controls.Add(eyebrow);
        heading.Controls.Add(title);
        heading.Controls.Add(subtitle);
        layout.Controls.Add(heading, 0, 0);

        CardPanel pathCard = new CardPanel();
        pathCard.Padding = new Padding(18, 14, 18, 12);
        pathCard.Dock = DockStyle.Fill;
        pathCard.Margin = Padding.Empty;
        TableLayoutPanel pathLayout = new TableLayoutPanel();
        pathLayout.Dock = DockStyle.Fill;
        pathLayout.Margin = Padding.Empty;
        pathLayout.ColumnCount = 3;
        pathLayout.RowCount = 3;
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
        pathLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        pathLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37F));
        pathLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Label pathTitle = CreateLabel("选择游戏目录", 11F, FontStyle.Bold, Ink);
        pathTitle.Dock = DockStyle.Fill;
        pathLayout.Controls.Add(pathTitle, 0, 0);
        pathLayout.SetColumnSpan(pathTitle, 3);
        pathBox = new ComboBox();
        pathBox.DropDownStyle = ComboBoxStyle.DropDown;
        pathBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        pathBox.AutoCompleteSource = AutoCompleteSource.FileSystemDirectories;
        pathBox.Dock = DockStyle.Fill;
        pathBox.Margin = new Padding(0, 3, 8, 0);
        pathBox.IntegralHeight = false;
        pathBox.DropDownHeight = 180;
        pathBox.AccessibleName = "游戏目录";
        pathBox.TextChanged += PathChanged;
        pathBox.SelectionChangeCommitted += delegate { ValidateCurrentPath(); };
        pathBox.Leave += delegate { if (!busy) ValidateCurrentPath(); };
        pathLayout.Controls.Add(pathBox, 0, 1);
        browseButton = CreateButton("浏览…", false);
        browseButton.Margin = new Padding(0, 0, 8, 0);
        browseButton.Dock = DockStyle.Fill;
        browseButton.Click += delegate { BrowseForGameDirectory(false); };
        pathLayout.Controls.Add(browseButton, 1, 1);
        discoverButton = CreateButton("重新查找", false);
        discoverButton.Dock = DockStyle.Fill;
        discoverButton.Click += delegate { BeginDiscovery(false); };
        pathLayout.Controls.Add(discoverButton, 2, 1);
        Label pathHint = CreateLabel("选择包含 DawnOfMan.exe 的文件夹，也可直接粘贴完整路径。", 9F, FontStyle.Regular, Muted);
        pathHint.Dock = DockStyle.Fill;
        pathHint.Padding = new Padding(0, 10, 0, 0);
        pathLayout.Controls.Add(pathHint, 0, 2);
        pathLayout.SetColumnSpan(pathHint, 3);
        pathCard.Controls.Add(pathLayout);
        layout.Controls.Add(pathCard, 0, 1);

        CardPanel statusCard = new CardPanel();
        statusCard.Dock = DockStyle.Fill;
        statusCard.Padding = new Padding(17, 14, 17, 12);
        statusCard.Margin = Padding.Empty;
        TableLayoutPanel statusLayout = new TableLayoutPanel();
        statusLayout.Dock = DockStyle.Fill;
        statusLayout.Margin = Padding.Empty;
        statusLayout.ColumnCount = 2;
        statusLayout.RowCount = 3;
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 53F));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 4F));
        statusBadge = new PictureBox();
        statusBadge.Size = new Size(40, 40);
        statusBadge.Margin = new Padding(0, 2, 8, 0);
        statusBadge.Paint += PaintStatusBadge;
        statusLayout.Controls.Add(statusBadge, 0, 0);
        statusLayout.SetRowSpan(statusBadge, 2);
        statusTitle = CreateLabel("准备安装修改器", 15F, FontStyle.Bold, Ink);
        statusTitle.Dock = DockStyle.Fill;
        statusLayout.Controls.Add(statusTitle, 1, 0);
        statusDescription = CreateLabel("正在准备查找游戏目录。您也可以点击“浏览…”手动选择。", 9.5F, FontStyle.Regular, Muted);
        statusDescription.Dock = DockStyle.Fill;
        statusDescription.AutoEllipsis = true;
        statusLayout.Controls.Add(statusDescription, 1, 1);
        progressBar = new ProgressBar();
        progressBar.Dock = DockStyle.Fill;
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.MarqueeAnimationSpeed = 25;
        progressBar.Margin = Padding.Empty;
        progressBar.Visible = false;
        statusLayout.Controls.Add(progressBar, 0, 2);
        statusLayout.SetColumnSpan(progressBar, 2);
        statusCard.Controls.Add(statusLayout);
        layout.Controls.Add(statusCard, 0, 3);

        FlowLayoutPanel actions = new FlowLayoutPanel();
        actions.Dock = DockStyle.Fill;
        actions.Margin = Padding.Empty;
        actions.Padding = new Padding(0, 16, 0, 0);
        actions.WrapContents = false;
        installButton = CreateButton("安装 / 更新", true);
        installButton.Size = new Size(202, 44);
        installButton.Margin = new Padding(0, 0, 12, 0);
        installButton.Click += delegate { BeginOperation("install"); };
        verifyButton = CreateButton("验证安装", false);
        verifyButton.Size = new Size(126, 44);
        verifyButton.Margin = new Padding(0, 0, 12, 0);
        verifyButton.Click += delegate { BeginOperation("verify"); };
        uninstallButton = CreateButton("卸载", false);
        uninstallButton.Size = new Size(100, 44);
        uninstallButton.Click += delegate { BeginOperation("uninstall"); };
        actions.Controls.Add(installButton);
        actions.Controls.Add(verifyButton);
        actions.Controls.Add(uninstallButton);
        layout.Controls.Add(actions, 0, 4);

        Panel footer = new Panel();
        footer.Dock = DockStyle.Fill;
        footer.Margin = Padding.Empty;
        FlowLayoutPanel links = new FlowLayoutPanel();
        links.Dock = DockStyle.Fill;
        links.Padding = new Padding(0, 9, 0, 0);
        links.Margin = Padding.Empty;
        links.WrapContents = false;
        openDirectoryLink = CreateLink("打开游戏目录");
        openDirectoryLink.LinkClicked += delegate { OpenGameDirectory(); };
        guideLink = CreateLink("使用说明");
        guideLink.LinkClicked += delegate
        {
            try { InstallerHost.ShowGuide(this); }
            catch (Exception ex)
            {
                AppendLog("打开使用说明失败：" + ex.Message);
                MessageBox.Show(this, "使用说明暂时无法打开。\r\n" + ex.Message, "使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        logLink = CreateLink("查看日志");
        logLink.LinkClicked += delegate { ShowLog(); };
        links.Controls.Add(openDirectoryLink);
        links.Controls.Add(guideLink);
        links.Controls.Add(logLink);
        footer.Controls.Add(links);
        layout.Controls.Add(footer, 0, 5);

        TableLayoutPanel notes = new TableLayoutPanel();
        notes.Dock = DockStyle.Fill;
        notes.Margin = Padding.Empty;
        notes.ColumnCount = 2;
        notes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        notes.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        Label reminder = CreateLabel("安装、更新或卸载前，请先退出游戏。", 9F, FontStyle.Regular, Muted);
        reminder.Dock = DockStyle.Fill;
        Label version = CreateLabel("v" + InstallerHost.Version, 9F, FontStyle.Regular, Muted);
        version.TextAlign = ContentAlignment.TopRight;
        version.Dock = DockStyle.Fill;
        notes.Controls.Add(reminder, 0, 0);
        notes.Controls.Add(version, 1, 0);
        layout.Controls.Add(notes, 0, 6);

        pathTimer = new Timer();
        pathTimer.Interval = 300;
        pathTimer.Tick += delegate { pathTimer.Stop(); ValidateCurrentPath(); };
        UpdateEnabledControls();
        FormClosing += WindowClosing;
    }

    protected override void OnLoad(EventArgs e)
    {
        // At 125% / 150% scaling a small laptop can have less vertical space than
        // the initial design size. Keep the actions inside the working area.
        Rectangle workingArea = Screen.FromControl(this).WorkingArea;
        int width = Math.Min(Width, Math.Max(320, workingArea.Width - 16));
        int height = Math.Min(Height, Math.Max(320, workingArea.Height - 16));
        if (width != Width || height != Height)
        {
            MinimumSize = new Size(Math.Min(MinimumSize.Width, width), Math.Min(MinimumSize.Height, height));
            Size = new Size(width, height);
            Location = new Point(workingArea.Left + (workingArea.Width - width) / 2,
                workingArea.Top + (workingArea.Height - height) / 2);
        }
        base.OnLoad(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (startupHandled) return;
        startupHandled = true;
        BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) BeginDiscovery(true); });
    }

    // The screenshot harness can render the default window without running discovery,
    // opening a folder chooser, or touching a game directory.
    internal Bitmap RenderPreview()
    {
        startupHandled = true;
        bool wasVisible = Visible;
        bool taskbar = ShowInTaskbar;
        double opacity = Opacity;
        try
        {
            if (!wasVisible)
            {
                ShowInTaskbar = false;
                Opacity = 0;
                Show();
            }
            PerformLayout();
            ActiveControl = installButton.Enabled ? (Control)installButton : browseButton;
            pathBox.SelectionStart = pathBox.Text.Length;
            pathBox.SelectionLength = 0;
            Bitmap bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            return bitmap;
        }
        finally
        {
            if (!wasVisible) Hide();
            Opacity = opacity;
            ShowInTaskbar = taskbar;
        }
    }

    private static Label CreateLabel(string text, float size, FontStyle style, Color color)
    {
        Label label = new Label();
        label.Text = text;
        label.Font = new Font("Microsoft YaHei UI", size, style);
        label.ForeColor = color;
        label.Margin = Padding.Empty;
        label.AutoSize = false;
        return label;
    }

    private static Button CreateButton(string text, bool primary)
    {
        Button button = new Button();
        button.Text = text;
        button.Font = new Font("Microsoft YaHei UI", 10F, primary ? FontStyle.Bold : FontStyle.Regular);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(211, 220, 232);
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(25, 82, 202) : Color.FromArgb(240, 245, 252);
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(20, 67, 170) : Color.FromArgb(228, 236, 248);
        button.BackColor = primary ? Blue : Color.White;
        button.ForeColor = primary ? Color.White : Ink;
        button.UseVisualStyleBackColor = false;
        button.Margin = Padding.Empty;
        button.Cursor = Cursors.Hand;
        return button;
    }

    private static LinkLabel CreateLink(string text)
    {
        LinkLabel link = new LinkLabel();
        link.Text = text;
        link.AutoSize = true;
        link.LinkColor = Blue;
        link.ActiveLinkColor = Color.FromArgb(20, 67, 170);
        link.DisabledLinkColor = Color.FromArgb(155, 165, 177);
        link.LinkBehavior = LinkBehavior.HoverUnderline;
        link.Margin = new Padding(0, 0, 26, 0);
        return link;
    }

    private void PathChanged(object sender, EventArgs e)
    {
        if (ignorePathChange || busy) return;
        currentGamePath = null;
        UpdateEnabledControls();
        pathTimer.Stop();
        pathTimer.Start();
    }

    private string GetNormalizedPath(string path)
    {
        if (String.IsNullOrWhiteSpace(path)) return null;
        try
        {
            string normalized = GameDiscovery.NormalizeSelection(path.Trim().Trim('"'));
            return normalized != null && GameDiscovery.IsGameDirectory(normalized) ? normalized : null;
        }
        catch { return null; }
    }

    private void ValidateCurrentPath()
    {
        if (busy || IsDisposed) return;
        currentGamePath = GetNormalizedPath(pathBox.Text);
        if (currentGamePath != null)
            SetStatus("游戏目录已就绪", "请确认游戏已退出，然后点击“安装 / 更新”。已安装的用户可先验证安装。", Green, "ready");
        else if (String.IsNullOrWhiteSpace(pathBox.Text))
            SetStatus("请选择游戏目录", "点击“浏览…”选择游戏文件夹，或点击“重新查找”再次自动搜索。", Blue, "info");
        else
            SetStatus("还未识别到游戏文件", "请选择包含 DawnOfMan.exe 和 DawnOfMan_Data 的文件夹；也可粘贴完整路径。", Red, "error");
        UpdateEnabledControls();
    }

    private void SetSelectedPath(string path)
    {
        ignorePathChange = true;
        try { pathBox.Text = path ?? String.Empty; }
        finally { ignorePathChange = false; }
        pathTimer.Stop();
        ValidateCurrentPath();
    }

    private void BeginDiscovery(bool initialSearch)
    {
        if (busy || IsDisposed) return;
        pathTimer.Stop();
        string preservedText = pathBox.Text;
        string preservedPath = GetNormalizedPath(preservedText);
        SetBusy(true, false);
        SetStatus("正在查找游戏目录", "正在检查安装器附近和常见的游戏平台目录，请稍候…", Blue, "busy");
        AppendLog("开始自动查找游戏目录。");
        BackgroundWorker worker = new BackgroundWorker();
        worker.DoWork += delegate(object sender, DoWorkEventArgs e)
        {
            e.Result = GameDiscovery.FindCandidates(AppDomain.CurrentDomain.BaseDirectory);
        };
        worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
        {
            worker.Dispose();
            if (IsDisposed || Disposing) return;
            SetBusy(false, false);
            List<string> candidates = new List<string>();
            if (e.Error != null) AppendLog("自动查找未完成：" + e.Error.Message);
            else
            {
                IList<string> found = e.Result as IList<string>;
                if (found != null)
                    foreach (string candidate in found)
                    {
                        if (String.IsNullOrWhiteSpace(candidate)) continue;
                        bool duplicate = false;
                        foreach (string existing in candidates)
                            if (String.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase)) { duplicate = true; break; }
                        if (!duplicate) candidates.Add(candidate);
                    }
            }
            ignorePathChange = true;
            try
            {
                pathBox.Items.Clear();
                foreach (string candidate in candidates) pathBox.Items.Add(candidate);
            }
            finally { ignorePathChange = false; }
            AppendLog("自动查找完成，找到 " + candidates.Count + " 个游戏目录。");
            if (preservedPath != null)
            {
                SetSelectedPath(preservedPath);
                if (candidates.Count == 0)
                    SetStatus("已保留当前游戏目录", "没有发现其他目录。您可以继续安装，也可以点击“浏览…”选择其他位置。", Green, "ready");
            }
            else if (candidates.Count > 0)
            {
                SetSelectedPath(candidates[0]);
                if (candidates.Count > 1)
                    SetStatus("找到 " + candidates.Count + " 个游戏目录", "请通过路径下拉列表选择要安装的游戏，再点击“安装 / 更新”。", Green, "ready");
            }
            else
            {
                SetSelectedPath(preservedText);
                SetStatus("未自动找到游戏目录", "请点击“浏览…”手动选择包含 DawnOfMan.exe 的游戏文件夹。", Blue, "info");
                if (initialSearch && !startupPromptShown)
                {
                    startupPromptShown = true;
                    BrowseForGameDirectory(true);
                }
            }
        };
        worker.RunWorkerAsync();
    }

    private void BrowseForGameDirectory(bool automaticPrompt)
    {
        if (busy) return;
        using (FolderBrowserDialog picker = new FolderBrowserDialog())
        {
            picker.Description = automaticPrompt
                ? "未自动找到游戏。请选择包含 DawnOfMan.exe 的游戏文件夹。"
                : "请选择包含 DawnOfMan.exe 和 DawnOfMan_Data 的游戏文件夹。";
            picker.ShowNewFolderButton = false;
            string existing = GetNormalizedPath(pathBox.Text);
            if (existing != null) picker.SelectedPath = existing;
            DialogResult result = picker.ShowDialog(this);
            if (result != DialogResult.OK)
            {
                if (GetNormalizedPath(pathBox.Text) == null)
                    SetStatus("等待选择游戏目录", "您可以随时点击“浏览…”重新选择，或在上方粘贴完整游戏路径。", Blue, "info");
                return;
            }
            string selected = GetNormalizedPath(picker.SelectedPath);
            SetSelectedPath(selected ?? picker.SelectedPath);
            if (selected == null)
            {
                AppendLog("所选文件夹未识别为游戏目录：" + picker.SelectedPath);
                MessageBox.Show(this, "此文件夹中没有找到完整的游戏文件。\r\n\r\n请选择包含 DawnOfMan.exe 和 DawnOfMan_Data 的文件夹。\r\nSteam 用户可在游戏的“管理 → 浏览本地文件”中查看位置。", "请重新选择游戏目录", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else AppendLog("已选择游戏目录：" + selected);
        }
    }

    private void BeginOperation(string operation)
    {
        if (busy) return;
        pathTimer.Stop();
        string gamePath = GetNormalizedPath(pathBox.Text);
        if (gamePath == null)
        {
            ValidateCurrentPath();
            MessageBox.Show(this, "请先选择正确的游戏目录。", "需要游戏目录", MessageBoxButtons.OK, MessageBoxIcon.Information);
            pathBox.Focus();
            return;
        }
        SetSelectedPath(gamePath);
        if (operation == "install")
        {
            if (MessageBox.Show(this, "请先退出《人类黎明》，再开始安装或更新。\r\n\r\n安装目录：\r\n" + gamePath + "\r\n\r\n确认游戏已退出并继续？", "安装 / 更新修改器", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        }
        else if (operation == "uninstall")
        {
            if (MessageBox.Show(this, "卸载将恢复游戏原文件，停用修改器。请先退出游戏。\r\n\r\n游戏目录：\r\n" + gamePath + "\r\n\r\n确认卸载？", "确认卸载修改器", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        }
        string operationName = operation == "install" ? "安装 / 更新" : operation == "uninstall" ? "卸载" : "验证安装";
        SetBusy(true, true);
        SetStatus("正在" + operationName, "正在检查游戏文件，请稍候。操作完成前请保持此窗口打开。", Blue, "busy");
        AppendLog("开始" + operationName + "。\r\n游戏目录：" + gamePath);
        BackgroundWorker worker = new BackgroundWorker();
        worker.DoWork += delegate(object sender, DoWorkEventArgs e)
        {
            e.Result = InstallerHost.Execute(gamePath, operation);
        };
        worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
        {
            worker.Dispose();
            if (IsDisposed || Disposing) return;
            SetBusy(false, false);
            InstallerResult result = e.Error == null ? e.Result as InstallerResult : null;
            string output = e.Error != null ? e.Error.ToString() : result != null ? result.Output ?? String.Empty : "安装器没有返回结果。";
            AppendLog(output.Trim());
            bool succeeded = result != null && result.ExitCode == 0;
            if (succeeded)
            {
                if (operation == "install")
                    SetStatus("安装 / 更新完成", "现在可以启动游戏。进入地图后，按 F11 打开修改器。\r\n您也可以点击“打开游戏目录”查看游戏位置。", Green, "success");
                else if (operation == "uninstall")
                    SetStatus("卸载完成", "游戏原文件已恢复。下次启动游戏时，修改器将不再加载。", Green, "success");
                else
                    SetStatus("验证已完成", GetOutputSummary(output, "文件检查已完成；请查看日志了解当前安装状态。"), Green, "success");
                AppendLog(operationName + "完成。");
            }
            else
            {
                string summary = e.Error != null ? e.Error.Message : GetOutputSummary(output, "操作未能完成。请查看日志中的详细原因。");
                bool permissionFailure = ContainsPermissionFailure(output);
                string message = summary + (permissionFailure
                    ? "\r\n请关闭安装器，右键选择“以管理员身份运行”后重试。"
                    : "\r\n可点击“查看日志”了解详细原因。");
                SetStatus(operationName + "未完成", message, Red, "error");
                MessageBox.Show(this, message, operationName + "未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            currentGamePath = GetNormalizedPath(pathBox.Text);
            UpdateEnabledControls();
        };
        worker.RunWorkerAsync();
    }

    private static string GetOutputSummary(string output, string fallback)
    {
        if (String.IsNullOrWhiteSpace(output)) return fallback;
        string[] lines = output.Replace("\r", String.Empty).Split('\n');
        string summary = String.Empty;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            summary = line;
            if (line.IndexOf("操作停止", StringComparison.Ordinal) >= 0 || line.IndexOf("错误", StringComparison.Ordinal) >= 0) break;
        }
        if (summary.Length == 0) return fallback;
        return summary.Length > 190 ? summary.Substring(0, 187) + "…" : summary;
    }

    private static bool ContainsPermissionFailure(string text)
    {
        return text.IndexOf("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("access is denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("is denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("权限", StringComparison.Ordinal) >= 0 ||
            text.IndexOf("拒绝访问", StringComparison.Ordinal) >= 0 ||
            text.IndexOf("访问被拒绝", StringComparison.Ordinal) >= 0;
    }

    private void SetBusy(bool value, bool fileOperation)
    {
        busy = value;
        operationRunning = value && fileOperation;
        progressBar.Visible = value;
        UseWaitCursor = value;
        UpdateEnabledControls();
    }

    private void UpdateEnabledControls()
    {
        bool valid = currentGamePath != null;
        pathBox.Enabled = !busy;
        browseButton.Enabled = !busy;
        discoverButton.Enabled = !busy;
        installButton.Enabled = !busy && valid;
        verifyButton.Enabled = !busy && valid;
        uninstallButton.Enabled = !busy && valid;
        openDirectoryLink.Enabled = !busy && valid;
        guideLink.Enabled = !busy;
        logLink.Enabled = !busy && log.Length > 0;
    }

    private void SetStatus(string title, string description, Color color, string kind)
    {
        statusTitle.Text = title;
        statusDescription.Text = description;
        statusColor = color;
        statusKind = kind;
        statusBadge.Invalidate();
    }

    private void PaintStatusBadge(object sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (Brush background = new SolidBrush(Color.FromArgb(22, statusColor)))
            e.Graphics.FillEllipse(background, 1, 1, 37, 37);
        using (Pen pen = new Pen(statusColor, 2.4F))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            if (statusKind == "ready" || statusKind == "success")
                e.Graphics.DrawLines(pen, new PointF[] { new PointF(11, 20), new PointF(17, 26), new PointF(28, 14) });
            else if (statusKind == "error")
            {
                e.Graphics.DrawLine(pen, 20, 11, 20, 22);
                e.Graphics.DrawLine(pen, 20, 27, 20, 27.5F);
            }
            else if (statusKind == "busy")
                e.Graphics.DrawArc(pen, 10, 10, 19, 19, -70, 270);
            else
            {
                e.Graphics.DrawLine(pen, 20, 18, 20, 28);
                e.Graphics.DrawLine(pen, 20, 11, 20, 11.5F);
            }
        }
    }

    private void AppendLog(string text)
    {
        if (String.IsNullOrWhiteSpace(text)) return;
        log.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ").AppendLine(text).AppendLine();
        logLink.Enabled = !busy;
    }

    private void ShowLog()
    {
        using (Form dialog = new Form())
        {
            dialog.Text = "修改器安装日志";
            dialog.Font = Font;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.Size = new Size(800, 500);
            dialog.MinimumSize = new Size(580, 360);
            dialog.BackColor = BackColor;
            dialog.Padding = new Padding(16);
            dialog.ShowInTaskbar = false;
            TextBox text = new TextBox();
            text.Multiline = true;
            text.ReadOnly = true;
            text.ScrollBars = ScrollBars.Both;
            text.WordWrap = false;
            text.BackColor = Color.White;
            text.Font = new Font("Consolas", 10F);
            text.Dock = DockStyle.Fill;
            text.Text = log.ToString();
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Bottom;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Height = 48;
            buttons.Padding = new Padding(0, 10, 0, 0);
            Button close = CreateButton("关闭", false);
            close.Size = new Size(88, 34);
            close.DialogResult = DialogResult.OK;
            Button copy = CreateButton("复制日志", false);
            copy.Size = new Size(104, 34);
            copy.Margin = new Padding(0, 0, 10, 0);
            copy.Click += delegate
            {
                try { Clipboard.SetText(text.Text.Length > 0 ? text.Text : "暂无日志。"); }
                catch { MessageBox.Show(dialog, "暂时无法复制到剪贴板。您可以直接选中日志文字后复制。", "复制日志", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            buttons.Controls.Add(close);
            buttons.Controls.Add(copy);
            dialog.Controls.Add(text);
            dialog.Controls.Add(buttons);
            dialog.AcceptButton = close;
            dialog.CancelButton = close;
            dialog.ShowDialog(this);
        }
    }

    private void OpenGameDirectory()
    {
        string path = GetNormalizedPath(pathBox.Text);
        if (path == null) { ValidateCurrentPath(); return; }
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception ex)
        {
            AppendLog("打开游戏目录失败：" + ex.Message);
            MessageBox.Show(this, "无法打开游戏目录。\r\n" + ex.Message, "打开游戏目录", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void WindowClosing(object sender, FormClosingEventArgs e)
    {
        if (!operationRunning) return;
        e.Cancel = true;
        MessageBox.Show(this, "正在处理游戏文件，请等待操作完成后再关闭窗口。", "操作尚未完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && pathTimer != null) pathTimer.Dispose();
        base.Dispose(disposing);
    }

    private sealed class CardPanel : Panel
    {
        internal CardPanel()
        {
            BackColor = Color.White;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(Color.FromArgb(222, 229, 239)))
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }
    }
}
