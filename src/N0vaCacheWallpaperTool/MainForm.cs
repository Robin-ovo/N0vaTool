using System.Diagnostics;

namespace N0vaCacheWallpaperTool;

internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(17, 22, 30);
    private static readonly Color Surface = Color.FromArgb(27, 35, 47);
    private static readonly Color SurfaceRaised = Color.FromArgb(37, 47, 62);
    private static readonly Color Accent = Color.FromArgb(76, 201, 178);
    private static readonly Color TextColor = Color.FromArgb(238, 243, 249);
    private static readonly Color Muted = Color.FromArgb(159, 174, 192);

    private readonly ProfileStore _store = new();
    private readonly SettingsStore _settings = new();
    private N0vaCacheService _cache = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _title = CreateTextBox();
    private readonly ComboBox _game = CreateComboBox();
    private readonly TextBox _author = CreateTextBox();
    private readonly ComboBox _type = CreateComboBox();
    private readonly TextBox _source = CreateTextBox();
    private readonly TextBox _installationPath = CreateTextBox();
    private readonly Label _status = new();
    private readonly CheckBox _launchAfterApply = new();
    private readonly Button _applyButton;
    private Button _restoreButton = null!;
    private readonly Button _saveButton;
    private string? _selectedId;
    private bool _refreshingGrid;
    private bool _busy;

    public MainForm()
    {
        Text = "N0va 缓存壁纸工具";
        Width = 1160;
        Height = 720;
        MinimumSize = new Size(980, 620);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Normal;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Background;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 9.5f);

        Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1400, 900);
        Size = new Size(
            Math.Min(1400, Math.Max(MinimumSize.Width, workingArea.Width - 80)),
            Math.Min(820, Math.Max(MinimumSize.Height, workingArea.Height - 80)));

        Panel header = BuildHeader();
        BuildFooter();

        Panel content = new()
        {
            Name = "contentPanel",
            Dock = DockStyle.Fill,
            BackColor = Background
        };
        Controls.Add(content);
        content.BringToFront();

        Panel leftPanel = new()
        {
            Name = "leftPanel",
            BackColor = Background,
            Padding = new Padding(20, 18, 12, 12)
        };
        Panel rightPanel = new()
        {
            Name = "rightPanel",
            Width = 400,
            BackColor = Background,
            Padding = new Padding(12, 18, 20, 12)
        };
        content.Controls.Add(leftPanel);
        content.Controls.Add(rightPanel);
        void LayoutColumns()
        {
            int rightWidth = Math.Clamp((int)(content.ClientSize.Width * 0.29), 350, 430);
            leftPanel.SetBounds(
                0,
                0,
                Math.Max(500, content.ClientSize.Width - rightWidth),
                content.ClientSize.Height);
            rightPanel.SetBounds(
                leftPanel.Right,
                0,
                Math.Max(1, content.ClientSize.Width - leftPanel.Right),
                content.ClientSize.Height);
            rightPanel.BringToFront();
        }
        content.Resize += (_, _) => LayoutColumns();
        Shown += (_, _) => LayoutColumns();

        BuildGrid(leftPanel);
        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.None,
            Height = 52,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Background,
            Padding = new Padding(0, 10, 0, 0)
        };
        leftPanel.Controls.Add(actions);

        Button importButton = CreateButton("导入 MP4", 108, true);
        importButton.Click += (_, _) => ImportVideo();
        actions.Controls.Add(importButton);

        _saveButton = CreateButton("保存信息", 104, false);
        _saveButton.Click += (_, _) => SaveCurrentProfile(showConfirmation: true);
        actions.Controls.Add(_saveButton);

        _applyButton = CreateButton("替换到人工桌面", 146, true);
        _applyButton.Click += (_, _) => ApplySelected();
        actions.Controls.Add(_applyButton);

        Button deleteButton = CreateButton("删除记录", 104, false);
        deleteButton.Click += (_, _) => DeleteSelected();
        actions.Controls.Add(deleteButton);

        void LayoutLeftPanel()
        {
            int width = Math.Max(1, leftPanel.ClientSize.Width - 32);
            _grid.SetBounds(20, 18, width, Math.Max(1, leftPanel.ClientSize.Height - 148));
            actions.SetBounds(
                20,
                Math.Max(18, leftPanel.ClientSize.Height - 120),
                width,
                52);
            actions.BringToFront();
        }
        leftPanel.Resize += (_, _) => LayoutLeftPanel();
        Shown += (_, _) => LayoutLeftPanel();

        BuildEditor(rightPanel);
        DetectInstallation(userInitiated: false);
        RefreshGrid(_store.LastAppliedId);

        string? layoutLog = Environment.GetEnvironmentVariable("N0VA_CACHE_TOOL_LAYOUT_LOG");
        if (!string.IsNullOrWhiteSpace(layoutLog))
        {
            Shown += (_, _) => WriteLayoutDiagnostics(layoutLog);
        }
    }

    private Panel BuildHeader()
    {
        Panel header = new()
        {
            Dock = DockStyle.Top,
            Height = 88,
            BackColor = Surface,
            Padding = new Padding(24, 17, 24, 12)
        };
        Controls.Add(header);

        Label title = new()
        {
            AutoSize = true,
            Text = "N0va 缓存壁纸工具",
            Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold),
            ForeColor = TextColor,
            Location = new Point(24, 14)
        };
        header.Controls.Add(title);

        Label subtitle = new()
        {
            AutoSize = true,
            Text = "自动定位当前官方动态缓存槽，一键生成 NDF 并保留可恢复备份",
            ForeColor = Muted,
            Location = new Point(26, 53)
        };
        header.Controls.Add(subtitle);

        header.BringToFront();
        return header;
    }

    private void BuildGrid(Control parent)
    {
        _grid.Dock = DockStyle.None;
        _grid.BackgroundColor = Surface;
        _grid.BorderStyle = BorderStyle.None;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceRaised;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextColor;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceRaised;
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6);
        _grid.DefaultCellStyle.BackColor = Surface;
        _grid.DefaultCellStyle.ForeColor = TextColor;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(49, 91, 88);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.DefaultCellStyle.Padding = new Padding(5);
        _grid.GridColor = Color.FromArgb(54, 67, 84);
        _grid.RowTemplate.Height = 38;
        DataGridViewColumn titleColumn = _grid.Columns[_grid.Columns.Add("title", "标题")];
        DataGridViewColumn gameColumn = _grid.Columns[_grid.Columns.Add("game", "游戏")];
        DataGridViewColumn authorColumn = _grid.Columns[_grid.Columns.Add("author", "作者")];
        DataGridViewColumn typeColumn = _grid.Columns[_grid.Columns.Add("type", "类型")];
        DataGridViewColumn appliedColumn = _grid.Columns[_grid.Columns.Add("applied", "最近应用")];
        titleColumn.FillWeight = 30;
        gameColumn.FillWeight = 20;
        authorColumn.FillWeight = 18;
        typeColumn.FillWeight = 12;
        appliedColumn.FillWeight = 20;
        _grid.SelectionChanged += (_, _) => LoadSelectedIntoEditor();
        parent.Controls.Add(_grid);
    }

    private void BuildEditor(Control parent)
    {
        FlowLayoutPanel editor = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Background,
            Padding = new Padding(8, 0, 0, 0),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };
        parent.Controls.Add(editor);

        Label installHeading = new()
        {
            Text = "人工桌面安装位置",
            AutoSize = true,
            ForeColor = TextColor,
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 10)
        };
        editor.Controls.Add(installHeading);

        _installationPath.ReadOnly = true;
        AddField(editor, "N0vaDesktop.exe 所在目录", _installationPath, 58);

        FlowLayoutPanel installButtons = new()
        {
            Dock = DockStyle.Fill,
            Height = 45,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Background
        };
        editor.Controls.Add(installButtons);

        Button chooseInstallButton = CreateButton("选择安装目录", 132, true);
        chooseInstallButton.Click += (_, _) => SelectInstallationDirectory();
        installButtons.Controls.Add(chooseInstallButton);

        Button autoDetectButton = CreateButton("自动检测", 108, false);
        autoDetectButton.Click += (_, _) => DetectInstallation(userInitiated: true);
        installButtons.Controls.Add(autoDetectButton);

        Label heading = new()
        {
            Text = "壁纸信息",
            AutoSize = true,
            ForeColor = TextColor,
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            Margin = new Padding(0, 10, 0, 14)
        };
        editor.Controls.Add(heading);

        AddField(editor, "标题", _title, 58);
        AddField(editor, "游戏", _game, 58);
        AddField(editor, "作者", _author, 58);
        AddField(editor, "壁纸类型", _type, 58);
        AddField(editor, "MP4 文件", _source, 94);

        _game.Items.AddRange(new object[]
        {
            "原神", "崩坏：星穹铁道", "崩坏3", "绝区零",
            "崩坏学园2", "yoyo鹿鸣", "米游社", "HOYO-MiX", "自定义游戏"
        });
        _game.DropDownStyle = ComboBoxStyle.DropDown;
        _type.Items.AddRange(new object[] { "动态" });
        _type.DropDownStyle = ComboBoxStyle.DropDownList;
        _source.ReadOnly = true;

        Label note = new()
        {
            AutoSize = false,
            Size = new Size(321, 118),
            Text = "标题、游戏、作者和类型保存在工具库；N0va 卡片字段来自私有缓存，" +
                   "当前暂不能改写。",
            ForeColor = Color.FromArgb(224, 185, 98),
            BackColor = Color.FromArgb(43, 38, 26),
            Padding = new Padding(10),
            Margin = new Padding(0, 10, 0, 10)
        };
        editor.Controls.Add(note);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            Height = 45,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Background
        };
        editor.Controls.Add(buttons);

        _restoreButton = CreateButton("恢复官方视频", 132, false);
        _restoreButton.Click += (_, _) => RestoreOfficial();
        buttons.Controls.Add(_restoreButton);

        Button folderButton = CreateButton("打开数据目录", 128, false);
        folderButton.Click += (_, _) => OpenDataDirectory();
        buttons.Controls.Add(folderButton);

        FlowLayoutPanel logButtons = new()
        {
            Dock = DockStyle.Fill,
            Height = 45,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Background
        };
        editor.Controls.Add(logButtons);

        Button openLogsButton = CreateButton("打开日志", 108, false);
        openLogsButton.Click += (_, _) => OpenLogDirectory();
        logButtons.Controls.Add(openLogsButton);

        Button deleteLogsButton = CreateButton("删除日志", 108, false);
        deleteLogsButton.Click += (_, _) => DeleteLogs();
        logButtons.Controls.Add(deleteLogsButton);
    }

    private void SelectInstallationDirectory()
    {
        using FolderBrowserDialog picker = new()
        {
            Description = "请选择包含 N0vaDesktop.exe 的人工桌面安装目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(_cache.CurrentHostDirectory)
                ? _cache.CurrentHostDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;

        if (!N0vaInstallationLocator.TryNormalizeHostDirectory(
                picker.SelectedPath,
                out string hostDirectory))
        {
            ToolLogger.Warning(
                $"手动选择目录无效，没有 N0vaDesktop.exe：{picker.SelectedPath}");
            MessageBox.Show(
                this,
                "所选目录中没有找到 N0vaDesktop.exe。\n\n" +
                "请选择真正包含 N0vaDesktop.exe 的目录，而不是快捷方式所在目录。",
                "安装目录无效",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _settings.SetN0vaInstallDirectory(hostDirectory);
        _cache = new N0vaCacheService(
            hostDirectory,
            manageProcesses: true,
            validateVersion: true);
        _installationPath.Text = hostDirectory;
        N0vaInstallationStatus status = _cache.GetInstallationStatus();
        ToolLogger.Info(
            $"用户手动选择安装目录：{hostDirectory}；状态：{status.Summary}");
        ShowInstallationStatus(status);
        UpdateStatus();
        UpdateActionState();
    }

    private void DetectInstallation(bool userInitiated)
    {
        if (userInitiated)
        {
            _settings.SetN0vaInstallDirectory(null);
            ToolLogger.Info("用户触发自动检测，已清除手动安装路径。");
        }

        IReadOnlyList<N0vaInstallationCandidate> candidates =
            N0vaInstallationLocator.FindCandidates(
                _settings.N0vaInstallDirectory);

        N0vaCacheService? selected = null;
        N0vaCacheService? compatibleFallback = null;
        N0vaCacheService? incompatibleFallback = null;
        foreach (N0vaInstallationCandidate candidate in candidates)
        {
            N0vaCacheService service = new(
                candidate.HostDirectory,
                manageProcesses: true,
                validateVersion: true);
            N0vaInstallationStatus status = service.GetInstallationStatus();
            ToolLogger.Info(
                $"安装检测（{candidate.Source}）：{candidate.HostDirectory}；" +
                $"版本={status.Version}；结果={status.Summary}");

            if (status.IsReady)
            {
                selected = service;
                break;
            }
            if (status.VersionCompatible)
            {
                compatibleFallback ??= service;
            }
            else
            {
                incompatibleFallback ??= service;
            }
        }

        _cache = selected ?? compatibleFallback ?? incompatibleFallback ?? new N0vaCacheService();
        _installationPath.Text = File.Exists(
            Path.Combine(_cache.CurrentHostDirectory, "N0vaDesktop.exe"))
            ? _cache.CurrentHostDirectory
            : "未找到，请点击“选择安装目录”";

        N0vaInstallationStatus finalStatus = _cache.GetInstallationStatus();
        ToolLogger.Info(
            $"最终安装目录：{_cache.CurrentHostDirectory}；状态：{finalStatus.Summary}");

        UpdateStatus();
        UpdateActionState();
        if (userInitiated) ShowInstallationStatus(finalStatus);
    }

    private void ShowInstallationStatus(N0vaInstallationStatus status)
    {
        ToolLogger.Info(
            $"显示安装状态：{status.Issue}；{status.HostDirectory}；{status.Version}");
        MessageBox.Show(
            this,
            status.UserMessage,
            status.IsReady ? "N0va Desktop 已就绪" : "N0va Desktop 检测结果",
            MessageBoxButtons.OK,
            status.IsReady ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void BuildFooter()
    {
        Panel footer = new()
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            BackColor = Surface,
            Padding = new Padding(22, 8, 22, 7)
        };
        Controls.Add(footer);
        footer.BringToFront();

        _status.AutoSize = true;
        _status.ForeColor = Muted;
        _status.Cursor = Cursors.Hand;
        _status.Location = new Point(22, 17);
        _status.Click += (_, _) =>
        {
            if (_status.Tag is not string message || string.IsNullOrWhiteSpace(message)) return;
            MessageBox.Show(
                this,
                message,
                "N0va Desktop 状态",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };
        footer.Controls.Add(_status);

        _launchAfterApply.AutoSize = true;
        _launchAfterApply.Text = "替换后启动人工桌面";
        _launchAfterApply.Checked = true;
        _launchAfterApply.ForeColor = Muted;
        _launchAfterApply.BackColor = Color.Transparent;
        _launchAfterApply.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _launchAfterApply.Location = new Point(Width - 250, 16);
        footer.Controls.Add(_launchAfterApply);
        footer.Resize += (_, _) => _launchAfterApply.Left = footer.ClientSize.Width - 238;
    }

    private static void AddField(
        FlowLayoutPanel container,
        string label,
        Control control,
        int height)
    {
        Panel group = new()
        {
            Dock = DockStyle.Top,
            Height = height,
            BackColor = Background,
            Margin = new Padding(0, 0, 0, 6)
        };
        Label caption = new()
        {
            Text = label,
            AutoSize = true,
            ForeColor = Muted,
            Location = new Point(0, 0)
        };
        control.Location = new Point(0, 24);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        control.Width = 310;
        group.Controls.Add(caption);
        group.Controls.Add(control);
        group.Resize += (_, _) => control.Width = group.ClientSize.Width;
        void ResizeGroup() => group.Width = Math.Max(
            220,
            container.ClientSize.Width -
            SystemInformation.VerticalScrollBarWidth -
            10);
        container.SizeChanged += (_, _) => ResizeGroup();
        ResizeGroup();
        container.Controls.Add(group);
    }

    private void ImportVideo()
    {
        using OpenFileDialog picker = new()
        {
            Title = "选择动态壁纸 MP4",
            Filter = "MP4 视频 (*.mp4)|*.mp4|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            UseWaitCursor = true;
            NdfBuilder.ValidateSource(picker.FileName);
            string id = Guid.NewGuid().ToString("N");
            string storedPath = Path.Combine(ToolPaths.MediaDirectory, id + ".mp4");
            File.Copy(picker.FileName, storedPath, false);
            WallpaperProfile profile = new()
            {
                Id = id,
                Title = Path.GetFileNameWithoutExtension(picker.FileName),
                Game = "原神",
                Author = "自定义",
                Type = "动态",
                SourcePath = storedPath,
                OriginalFileName = Path.GetFileName(picker.FileName),
                SourceLength = new FileInfo(storedPath).Length
            };
            _store.Add(profile);
            _selectedId = profile.Id;
            RefreshGrid(profile.Id);
        }
        catch (Exception ex)
        {
            ToolLogger.Error("导入 MP4 失败", ex);
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void SaveCurrentProfile(bool showConfirmation)
    {
        WallpaperProfile? profile = GetSelectedProfile();
        if (profile is null)
        {
            MessageBox.Show(
                this,
                "壁纸库中还没有选中的记录。请先点击左下角“导入 MP4”。",
                "没有选中壁纸",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(_title.Text))
        {
            MessageBox.Show(this, "标题不能为空。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        profile.Title = _title.Text.Trim();
        profile.Game = _game.Text.Trim();
        profile.Author = _author.Text.Trim();
        profile.Type = string.IsNullOrWhiteSpace(_type.Text) ? "动态" : _type.Text;
        _store.Update(profile);
        RefreshGrid(profile.Id);

        if (showConfirmation)
        {
            SetStatus("壁纸信息已保存");
        }
    }

    private void ApplySelected()
    {
        SaveCurrentProfile(false);
        if (GetSelectedProfile() is not { } profile)
        {
            MessageBox.Show(
                this,
                "壁纸库还是空的。请先点击左下角“导入 MP4”，选中列表中的壁纸，再点击替换。",
                "先导入壁纸",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!string.Equals(profile.Type, "动态", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "当前缓存槽只支持动态 MP4 壁纸。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            SetBusy(true, "正在生成 NDF 并替换 N0va 缓存…");
            CacheOperationResult result = _cache.Apply(profile);
            _store.MarkApplied(profile.Id);
            RefreshGrid(profile.Id);
            SetStatus(
                $"替换完成 · {result.SlotName} · {result.DiscoverySource}");
            if (_launchAfterApply.Checked)
            {
                TryLaunchN0va("替换完成，但自动启动失败。请从桌面手动启动人工桌面。");
            }
            else
            {
                MessageBox.Show(
                    this,
                    "替换完成。请从桌面或托盘正常启动人工桌面。",
                    "完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            ToolLogger.Error("替换壁纸失败：没有写入权限", ex);
            MessageBox.Show(
                this,
                "没有写入人工桌面目录的权限。请以管理员身份运行本工具。",
                "权限不足",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            ToolLogger.Error("替换壁纸失败", ex);
            MessageBox.Show(this, ex.Message, "替换失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RestoreOfficial()
    {
        DialogResult answer = MessageBox.Show(
            this,
            "恢复到当前缓存槽替换前的官方备份？\n当前替换视频会退出使用。",
            "恢复官方视频",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        try
        {
            SetBusy(true, "正在恢复官方缓存视频…");
            CacheOperationResult result = _cache.RestoreOfficial();
            SetStatus($"官方视频已恢复 · {result.SlotName}");
            if (_launchAfterApply.Checked)
            {
                TryLaunchN0va("已恢复，但自动启动失败。请从桌面手动启动人工桌面。");
            }
            else
            {
                MessageBox.Show(
                    this,
                    "已恢复。请从桌面或托盘正常启动人工桌面。",
                    "恢复完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            ToolLogger.Error("恢复官方缓存失败", ex);
            MessageBox.Show(this, ex.Message, "恢复失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void DeleteSelected()
    {
        WallpaperProfile? profile = GetSelectedProfile();
        if (profile is null)
        {
            MessageBox.Show(
                this,
                "请先点击列表中的一条壁纸记录。",
                "没有选中壁纸",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
                this,
                $"删除工具库中的“{profile.Title}”？\nN0va 当前正在使用的缓存不会被删除。",
                "删除记录",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _store.Remove(profile.Id);
        _selectedId = null;
        RefreshGrid(null);
    }

    private void RefreshGrid(string? selectId)
    {
        _refreshingGrid = true;
        try
        {
            _grid.Rows.Clear();
            DataGridViewRow? wanted = null;
            foreach (WallpaperProfile profile in _store.Snapshot().OrderByDescending(item => item.ImportedAt))
            {
                string applied = profile.LastAppliedAt?.ToString("MM-dd HH:mm") ?? "";
                int index = _grid.Rows.Add(
                    profile.Title,
                    profile.Game,
                    profile.Author,
                    profile.Type,
                    applied);
                _grid.Rows[index].Tag = profile.Id;
                if (profile.Id == selectId)
                {
                    wanted = _grid.Rows[index];
                }
            }

            wanted ??= _grid.Rows.Count > 0 ? _grid.Rows[0] : null;
            _grid.ClearSelection();
            if (wanted is not null)
            {
                wanted.Selected = true;
                _grid.CurrentCell = wanted.Cells[0];
                _selectedId = wanted.Tag as string;
            }
            else
            {
                _selectedId = null;
            }
        }
        finally
        {
            _refreshingGrid = false;
        }

        LoadSelectedIntoEditor();
        UpdateStatus();
    }

    private void LoadSelectedIntoEditor()
    {
        if (_refreshingGrid) return;
        DataGridViewRow? row = _grid.SelectedRows.Count > 0
            ? _grid.SelectedRows[0]
            : _grid.CurrentRow;
        if (row?.Tag is not string id)
        {
            _selectedId = null;
            _title.Clear();
            _game.Text = "";
            _author.Clear();
            _type.SelectedIndex = -1;
            _source.Clear();
            UpdateActionState();
            return;
        }
        WallpaperProfile? profile = _store.Find(id);
        if (profile is null) return;
        _selectedId = id;
        _title.Text = profile.Title;
        _game.Text = profile.Game;
        _author.Text = profile.Author;
        _type.SelectedItem = profile.Type;
        _source.Text = profile.SourcePath;
        UpdateActionState();
    }

    private void UpdateStatus()
    {
        N0vaInstallationStatus installation = _cache.GetInstallationStatus();
        _status.Tag = installation.UserMessage;
        if (!installation.IsReady)
        {
            _status.Text = installation.Summary;
            _status.ForeColor = Color.FromArgb(245, 120, 120);
            UpdateActionState();
            return;
        }

        try
        {
            _status.Text = _cache.DescribeCurrentSlot();
            _status.ForeColor = Muted;
        }
        catch (Exception ex)
        {
            _status.Text = "状态读取失败：" + ex.Message;
            _status.ForeColor = Color.FromArgb(245, 120, 120);
        }
        UpdateActionState();
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _status.ForeColor = Accent;
    }

    private void TryLaunchN0va(string failureMessage)
    {
        try
        {
            _cache.LaunchN0va();
        }
        catch
        {
            ToolLogger.Warning("替换完成后自动启动人工桌面失败。");
            MessageBox.Show(
                this,
                failureMessage,
                "启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        UseWaitCursor = busy;
        UpdateActionState();
        if (!string.IsNullOrWhiteSpace(message))
        {
            SetStatus(message);
        }
    }

    private WallpaperProfile? GetSelectedProfile()
    {
        string? id = _selectedId;
        if (string.IsNullOrWhiteSpace(id) && _grid.CurrentRow?.Tag is string currentId)
        {
            id = currentId;
            _selectedId = id;
        }
        return string.IsNullOrWhiteSpace(id) ? null : _store.Find(id);
    }

    private void UpdateActionState()
    {
        bool hasSelection = !string.IsNullOrWhiteSpace(_selectedId);
        bool installationReady = _cache.GetInstallationStatus().IsReady;
        _saveButton.Enabled = !_busy && hasSelection;
        _applyButton.Enabled = !_busy && hasSelection && installationReady;
        _restoreButton.Enabled = !_busy && installationReady;
    }

    private static void OpenDataDirectory()
    {
        Directory.CreateDirectory(ToolPaths.Root);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + ToolPaths.Root + "\"",
            UseShellExecute = true
        });
    }

    private static void OpenLogDirectory()
    {
        Directory.CreateDirectory(ToolPaths.LogDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + ToolPaths.LogDirectory + "\"",
            UseShellExecute = true
        });
    }

    private void DeleteLogs()
    {
        if (MessageBox.Show(
                this,
                "删除程序 logs 文件夹中的所有日志？\n\n" +
                "不会删除壁纸记录、导入的视频或官方缓存备份。",
                "删除日志",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            int deleted = ToolLogger.DeleteLogs();
            _status.Text = $"已删除 {deleted} 个日志文件";
            _status.ForeColor = Accent;
            _status.Tag = $"日志目录：{ToolPaths.LogDirectory}\n已删除：{deleted} 个文件";
            MessageBox.Show(
                this,
                $"已删除 {deleted} 个日志文件。\n\n{ToolPaths.LogDirectory}",
                "删除完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "删除日志失败：" + ex.Message,
                "删除日志",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static TextBox CreateTextBox()
    {
        return new TextBox
        {
            BackColor = SurfaceRaised,
            ForeColor = TextColor,
            BorderStyle = BorderStyle.FixedSingle,
            Height = 31
        };
    }

    private static ComboBox CreateComboBox()
    {
        return new ComboBox
        {
            BackColor = SurfaceRaised,
            ForeColor = TextColor,
            FlatStyle = FlatStyle.Flat,
            Height = 31
        };
    }

    private static Button CreateButton(string text, int width, bool primary)
    {
        Button button = new()
        {
            Text = text,
            Width = width,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : SurfaceRaised,
            ForeColor = primary ? Color.FromArgb(10, 34, 31) : TextColor,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 0)
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(68, 84, 104);
        return button;
    }

    private void WriteLayoutDiagnostics(string path)
    {
        List<string> lines = [];
        void Visit(Control control, int depth)
        {
            lines.Add(
                new string(' ', depth * 2) +
                $"{control.GetType().Name} name={control.Name} " +
                $"bounds={control.Bounds} visible={control.Visible} dock={control.Dock}");
            foreach (Control child in control.Controls)
            {
                Visit(child, depth + 1);
            }
        }
        Visit(this, 0);
        File.WriteAllLines(path, lines);
    }
}
