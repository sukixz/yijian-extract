using ArchiveChainTool.Storage;
using ArchiveChainTool.Workflows;

namespace ArchiveChainTool.UI;

internal sealed class StepEditorPanel : UserControl
{
    private readonly TextBox _name = new();
    private readonly CheckBox _enabled = new() { Text = "启用此步骤" };
    private readonly TextBox _pattern = new();
    private readonly CheckedListBox _extensionsList = new() { Height = 92, CheckOnClick = true };
    private readonly CheckedListBox _formatsList = new() { Height = 92, CheckOnClick = true };
    private readonly TextBox _customExtensions = new();
    private readonly CheckBox _recursive = new() { Text = "搜索所有子文件夹" };
    private readonly CheckBox _askWhenMultiple = new() { Text = "多个候选时弹窗选择" };
    private readonly Label _operationTitle = new();
    private readonly TextBox _outputName = new();
    private readonly ComboBox _passwordMode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _showPassword = new() { Text = "显示密码" };
    private readonly CheckedListBox _libraryPasswords = new() { Height = 110, CheckOnClick = true };
    private readonly Button _passwordUp = new() { Text = "密码上移", AutoSize = true };
    private readonly Button _passwordDown = new() { Text = "密码下移", AutoSize = true };
    private readonly TextBox _targetName = new();
    private readonly ComboBox _collision = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TableLayoutPanel _table;
    private WorkflowStepDefinition? _step;
    private IReadOnlyList<PasswordEntry> _passwordEntries = [];
    private bool _loading;

    public event EventHandler<WorkflowStepDefinition>? StepChanged;

