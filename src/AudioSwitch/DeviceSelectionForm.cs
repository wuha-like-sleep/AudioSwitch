namespace AudioSwitch;

internal sealed class DeviceSelectionForm : ChromeForm
{
    private readonly List<OutputDevice> selected;
    private List<OutputDevice> available;
    private readonly FlowLayoutPanel rows = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    public List<OutputDevice> Selection => selected.ToList();
    public DeviceSelectionForm(List<OutputDevice> available, List<OutputDevice> selected)
    {
        this.available = available.ToList(); this.selected = selected.DistinctBy(d => d.Id).ToList();
        Text = "AudioSwitch · 管理输出设备"; Icon = BrandIcon.Load();
        Font = new Font("Microsoft YaHei UI", 10); ForeColor = Theme.Ink; BackColor = Color.FromArgb(243, 246, 251);
        ClientSize = new Size(660, 590); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(24) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(new Label { Text = "你的设备，你的切换顺序", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 18, FontStyle.Bold) }, 0, 0);
        root.Controls.Add(new Label { Text = "勾选参加循环切换的设备，用上下按钮调整顺序。\n支持 Windows 可用的 USB、蓝牙、HDMI 和其他音频输出。", Dock = DockStyle.Fill, ForeColor = Theme.Muted }, 0, 1);
        root.Controls.Add(rows, 0, 2);
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
        footer.Controls.AddRange([done, cancel, refresh]); root.Controls.Add(footer, 0, 3);
        Controls.Add(root); root.BringToFront(); AcceptButton = done; CancelButton = cancel;
        rows.SizeChanged += (_, _) => ResizeRows();
        Render(); Shown += (_, _) => done.Focus();
    }
    private void Render()
    {
        rows.SuspendLayout();
        foreach (Control control in rows.Controls.Cast<Control>().ToArray()) control.Dispose();
        var entries = selected.Concat(available.Where(d => selected.All(s => s.Id != d.Id))).ToList();
        if (entries.Count == 0) rows.Controls.Add(new Label { Text = "未发现输出设备，请连接设备后刷新。", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(10, 24, 0, 0) });
        foreach (var entry in entries)
        {
            var device = available.FirstOrDefault(d => d.Id == entry.Id) ?? entry;
            bool online = available.Any(d => d.Id == device.Id);
            int index = selected.FindIndex(d => d.Id == device.Id);
            var card = new RoundedPanel { Height = 70, Padding = new Padding(14, 8, 12, 8), Margin = new Padding(0, 0, 0, 10) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = Color.White };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var toggle = new Toggle { Text = device.Name, Checked = index >= 0, Dock = DockStyle.Fill, Margin = Padding.Empty };
            toggle.CheckedChanged += (_, _) =>
            {
                if (toggle.Checked) selected.Add(device); else selected.RemoveAll(d => d.Id == device.Id);
                BeginInvoke(() => { if (!IsDisposed) Render(); });
            };
            layout.Controls.Add(toggle, 0, 0);
            layout.Controls.Add(new Label { Text = (index >= 0 ? $"顺序 {index + 1} · " : "未加入循环 · ") + (online ? "已连接" : "未连接，保留选择"), Dock = DockStyle.Fill, ForeColor = online ? Color.FromArgb(27, 139, 103) : Theme.Muted, Padding = new Padding(44, 0, 0, 0), AutoEllipsis = true }, 0, 1);
            var up = new ThemeButton { Text = "↑", Dock = DockStyle.Fill, Enabled = index > 0, AccessibleName = "上移 " + device.Name };
            var down = new ThemeButton { Text = "↓", Dock = DockStyle.Fill, Enabled = index >= 0 && index < selected.Count - 1, AccessibleName = "下移 " + device.Name };
            up.Click += (_, _) => MoveDevice(device.Id, -1); down.Click += (_, _) => MoveDevice(device.Id, 1);
            layout.Controls.Add(up, 1, 0); layout.SetRowSpan(up, 2); layout.Controls.Add(down, 2, 0); layout.SetRowSpan(down, 2);
            card.Controls.Add(layout); rows.Controls.Add(card);
        }
        ResizeRows(); rows.ResumeLayout();
    }
    internal void MoveDevice(string id, int delta)
    {
        int index = selected.FindIndex(d => d.Id == id), next = index + delta;
        if (index < 0 || next < 0 || next >= selected.Count) return;
        (selected[index], selected[next]) = (selected[next], selected[index]); Render();
    }
    private void ResizeRows() { foreach (var card in rows.Controls.OfType<RoundedPanel>()) card.Width = Math.Max(200, rows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4); }
    private static ThemeButton Button(string text, bool primary) => new() { Text = text, Primary = primary, Size = new Size(120, 42), Margin = new Padding(10, 10, 0, 0) };
}
