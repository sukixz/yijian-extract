using ArchiveChainTool.Storage;

namespace ArchiveChainTool.UI;

internal sealed class PasswordManagerControl : UserControl
{
    private readonly PasswordStore _store;
    private PasswordVault _vault;
    private readonly ListBox _list = new();
    private readonly TextBox _name = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };

    public PasswordManagerControl(string baseDirectory)
    {
        _store = new PasswordStore(baseDirectory);
        _vault = _store.Load();
        Dock = DockStyle.Fill;

        var warning = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            ForeColor = Color.DarkRed,
            Padding = new Padding(8),
            Text = "安全提示：密码以明文保存在便携目录 data\\passwords.json。复制软件目录会同时复制密码。"
        };
        _list.Dock = DockStyle.Left;
        _list.Width = 240;
        _list.SelectedIndexChanged += (_, _) => LoadSelected();

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.Controls.Add(new Label { Text = "名称", AutoSize = true }, 0, 0);
        editor.Controls.Add(_name, 1, 0);
        editor.Controls.Add(new Label { Text = "密码", AutoSize = true }, 0, 1);
        editor.Controls.Add(_password, 1, 1);
        _name.Dock = _password.Dock = DockStyle.Top;
        var show = new CheckBox { Text = "显示密码", AutoSize = true };
        show.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !show.Checked;
        editor.Controls.Add(show, 1, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46 };
        buttons.Controls.Add(Button("新建", (_, _) => NewEntry()));
        buttons.Controls.Add(Button("保存", (_, _) => SaveEntry()));
        buttons.Controls.Add(Button("删除", (_, _) => DeleteEntry()));
        buttons.Controls.Add(Button("上移", (_, _) => MoveEntry(-1)));
        buttons.Controls.Add(Button("下移", (_, _) => MoveEntry(1)));

        Controls.Add(editor);
        Controls.Add(_list);
        Controls.Add(buttons);
        Controls.Add(warning);
        RefreshList();
    }

    public IReadOnlyList<PasswordEntry> Entries => _vault.Entries;

    private static Button Button(string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += handler;
        return button;
    }

    private void RefreshList(int select = -1)
    {
        _list.Items.Clear();
        foreach (var entry in _vault.Entries) _list.Items.Add(entry.Name);
        if (select >= 0 && select < _list.Items.Count) _list.SelectedIndex = select;
    }

    private void LoadSelected()
    {
        if (_list.SelectedIndex < 0) return;
        var entry = _vault.Entries[_list.SelectedIndex];
        _name.Text = entry.Name;
        _password.Text = entry.Password;
    }

    private void NewEntry()
    {
        _list.ClearSelected();
        _name.Clear();
        _password.Clear();
        _name.Focus();
    }

    private void SaveEntry()
    {
        try
        {
            if (_list.SelectedIndex >= 0)
            {
                var entry = _vault.Entries[_list.SelectedIndex];
                entry.Name = _name.Text.Trim();
                entry.Password = _password.Text;
            }
            else
            {
                _vault.Entries.Add(new PasswordEntry { Name = _name.Text.Trim(), Password = _password.Text });
            }
            _store.Save(_vault);
            RefreshList(Math.Max(0, _list.SelectedIndex >= 0 ? _list.SelectedIndex : _vault.Entries.Count - 1));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "密码库保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void DeleteEntry()
    {
        var index = _list.SelectedIndex;
        if (index < 0 || MessageBox.Show(this, "确定删除该密码条目？", "确认", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _vault.Entries.RemoveAt(index);
        _store.Save(_vault);
        RefreshList();
        NewEntry();
    }

    private void MoveEntry(int delta)
    {
        var index = _list.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _vault.Entries.Count) return;
        var entry = _vault.Entries[index];
        _vault.Entries.RemoveAt(index);
        _vault.Entries.Insert(target, entry);
        _store.Save(_vault);
        RefreshList(target);
    }
}
