using System.Diagnostics;
using ArchiveChainTool.Batch;
using ArchiveChainTool.Storage;
using ArchiveChainTool.UI;
using ArchiveChainTool.Workflows;

namespace ArchiveChainTool;

internal sealed class MainForm : Form
{
    private readonly string _baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
    private readonly TextBox _output = new();
    private readonly CheckBox _automatic = new() { Text = "开启连续自动检测和逐层解压", AutoSize = true };
    private readonly NumericUpDown _maxLayers = new() { Minimum = 1, Maximum = 100, Value = 10, Width = 64 };
    private readonly RadioButton _keepAll = new() { Text = "保留全部中间文件", Checked = true, AutoSize = true };
    private readonly RadioButton _deleteOnSuccess = new() { Text = "成功后自动删除中间文件", AutoSize = true };
    private readonly TextBox _workflowName = new() { Width = 150 };
    private readonly TextBox _workflowDescription = new() { Width = 220 };
    private readonly DataGridView _queue = new();
    private readonly DataGridView _steps = new();
    private readonly StepEditorPanel _stepEditor = new();
    private readonly TextBox _log = new();
    private readonly Button _run = new() { Text = "开始批处理", Width = 110 };
    private readonly Button _cancel = new() { Text = "取消全部", Width = 90, Enabled = false };
    private readonly List<string> _inputs = [];
    private readonly PasswordManagerControl _passwordManager;
    private readonly TemplateStore _templateStore;
    private readonly ListBox _templateList = new();
    private readonly TextBox _templatePreview = new();
    private IReadOnlyList<TemplateDescriptor> _templates = [];
    private WorkflowDefinition _workflow = BuiltInTemplates.FixedFourLayer();
    private CancellationTokenSource? _cancellation;

