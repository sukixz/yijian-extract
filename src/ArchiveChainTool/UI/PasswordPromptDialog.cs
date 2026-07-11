namespace ArchiveChainTool.UI;

internal sealed class PasswordPromptDialog : Form
{
    private readonly TextBox _password = new();
    public string? Password { get; private set; }

    public PasswordPromptDialog(string stepName, string archivePath)
    {
        Text = "输入解压密码";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(540, 190);
        Font = new Font("Microsoft YaHei UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 78,
            Padding = new Padding(14),
            Text = $"步骤：{stepName}\r\n文件：{Path.GetFileName(archivePath)}"
        };
        _password.UseSystemPasswordChar = true;
        _password.Width = 480;
        _password.Location = new Point(28, 88);

        var show = new CheckBox { Text = "显示密码", AutoSize = true, Location = new Point(28, 123) };
        show.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !show.Checked;
        var ok = new Button { Text = "确定", Width = 90, Location = new Point(318, 148) };
        ok.Click += (_, _) => { Password = _password.Text; DialogResult = DialogResult.OK; Close(); };
        var cancel = new Button { Text = "取消", Width = 90, Location = new Point(418, 148), DialogResult = DialogResult.Cancel };

        Controls.Add(label);
        Controls.Add(_password);
        Controls.Add(show);
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
