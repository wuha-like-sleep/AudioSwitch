namespace AudioSwitch;

internal sealed class DeviceSelectionForm : ChromeForm
{
    private readonly List<OutputDevice> selected;
    private List<OutputDevice> available;
    private readonly FlowLayoutPanel rows = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索设备名称…", AccessibleName = "搜索输出设备", BorderStyle = BorderStyle.FixedSingle };
    private readonly Label count = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted };
    private readonly Dictionary<string, Toggle> toggles = new();
    internal string SearchQuery { get => search.Text; set => search.Text = value; }
    internal List<string> VisibleIds => Entries().Where(Matches).Select(d => d.Id).ToList();
    public List<OutputDevice> Selection => selected.ToList();
    public DeviceSelectionForm(List<OutputDevice> available, List<OutputDevice> selected)
    {
        this.available = available.ToList(); this.selected = selected.DistinctBy(d => d.Id).ToList();
        Text = "AudioSwitch · 管理输出设备"; Icon = BrandIcon.Load();
        Font = new Font("Microsoft YaHei UI", 10); ForeColor = Theme.Ink; BackColor = Color.FromArgb(243, 246, 251);
        ClientSize = new Size(660, 640); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(24) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(new Label { Text = "你的设备，你的切换顺序", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 18, FontStyle.Bold) }, 0, 0);
        root.Controls.Add(new Label { Text = "勾选参加循环切换的设备，用上下按钮调整顺序。\n支持 Windows 可用的 USB、蓝牙、HDMI 和其他音频输出。", Dock = DockStyle.Fill, ForeColor = Theme.Muted }, 0, 1);
        var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 8) };
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        var add = new ThemeButton { Text = "加入在线", Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
        var remove = new ThemeButton { Text = "移出列表", Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
        add.Click += (_, _) => AddVisibleOnline(); remove.Click += (_, _) => RemoveVisible();
        var tips = new ToolTip(); tips.SetToolTip(add, "加入当前搜索结果中的在线设备，保留原有顺序。"); tips.SetToolTip(remove, "移除当前搜索结果中的循环设备，其他设备保持不变。");
        tools.Controls.Add(search, 0, 0); tools.Controls.Add(add, 1, 0); tools.Controls.Add(remove, 2, 0);
        root.Controls.Add(tools, 0, 2); root.Controls.Add(count, 0, 3); root.Controls.Add(rows, 0, 4);
        FormClosed += (_, _) => tips.Dispose(); search.TextChanged += (_, _) => { rows.AutoScrollPosition = Point.Empty; Render(); };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var done = Button("完成", true); done.Click += (_, _) =>
        {
            if (this.selected.Count == 0) { AppDialog.Show(this, "请至少选择一台输出设备。", "尚未选择设备"); return; }
            DialogResult = DialogResult.OK;
        };
        var cancel = Button("取消", false); cancel.DialogResult = DialogResult.Cancel;
        var refresh = Button("刷新设备", false); refresh.Click += (_, _) =>
        {
            try { this.available = AudioDevices.List(); Render(); }
            catch (Exception ex) { AppDialog.Show(this, ex.Message, "刷新失败"); }
        };
        footer.Controls.AddRange([done, cancel, refresh]); root.Controls.Add(footer, 0, 5);
        Controls.Add(root); root.BringToFront(); AcceptButton = done; CancelButton = cancel;
        rows.SizeChanged += (_, _) => ResizeRows();
        Render(); Shown += (_, _) => done.Focus();
    }
    private List<OutputDevice> Entries() => selected.Select(d => available.FirstOrDefault(a => a.Id == d.Id) ?? d).Concat(available.Where(d => selected.All(s => s.Id != d.Id))).ToList();
    private bool Matches(OutputDevice device) => device.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase);
    internal void AddVisibleOnline()
    {
        foreach (var device in available.Where(Matches)) if (selected.All(s => s.Id != device.Id)) selected.Add(device);
        Render();
    }
    internal void RemoveVisible() { selected.RemoveAll(d => Matches(available.FirstOrDefault(a => a.Id == d.Id) ?? d)); Render(); }
    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96f);
    private void Render(string? focusId = null)
    {
        int scrollY = -rows.AutoScrollPosition.Y;
        rows.SuspendLayout(); toggles.Clear();
        foreach (Control control in rows.Controls.Cast<Control>().ToArray()) control.Dispose();
        var entries = Entries().Where(Matches).ToList();
        count.Text = $"{selected.Count} 台参与循环 · {available.Count} 台在线 · 当前显示 {entries.Count} 台";
        if (entries.Count == 0) rows.Controls.Add(new Label { Text = search.Text.Trim().Length > 0 ? "没有匹配的设备，试试其他关键词。" : "未发现输出设备，请连接设备后刷新。", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(10, 24, 0, 0) });
        foreach (var entry in entries)
        {
            var device = available.FirstOrDefault(d => d.Id == entry.Id) ?? entry;
            bool online = available.Any(d => d.Id == device.Id);
            int index = selected.FindIndex(d => d.Id == device.Id);
            var card = new RoundedPanel { Height = Scale(70), Padding = new Padding(Scale(14), Scale(8), Scale(12), Scale(8)), Margin = new Padding(0, 0, 0, Scale(10)) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = Color.White };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(40))); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(40)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(30))); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var toggle = new Toggle { Text = device.Name, Checked = index >= 0, Dock = DockStyle.Fill, Margin = Padding.Empty };
            toggle.CheckedChanged += (_, _) =>
            {
                if (toggle.Checked) selected.Add(device); else selected.RemoveAll(d => d.Id == device.Id);
                BeginInvoke(() => { if (!IsDisposed) Render(device.Id); });
            };
            toggles[device.Id] = toggle;
            layout.Controls.Add(toggle, 0, 0);
            layout.Controls.Add(new Label { Text = (index >= 0 ? $"顺序 {index + 1} · " : "未加入循环 · ") + (online ? "已连接" : "未连接，保留选择"), Dock = DockStyle.Fill, ForeColor = online ? Color.FromArgb(27, 139, 103) : Theme.Muted, Padding = new Padding(Scale(44), 0, 0, 0), AutoEllipsis = true }, 0, 1);
            var up = new ThemeButton { Text = "↑", Dock = DockStyle.Fill, Enabled = index > 0, AccessibleName = "上移 " + device.Name };
            var down = new ThemeButton { Text = "↓", Dock = DockStyle.Fill, Enabled = index >= 0 && index < selected.Count - 1, AccessibleName = "下移 " + device.Name };
            up.Click += (_, _) => MoveDevice(device.Id, -1); down.Click += (_, _) => MoveDevice(device.Id, 1);
            layout.Controls.Add(up, 1, 0); layout.SetRowSpan(up, 2); layout.Controls.Add(down, 2, 0); layout.SetRowSpan(down, 2);
            card.Controls.Add(layout); rows.Controls.Add(card);
        }
        ResizeRows(); rows.ResumeLayout();
        rows.AutoScrollPosition = new Point(0, scrollY);
        if (focusId != null && toggles.TryGetValue(focusId, out var focus)) focus.Focus();
    }
    internal void MoveDevice(string id, int delta)
    {
        int index = selected.FindIndex(d => d.Id == id), next = index + delta;
        if (index < 0 || next < 0 || next >= selected.Count) return;
        (selected[index], selected[next]) = (selected[next], selected[index]); Render(id);
    }
    protected override bool ProcessCmdKey(ref Message message, Keys keys)
    {
        if (keys == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
        if (keys == Keys.Escape && search.Text.Length > 0) { search.Clear(); return true; }
        return base.ProcessCmdKey(ref message, keys);
    }
    private void ResizeRows() { foreach (var card in rows.Controls.OfType<RoundedPanel>()) card.Width = Math.Max(200, rows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4); }
    private static ThemeButton Button(string text, bool primary) => new() { Text = text, Primary = primary, Size = new Size(120, 42), Margin = new Padding(10, 10, 0, 0) };
}