    public MainForm()
    {
        _passwordManager = new PasswordManagerControl(_baseDirectory);
        _templateStore = new TemplateStore(_baseDirectory);
        Text = "熠键解压 v3.1";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 720);
        Size = new Size(1280, 850);
        Font = new Font("Microsoft YaHei UI", 9F);
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildWorkflowPage());
        tabs.TabPages.Add(BuildTemplatePage());
        tabs.TabPages.Add(new TabPage("密码库") { Controls = { _passwordManager } });
        Controls.Add(tabs);

        var parent = Directory.GetParent(_baseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        _output.Text = parent?.FullName ?? _baseDirectory;
        RefreshSteps();
        ReloadTemplates();
        BindWorkflowToUi();
    }

    private TabPage BuildWorkflowPage()
    {
        var page = new TabPage("工作流与批处理") { AllowDrop = true };
        page.DragEnter += OnDragEnter;
        page.DragDrop += OnDragDrop;

        var options = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(8), AutoScroll = true };
        options.Controls.Add(new Label { Text = "工作流名称", AutoSize = true, Margin = new Padding(2, 7, 2, 0) });
        options.Controls.Add(_workflowName);
        options.Controls.Add(new Label { Text = "说明", AutoSize = true, Margin = new Padding(8, 7, 2, 0) });
        options.Controls.Add(_workflowDescription);
        options.Controls.Add(Button("新建工作流", (_, _) => NewWorkflow()));
        options.Controls.Add(Button("保存工作流模板", (_, _) => SaveCurrentTemplate()));
        options.Controls.Add(_automatic);
        options.Controls.Add(new Label { Text = "最大层数", AutoSize = true, Margin = new Padding(16, 7, 2, 0) });
        options.Controls.Add(_maxLayers);
        var cleanupGroup = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(12, 0, 4, 0) };
        cleanupGroup.Controls.Add(new Label { Text = "中间文件：", AutoSize = true, Margin = new Padding(0, 7, 2, 0) });
        cleanupGroup.Controls.Add(_keepAll);
        cleanupGroup.Controls.Add(_deleteOnSuccess);
        options.Controls.Add(cleanupGroup);
        options.Controls.Add(new Label { Text = "输出根目录", AutoSize = true, Margin = new Padding(16, 7, 2, 0) });
        _output.Width = 320;
        options.Controls.Add(_output);
        options.Controls.Add(Button("浏览...", (_, _) => BrowseOutput()));
        options.Controls.Add(Button("添加文件", (_, _) => BrowseInputs()));
        options.Controls.Add(Button("清空队列", (_, _) => { _inputs.Clear(); RefreshQueue(); }));
        _automatic.CheckedChanged += (_, _) => { _workflow.ExecutionMode = _automatic.Checked ? ExecutionMode.AutomaticChain : ExecutionMode.CustomWorkflow; _steps.Enabled = _stepEditor.Enabled = !_automatic.Checked; };
        _maxLayers.ValueChanged += (_, _) => _workflow.Automatic.MaxLayers = (int)_maxLayers.Value;
        _workflowName.TextChanged += (_, _) => _workflow.Name = _workflowName.Text;
        _workflowDescription.TextChanged += (_, _) => _workflow.Description = _workflowDescription.Text;
        _keepAll.CheckedChanged += (_, _) => { if (_keepAll.Checked) _workflow.KeepIntermediateFiles = true; };
        _deleteOnSuccess.CheckedChanged += (_, _) => { if (_deleteOnSuccess.Checked) _workflow.KeepIntermediateFiles = false; };

        ConfigureQueue();
        var queueGroup = new GroupBox { Text = "输入队列（可拖入多个文件或文件夹）", Dock = DockStyle.Top, Height = 190, Padding = new Padding(8) };
        queueGroup.Controls.Add(_queue);

        ConfigureSteps();
        var stepButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
        stepButtons.Controls.Add(Button("+ 解压", (_, _) => AddStep(new ExtractStepDefinition { Name = "解压步骤" })));
        stepButtons.Controls.Add(Button("+ 重命名", (_, _) => AddStep(new RenameStepDefinition { Name = "重命名步骤" })));
        stepButtons.Controls.Add(Button("删除", (_, _) => DeleteStep()));
        stepButtons.Controls.Add(Button("上移", (_, _) => MoveStep(-1)));
        stepButtons.Controls.Add(Button("下移", (_, _) => MoveStep(1)));
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 700 };
        split.Panel1.Controls.Add(_steps);
        split.Panel1.Controls.Add(stepButtons);
        split.Panel2.Controls.Add(_stepEditor);

        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Color.White;
        var runPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        _run.Click += RunBatch_Click;
        _cancel.Click += (_, _) => _cancellation?.Cancel();
        runPanel.Controls.Add(_run);
        runPanel.Controls.Add(_cancel);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 180 };
        bottom.Controls.Add(_log);
        bottom.Controls.Add(runPanel);

        page.Controls.Add(split);
        page.Controls.Add(bottom);
        page.Controls.Add(queueGroup);
        page.Controls.Add(options);
        return page;
    }

    private TabPage BuildTemplatePage()
    {
        var page = new TabPage("模板") { Padding = new Padding(8) };
        _templateList.Dock = DockStyle.Left;
        _templateList.Width = 320;
        _templateList.SelectedIndexChanged += (_, _) => PreviewTemplate();
        _templatePreview.Dock = DockStyle.Fill;
        _templatePreview.Multiline = true;
        _templatePreview.ReadOnly = true;
        _templatePreview.ScrollBars = ScrollBars.Vertical;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46 };
        buttons.Controls.Add(Button("保存当前方案为模板", (_, _) => SaveCurrentTemplate()));
        buttons.Controls.Add(Button("应用所选模板", (_, _) => ApplyTemplate()));
        buttons.Controls.Add(Button("复制为新模板", (_, _) => CopyTemplate()));
        buttons.Controls.Add(Button("重命名", (_, _) => RenameTemplate()));
        buttons.Controls.Add(Button("导入", (_, _) => ImportTemplate()));
        buttons.Controls.Add(Button("导出", (_, _) => ExportTemplate()));
        buttons.Controls.Add(Button("删除", (_, _) => DeleteTemplate()));
        page.Controls.Add(_templatePreview);
        page.Controls.Add(_templateList);
        page.Controls.Add(buttons);
        return page;
    }

    private static Button Button(string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += handler;
        return button;
    }

    private void ConfigureQueue()
    {
        _queue.Dock = DockStyle.Fill;
        _queue.AllowUserToAddRows = false;
        _queue.ReadOnly = true;
        _queue.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _queue.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _queue.Columns.Add("number", "序号");
        _queue.Columns.Add("input", "输入压缩包");
        _queue.Columns.Add("format", "检测格式");
        _queue.Columns.Add("status", "状态");
        _queue.Columns.Add("result", "结果/错误");
        _queue.Columns[0].FillWeight = 25;
        _queue.Columns[2].FillWeight = 45;
        _queue.Columns[3].FillWeight = 55;
    }

    private void ConfigureSteps()
    {
        _steps.Dock = DockStyle.Fill;
        _steps.AllowUserToAddRows = false;
        _steps.ReadOnly = true;
        _steps.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _steps.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _steps.Columns.Add("number", "序号");
        _steps.Columns.Add("type", "操作");
        _steps.Columns.Add("name", "名称");
        _steps.Columns.Add("rule", "筛选规则");
        _steps.Columns[0].FillWeight = 25;
        _steps.Columns[1].FillWeight = 45;
        _steps.SelectionChanged += (_, _) => SelectStep();
        _stepEditor.StepChanged += (_, step) => UpdateStepRow(step);
        _stepEditor.SetPasswordEntries(_passwordManager.Entries);
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (_cancellation is not null) return;
        var paths = (string[]?)e.Data?.GetData(DataFormats.FileDrop) ?? [];
        try
        {
            AppendLog("正在扫描拖入内容……");
            var inputs = await InputBatchBuilder.BuildAsync(paths, CancellationToken.None);
            foreach (var path in inputs) if (!_inputs.Contains(path, StringComparer.OrdinalIgnoreCase)) _inputs.Add(path);
            RefreshQueue();
            AppendLog($"已加入 {inputs.Count} 个可识别压缩包。");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "拖放扫描失败"); }
    }

    private void OnDragEnter(object? sender, DragEventArgs e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;

    private void BrowseInputs()
    {
        using var dialog = new OpenFileDialog { Filter = "所有文件 (*.*)|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var path in dialog.FileNames) if (!_inputs.Contains(path, StringComparer.OrdinalIgnoreCase)) _inputs.Add(path);
        RefreshQueue();
    }

    private void BrowseOutput()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = _output.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK) _output.Text = dialog.SelectedPath;
    }

    private void RefreshQueue()
    {
        _queue.Rows.Clear();
        for (var i = 0; i < _inputs.Count; i++) _queue.Rows.Add(i + 1, _inputs[i], ArchiveInspector.DetectFormat(_inputs[i]), "等待", "");
    }

    private async void RunBatch_Click(object? sender, EventArgs e)
    {
        if (_inputs.Count == 0) { MessageBox.Show(this, "请添加或拖入至少一个压缩包。"); return; }
        try
        {
            Directory.CreateDirectory(_output.Text);
            _workflow.ExecutionMode = _automatic.Checked ? ExecutionMode.AutomaticChain : ExecutionMode.CustomWorkflow;
            _workflow.Automatic.MaxLayers = (int)_maxLayers.Value;
            _workflow.KeepIntermediateFiles = _keepAll.Checked;
            WorkflowStore.Validate(_workflow);
            SetRunning(true);
            _cancellation = new CancellationTokenSource();
            var sevenZip = Path.Combine(_baseDirectory, "tools", "7zip", "7z.exe");
            var successes = 0;
            var failures = new List<WorkflowRunResult>();
            for (var i = 0; i < _inputs.Count; i++)
            {
                if (_cancellation.IsCancellationRequested) break;
                SetQueueStatus(i, "运行中", "");
                try
                {
                    var engine = new WorkflowEngine(ChooseCandidateAsync, RequestPasswordAsync, _passwordManager.Entries);
                    var progress = new Progress<WorkflowProgress>(p => { SetQueueStatus(i, $"{p.StepIndex}/{p.StepCount}", p.Message); AppendLog($"[{i + 1}/{_inputs.Count}] {p.Message}"); });
                    var result = await engine.RunAsync(_workflow, _inputs[i], _output.Text, sevenZip, progress, _cancellation.Token);
                    if (!_workflow.KeepIntermediateFiles) PromoteAndClean(result);
                    SetQueueStatus(i, "成功", result.FinalArtifact);
                    successes++;
                }
                catch (OperationCanceledException) { SetQueueStatus(i, "已取消", ""); }
                catch (WorkflowRunException ex) { SetQueueStatus(i, "失败", ex.Message); failures.Add(new(ex.TaskDirectory, ex.TaskDirectory, ex.LogPath)); }
                catch (Exception ex) { SetQueueStatus(i, "失败", ex.Message); }
            }
            if (failures.Count > 0 && MessageBox.Show(this, $"有 {failures.Count} 个任务失败。是否清理失败任务的中间文件？\n\n选择“否”将保留现场。", "失败任务清理", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                foreach (var failure in failures) CleanTaskWork(failure.TaskDirectory);
            MessageBox.Show(this, $"批处理结束。成功 {successes}，失败 {_inputs.Count - successes}。", "批处理完成");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法开始", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _cancellation?.Dispose(); _cancellation = null; SetRunning(false); }
    }

    private static void PromoteAndClean(WorkflowRunResult result)
    {
        var resultDir = Path.Combine(result.TaskDirectory, "result");
        Directory.CreateDirectory(resultDir);
        var source = result.FinalArtifact;
        if (File.Exists(source)) File.Copy(source, Path.Combine(resultDir, Path.GetFileName(source)), false);
        else if (Directory.Exists(source)) CopyDirectory(source, Path.Combine(resultDir, Path.GetFileName(source)));
        foreach (var path in Directory.EnumerateFileSystemEntries(result.TaskDirectory).Where(path => Path.GetFileName(path) is not ("result" or "logs")))
        {
            if (File.Exists(path)) File.Delete(path); else Directory.Delete(path, true);
        }
    }

    private static void CleanTaskWork(string taskDirectory)
    {
        var full = Path.GetFullPath(taskDirectory);
        foreach (var path in Directory.EnumerateFileSystemEntries(full).Where(path => Path.GetFileName(path) is not ("logs" or "result")))
        {
            var candidate = Path.GetFullPath(path);
            if (!candidate.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(candidate)) File.Delete(candidate); else Directory.Delete(candidate, true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private void SetQueueStatus(int row, string status, string result) { _queue.Rows[row].Cells[3].Value = status; _queue.Rows[row].Cells[4].Value = result; }

    private Task<string?> ChooseCandidateAsync(IReadOnlyList<string> candidates, string name)
    {
        using var dialog = new CandidateSelectionDialog(candidates, name);
        return Task.FromResult(dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null);
    }

    private Task<string?> RequestPasswordAsync(ExtractStepDefinition step, string archive)
    {
        using var dialog = new PasswordPromptDialog(step.Name, archive);
        return Task.FromResult(dialog.ShowDialog(this) == DialogResult.OK ? dialog.Password : null);
    }

    private void AddStep(WorkflowStepDefinition step) { _workflow.Steps.Add(step); RefreshSteps(); SelectRow(_workflow.Steps.Count - 1); }
    private int SelectedStep => _steps.CurrentRow?.Index ?? -1;
    private void DeleteStep() { if (SelectedStep >= 0) { _workflow.Steps.RemoveAt(SelectedStep); RefreshSteps(); } }
    private void MoveStep(int delta) { var i = SelectedStep; var j = i + delta; if (i < 0 || j < 0 || j >= _workflow.Steps.Count) return; var s = _workflow.Steps[i]; _workflow.Steps.RemoveAt(i); _workflow.Steps.Insert(j, s); RefreshSteps(); SelectRow(j); }
    private void SelectRow(int index) { if (index >= 0 && index < _steps.Rows.Count) { _steps.CurrentCell = _steps.Rows[index].Cells[0]; _steps.Rows[index].Selected = true; } }
    private void SelectStep() => _stepEditor.SetStep(SelectedStep >= 0 && SelectedStep < _workflow.Steps.Count ? _workflow.Steps[SelectedStep] : null);

    private void RefreshSteps(bool keep = false)
    {
        var selected = keep ? SelectedStep : 0;
        _steps.Rows.Clear();
        for (var i = 0; i < _workflow.Steps.Count; i++)
        {
            var s = _workflow.Steps[i];
            var ext = string.IsNullOrWhiteSpace(s.Candidate.Extensions) ? "不限" : s.Candidate.Extensions;
            var fmt = string.IsNullOrWhiteSpace(s.Candidate.Formats) ? "不限" : s.Candidate.Formats;
            _steps.Rows.Add(i + 1, s.StepType == StepType.Extract ? "解压" : "重命名", s.Name, $"扩展名:{ext}; 格式:{fmt}");
        }
        SelectRow(Math.Clamp(selected, 0, Math.Max(0, _workflow.Steps.Count - 1)));
    }

    private void UpdateStepRow(WorkflowStepDefinition step)
    {
        var index = _workflow.Steps.FindIndex(candidate => candidate.Id == step.Id);
        if (index < 0 || index >= _steps.Rows.Count) return;
        var ext = string.IsNullOrWhiteSpace(step.Candidate.Extensions) ? "不限" : step.Candidate.Extensions;
        var fmt = string.IsNullOrWhiteSpace(step.Candidate.Formats) ? "不限" : step.Candidate.Formats;
        var row = _steps.Rows[index];
        row.Cells[1].Value = step.StepType == StepType.Extract ? "解压" : "重命名";
        row.Cells[2].Value = step.Name;
        row.Cells[3].Value = $"扩展名:{ext}; 格式:{fmt}";
    }

    private void BindWorkflowToUi()
    {
        _workflowName.Text = _workflow.Name;
        _workflowDescription.Text = _workflow.Description;
        _automatic.Checked = _workflow.ExecutionMode == ExecutionMode.AutomaticChain;
        _maxLayers.Value = Math.Clamp(_workflow.Automatic.MaxLayers, 1, 100);
        _keepAll.Checked = _workflow.KeepIntermediateFiles;
        _deleteOnSuccess.Checked = !_workflow.KeepIntermediateFiles;
        _stepEditor.SetPasswordEntries(_passwordManager.Entries);
        RefreshSteps();
    }

    private void NewWorkflow()
    {
        if (MessageBox.Show(this, "确定新建空白工作流？当前未保存修改将丢失。", "新建工作流", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _workflow = BuiltInTemplates.NewBlank();
        BindWorkflowToUi();
    }

    private void SetRunning(bool running) { _run.Enabled = !running; _cancel.Enabled = running; _automatic.Enabled = _maxLayers.Enabled = _keepAll.Enabled = _deleteOnSuccess.Enabled = !running; _steps.Enabled = _stepEditor.Enabled = !running && !_automatic.Checked; AllowDrop = !running; }
    private void AppendLog(string text) { _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\r\n"); _log.SelectionStart = _log.TextLength; _log.ScrollToCaret(); }

    private void ReloadTemplates() { _templates = _templateStore.LoadAll(); _templateList.Items.Clear(); foreach (var t in _templates) _templateList.Items.Add((t.IsBuiltIn ? "[内置] " : "[用户] ") + t.Name); if (_templates.Count > 0) _templateList.SelectedIndex = 0; }
    private TemplateDescriptor? SelectedTemplate => _templateList.SelectedIndex >= 0 ? _templates[_templateList.SelectedIndex] : null;
    private void PreviewTemplate() { var t = SelectedTemplate; _templatePreview.Text = t is null ? "" : $"名称：{t.Name}\r\n来源：{(t.IsBuiltIn ? "内置只读" : "用户模板")}\r\n说明：{t.Description}\r\n模式：{t.Workflow.ExecutionMode}\r\n步骤数：{t.Workflow.Steps.Count}\r\n\r\n" + string.Join("\r\n", t.Workflow.Steps.Select((s, i) => $"{i + 1}. {s.Name} ({s.StepType})")); }
    private void ApplyTemplate() { if (SelectedTemplate is { } t) { _workflow = WorkflowStore.Clone(t.Workflow); BindWorkflowToUi(); MessageBox.Show(this, "模板已应用到当前方案。", "模板"); } }
    private void SaveCurrentTemplate() { var name = PromptText("模板名称", _workflow.Name); if (name is null) return; _templateStore.SaveAsTemplate(_workflow, name, _workflow.Description); ReloadTemplates(); }
    private void CopyTemplate() { if (SelectedTemplate is not { } t) return; var name = PromptText("新模板名称", t.Name + " 副本"); if (name is null) return; _templateStore.SaveAsTemplate(t.Workflow, name, t.Description); ReloadTemplates(); }
    private void RenameTemplate() { if (SelectedTemplate is not { IsBuiltIn: false } t || t.FilePath is null) { MessageBox.Show(this, "内置模板不能重命名。"); return; } var name = PromptText("模板名称", t.Name); if (name is null) return; var w = WorkflowStore.Load(t.FilePath); w.Name = name; WorkflowStore.Save(t.FilePath, w); ReloadTemplates(); }
    private void DeleteTemplate() { if (SelectedTemplate is not { IsBuiltIn: false } t) { MessageBox.Show(this, "内置模板不能删除。"); return; } if (MessageBox.Show(this, "确定删除该用户模板？", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes) { _templateStore.Delete(t); ReloadTemplates(); } }
    private void ImportTemplate() { using var d = new OpenFileDialog { Filter = "模板 (*.json)|*.json" }; if (d.ShowDialog(this) != DialogResult.OK) return; var w = WorkflowStore.Load(d.FileName); _templateStore.SaveAsTemplate(w, w.Name, w.Description); ReloadTemplates(); }
    private void ExportTemplate() { if (SelectedTemplate is not { } t) return; using var d = new SaveFileDialog { Filter = "模板 (*.json)|*.json", FileName = PathHelpers.SanitizeFileName(t.Name) + ".json" }; if (d.ShowDialog(this) == DialogResult.OK) WorkflowStore.Save(d.FileName, WorkflowStore.Clone(t.Workflow)); }

    private string? PromptText(string title, string initial)
    {
        using var form = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(430, 120), FormBorderStyle = FormBorderStyle.FixedDialog };
        var box = new TextBox { Text = initial, Location = new Point(15, 15), Width = 400 };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(235, 65), Width = 80 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(330, 65), Width = 80 };
        form.Controls.AddRange([box, ok, cancel]); form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }
}
