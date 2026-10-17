namespace AudioSwitch;

internal sealed class ToastStyleForm : ChromeForm
{
    private readonly ComboBox duration = new DeviceComboBox();
    private readonly Dictionary<string, ThemeButton> corners = new();
    private SwitchToast? preview;
    public string Corner { get; private set; }
    public int Duration => duration.SelectedItem is DurationChoice choice ? choice.Milliseconds : 2200;

    public ToastStyleForm(string monitor, string corner, int milliseconds)
    {
        Text = "AudioSwitch · 提示样式"; Icon = BrandIcon.Load();
        Font = new Font("Microsoft YaHei UI", 10); ForeColor = Theme.Ink;
        BackColor = Color.FromArgb(243, 246, 251);
        ClientSize = new Size(560, 580); StartPosition = FormStartPosition.CenterParent;
        Corner = ToastPlacement.Corners.Contains(corner) ? corner : "bottom-right";
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(24), AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 40, 28, 140, 60, 52, 64 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.Controls.Add(new Label { Text = "让提示出现在合适的位置", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 18, FontStyle.Bold) }, 0, 0);
        root.Controls.Add(new Label { Text = "选择屏幕角落，再预览真实效果。", Dock = DockStyle.Fill, ForeColor = Theme.Muted }, 0, 1);
        var surface = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = new Padding(0, 4, 0, 4) };
        var screen = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = Color.White };
        screen.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        screen.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        screen.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        screen.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        screen.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        screen.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        foreach (var item in new[] { ("top-left", "左上角", 0, 0), ("top-right", "右上角", 2, 0), ("bottom-left", "左下角", 0, 2), ("bottom-right", "右下角", 2, 2) })
        {
            var button = new ThemeButton { Text = item.Item2, Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = "提示位置 " + item.Item2 };
            corners.Add(item.Item1, button);
            button.Click += (_, _) => { Corner = item.Item1; UpdateSelection(); };
            screen.Controls.Add(button, item.Item3, item.Item4);
        }
        var center = new Label { Text = "显示器工作区域 · 自动避开任务栏", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted };
        screen.Controls.Add(center, 0, 1); screen.SetColumnSpan(center, 3);
        surface.Controls.Add(screen); root.Controls.Add(surface, 0, 2);
        var timeRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 12, 0, 0) };
        timeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        timeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        timeRow.Controls.Add(new Label { Text = "提示停留时间", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        foreach (int value in new[] { 1500, 2200, 3000, 5000 }) duration.Items.Add(new DurationChoice(value));
        duration.SelectedItem = duration.Items.Cast<DurationChoice>().FirstOrDefault(c => c.Milliseconds == milliseconds) ?? duration.Items[1];
        timeRow.Controls.Add(duration, 1, 0); root.Controls.Add(timeRow, 0, 3);
        root.Controls.Add(new Label { Text = "提示不抢焦点、不拦截鼠标。\n点击完成后，回到设置页保存即可生效。", ForeColor = Theme.Muted, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) }, 0, 4);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var done = Button("完成", true); done.DialogResult = DialogResult.OK;
        var cancel = Button("取消", false); cancel.DialogResult = DialogResult.Cancel;
        var test = Button("预览效果", false);
        test.Click += (_, _) =>
        {
            preview?.Close(); preview?.Dispose();
            preview = new SwitchToast("提示预览", "USB 耳机 · Headphones", monitor, Duration, Corner);
            preview.Show();
        };
        footer.Controls.AddRange([done, cancel, test]); root.Controls.Add(footer, 0, 5);
        Controls.Add(root); root.BringToFront(); AcceptButton = done; CancelButton = cancel;
        UpdateSelection();
        FormClosed += (_, _) => { preview?.Close(); preview?.Dispose(); };
    }
    private void UpdateSelection()
    {
        foreach (var (id, button) in corners) { button.Primary = id == Corner; button.Invalidate(); }
    }
    private static ThemeButton Button(string text, bool primary) => new() { Text = text, Primary = primary, Size = new Size(120, 42), Margin = new Padding(10, 10, 0, 0) };
    private sealed record DurationChoice(int Milliseconds)
    {
        public override string ToString() => Milliseconds == 2200 ? "2.2 秒（默认）" : $"{Milliseconds / 1000d:0.#} 秒";
    }
}
