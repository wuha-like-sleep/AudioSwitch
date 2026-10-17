namespace AudioSwitch;

internal static class SignalCalibration
{
    public static int Recommend(IEnumerable<int> readings)
    {
        var values = readings.Where(v => v is >= -127 and < 0).Order().ToList();
        if (values.Count < 3) throw new InvalidOperationException("请将手机放在正常使用位置，等待至少 3 次实时信号更新后再标定。");
        int median = (values[(values.Count - 1) / 2] + values[values.Count / 2]) / 2;
        if (median <= -115) throw new InvalidOperationException("当前信号过弱，请将手机靠近电脑后再标定。");
        return Math.Clamp(median - 15, -120, -20);
    }
}
