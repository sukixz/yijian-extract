namespace ArchiveChainTool.UI;

internal sealed class CandidateSelectionDialog : Form
{
    private readonly ListView _list = new();
    public string? SelectedPath { get; private set; }

    public CandidateSelectionDialog(IReadOnlyList<string> candidates, string stepName)
    {
        Text = $"选择候选文件 - {stepName}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(850, 480);
        Font = new Font("Microsoft YaHei UI", 9F);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.Columns.Add("文件名", 220);
        _list.Columns.Add("路径", 430);
        _list.Columns.Add("大小", 120);
        foreach (var path in candidates)
        {
            var item = new ListViewItem(Path.GetFileName(path)) { Tag = path };
            item.SubItems.Add(path);
            item.SubItems.Add(FormatSize(new FileInfo(path).Length));
            _list.Items.Add(item);
        }
        _list.DoubleClick += (_, _) => Confirm();

        var ok = new Button { Text = "选择", DialogResult = DialogResult.None, Width = 100 };
        ok.Click += (_, _) => Confirm();
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 100 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        Controls.Add(_list);
        Controls.Add(buttons);
        CancelButton = cancel;
    }

    private void Confirm()
    {
        if (_list.SelectedItems.Count != 1)
        {
            MessageBox.Show(this, "请先选择一个文件。", "提示");
            return;
        }
        SelectedPath = (string)_list.SelectedItems[0].Tag!;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string FormatSize(long value) => value >= 1L << 30
        ? $"{value / (double)(1L << 30):F2} GB"
        : value >= 1L << 20 ? $"{value / (double)(1L << 20):F2} MB" : $"{value / 1024d:F1} KB";
}
