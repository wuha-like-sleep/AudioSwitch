using System.Text.Json;

namespace AudioSwitch;

internal sealed class DeviceSummaryPanel : TableLayoutPanel
{
    private string? signature;
    public DeviceSummaryPanel()
    {
        Dock = DockStyle.Fill; ColumnCount = 3; RowCount = 3; Margin = Padding.Empty; BackColor = Color.White;
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34)); ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        for (int i = 0; i < 3; i++) RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
    }
    public void UpdateSummary(List<OutputDevice> selected, List<OutputDevice> available)
    {
        var online = available.Select(d => d.Id).ToHashSet();
        string next = JsonSerializer.Serialize(selected.Select(d => new { d.Id, d.Name, Online = online.Contains(d.Id) }));
        if (next == signature) return; signature = next;
        SuspendLayout();
        foreach (Control child in Controls.Cast<Control>().ToArray()) child.Dispose();
        if (selected.Count == 0)
        {
            var empty = Label("点击管理设备，选择参加循环的输出。", Theme.Muted);
            Controls.Add(empty, 0, 0); SetColumnSpan(empty, 3);
            var support = Label("支持 USB、蓝牙、HDMI 等音频设备。", Theme.Muted);
            Controls.Add(support, 0, 1); SetColumnSpan(support, 3);
        }
        int visible = selected.Count > 3 ? 2 : selected.Count;
        for (int i = 0; i < visible; i++)
        {
            Controls.Add(Label((i + 1).ToString("00"), Theme.Accent), 0, i);
            Controls.Add(Label(selected[i].Name, Theme.Ink), 1, i);
            Controls.Add(Label(online.Contains(selected[i].Id) ? "已连接" : "未连接", online.Contains(selected[i].Id) ? Color.FromArgb(27, 139, 103) : Theme.Muted), 2, i);
        }
        if (selected.Count > 3)
        {
            var more = Label($"另有 {selected.Count - 2} 台设备，点击管理查看全部", Theme.Muted);
            Controls.Add(more, 0, 2); SetColumnSpan(more, 3);
        }
        AccessibleName = $"已选择 {selected.Count} 台输出设备";
        ResumeLayout();
    }
    private static Label Label(string text, Color color) => new() { Text = text, Dock = DockStyle.Fill, ForeColor = color, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
}