    public StepEditorPanel()
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        Padding = new Padding(12);
        Font = new Font("Microsoft YaHei UI", 9F);
        _passwordMode.Items.AddRange(["无密码", "固定密码（仅当前工作流）", "运行时询问", "选择密码库"]);
        _collision.Items.AddRange(["自动追加数字", "发现同名时停止"]);
        _extensionsList.Items.AddRange([".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".jpg", ".jpeg"]);
        _formatsList.Items.AddRange(["zip", "rar", "7z", "tar", "gz", "bz2", "xz"]);

        _table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddHeader("步骤设置"); AddRow("步骤名称", _name); AddWide(_enabled);
        AddHeader("文件筛选规则"); AddRow("文件名通配符", _pattern); AddRow("常见扩展名", _extensionsList);
        AddRow("其他扩展名", _customExtensions); AddRow("真实格式", _formatsList); AddWide(_recursive); AddWide(_askWhenMultiple);
        AddHeader("操作参数"); AddRow("输出文件夹名称", _outputName); AddRow("解压密码方式", _passwordMode);
        AddRow("固定密码", _password); AddWide(_showPassword); AddRow("选择密码库", _libraryPasswords);
        var passwordButtons = new FlowLayoutPanel { AutoSize = true }; passwordButtons.Controls.Add(_passwordUp); passwordButtons.Controls.Add(_passwordDown); AddWide(passwordButtons);
        AddRow("重命名为", _targetName); AddRow("同名文件处理", _collision);
        Controls.Add(_table);
        WireEvents();
        SetStep(null);
    }

    public void SetPasswordEntries(IReadOnlyList<PasswordEntry> entries)
    {
        _passwordEntries = entries;
        if (_step is ExtractStepDefinition) SetStep(_step);
    }

    public void SetStep(WorkflowStepDefinition? step)
    {
        _loading = true;
        _table.SuspendLayout();
        try
        {
            _step = step;
            Enabled = step is not null;
            if (step is null) { ClearControls(); return; }
            _name.Text = step.Name; _enabled.Checked = step.Enabled; _pattern.Text = step.Candidate.NamePattern;
            SetCheckedValues(_extensionsList, step.Candidate.Extensions, out var custom); _customExtensions.Text = custom;
            SetCheckedValues(_formatsList, step.Candidate.Formats, out _); _recursive.Checked = step.Candidate.Recursive;
            _askWhenMultiple.Checked = step.Candidate.AskWhenMultiple;
            var extract = step as ExtractStepDefinition; var rename = step as RenameStepDefinition;
            SetExtractVisibility(extract is not null); SetRenameVisibility(rename is not null);
            if (extract is not null)
            {
                _outputName.Text = extract.OutputName; _passwordMode.SelectedIndex = (int)extract.Password.Mode;
                _password.Text = extract.Password.Value ?? string.Empty; LoadPasswordReferences(extract.Password.LibraryEntryIds); UpdatePasswordState(false);
            }
            if (rename is not null) { _targetName.Text = rename.TargetFileName; _collision.SelectedIndex = (int)rename.CollisionPolicy; }
        }
        finally { _table.ResumeLayout(true); _loading = false; }
    }

    private void WireEvents()
    {
        _name.TextChanged += (_, _) => ApplyChanges(); _enabled.CheckedChanged += (_, _) => ApplyChanges();
        _pattern.TextChanged += (_, _) => ApplyChanges(); _customExtensions.TextChanged += (_, _) => ApplyChanges();
        _extensionsList.ItemCheck += ListItemCheck; _formatsList.ItemCheck += ListItemCheck; _libraryPasswords.ItemCheck += ListItemCheck;
        _recursive.CheckedChanged += (_, _) => ApplyChanges(); _askWhenMultiple.CheckedChanged += (_, _) => ApplyChanges();
        _outputName.TextChanged += (_, _) => ApplyChanges();
        _passwordMode.SelectedIndexChanged += (_, _) => { if (!_loading) { UpdatePasswordState(true); ApplyChanges(); } };
        _password.TextChanged += (_, _) => ApplyChanges(); _showPassword.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !_showPassword.Checked;
        _targetName.TextChanged += (_, _) => ApplyChanges(); _collision.SelectedIndexChanged += (_, _) => ApplyChanges();
        _passwordUp.Click += (_, _) => MovePassword(-1); _passwordDown.Click += (_, _) => MovePassword(1);
    }

    private void ListItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_loading) return;
        BeginInvoke(new Action(ApplyChanges));
    }

    private void ApplyChanges()
    {
        if (_loading || _step is null) return;
        _step.Name = _name.Text; _step.Enabled = _enabled.Checked; _step.Candidate.NamePattern = _pattern.Text;
        _step.Candidate.Extensions = string.Join(',', CheckedValues(_extensionsList).Concat(SplitValues(_customExtensions.Text)).Distinct(StringComparer.OrdinalIgnoreCase));
        _step.Candidate.Formats = string.Join(',', CheckedValues(_formatsList));
        _step.Candidate.Recursive = _recursive.Checked; _step.Candidate.AskWhenMultiple = _askWhenMultiple.Checked;
        if (_step is ExtractStepDefinition extract)
        {
            extract.OutputName = _outputName.Text; extract.Password.Mode = _passwordMode.SelectedIndex >= 0 ? (PasswordMode)_passwordMode.SelectedIndex : PasswordMode.None;
            extract.Password.Value = extract.Password.Mode == PasswordMode.Stored ? _password.Text : null;
            extract.Password.LibraryEntryIds = extract.Password.Mode == PasswordMode.PasswordLibrary
                ? _libraryPasswords.CheckedItems.Cast<PasswordListItem>().Select(item => item.Id).ToList() : [];
        }
        else if (_step is RenameStepDefinition rename)
        {
            rename.TargetFileName = _targetName.Text; rename.CollisionPolicy = _collision.SelectedIndex >= 0 ? (CollisionPolicy)_collision.SelectedIndex : CollisionPolicy.AppendNumber;
        }
        StepChanged?.Invoke(this, _step);
    }

    private void LoadPasswordReferences(IEnumerable<Guid> ids)
    {
        _libraryPasswords.Items.Clear();
        var referenced = ids.ToList();
        foreach (var id in referenced)
        {
            var entry = _passwordEntries.FirstOrDefault(value => value.Id == id);
            _libraryPasswords.Items.Add(entry is null ? new PasswordListItem(id, $"缺失密码 ({id.ToString()[..8]})") : new PasswordListItem(id, entry.Name), true);
        }
        foreach (var entry in _passwordEntries.Where(entry => !referenced.Contains(entry.Id))) _libraryPasswords.Items.Add(new PasswordListItem(entry.Id, entry.Name), false);
    }

    private void MovePassword(int delta)
    {
        var index = _libraryPasswords.SelectedIndex; var target = index + delta;
        if (index < 0 || target < 0 || target >= _libraryPasswords.Items.Count) return;
        _loading = true;
        var item = _libraryPasswords.Items[index]; var check = _libraryPasswords.GetItemChecked(index);
        _libraryPasswords.Items.RemoveAt(index); _libraryPasswords.Items.Insert(target, item); _libraryPasswords.SetItemChecked(target, check);
        _libraryPasswords.SelectedIndex = target; _loading = false; ApplyChanges();
    }

    private void UpdatePasswordState(bool userAction)
    {
        var stored = _passwordMode.SelectedIndex == (int)PasswordMode.Stored;
        var library = _passwordMode.SelectedIndex == (int)PasswordMode.PasswordLibrary;
        _password.Enabled = _showPassword.Enabled = stored; _libraryPasswords.Enabled = _passwordUp.Enabled = _passwordDown.Enabled = library;
        if (userAction && !stored) _password.Clear();
    }

    private void SetExtractVisibility(bool visible)
    {
        foreach (var control in new Control[] { _outputName, _passwordMode, _password, _showPassword, _libraryPasswords, _passwordUp, _passwordDown }) control.Visible = visible;
    }
    private void SetRenameVisibility(bool visible) { _targetName.Visible = _collision.Visible = visible; }
    private void ClearControls() { _name.Clear(); _pattern.Clear(); _customExtensions.Clear(); _outputName.Clear(); _password.Clear(); _targetName.Clear(); _libraryPasswords.Items.Clear(); }
    private static IEnumerable<string> CheckedValues(CheckedListBox list) => list.CheckedItems.Cast<object>().Select(item => item.ToString()!);
    private static string[] SplitValues(string value) => value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private void SetCheckedValues(CheckedListBox list, string values, out string custom)
    {
        var parsed = SplitValues(values).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < list.Items.Count; index++) list.SetItemChecked(index, parsed.Remove(list.Items[index].ToString()!));
        custom = string.Join(',', parsed);
    }
    private void AddHeader(string text) { var label = new Label { Text = text, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(35, 80, 145), AutoSize = true }; AddWide(label); }
    private void AddRow(string label, Control control) { var row = _table.RowCount++; _table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); _table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row); control.Dock = DockStyle.Top; _table.Controls.Add(control, 1, row); }
    private void AddWide(Control control) { var row = _table.RowCount++; _table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); _table.Controls.Add(control, 0, row); _table.SetColumnSpan(control, 2); }
    private sealed record PasswordListItem(Guid Id, string Name) { public override string ToString() => Name; }
}
