using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GameOptimizer.Converters;

public class PctToColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        var pct = value is int i ? i : 0;
        var color = pct > 80 ? Color.FromArgb(255, 220, 60, 60)
                  : pct > 50 ? Color.FromArgb(255, 220, 170, 50)
                              : Color.FromArgb(255, 80, 200, 100);
        return new SolidColorBrush(color);
    }
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class PctToTextConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        $"{value}%";
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class BoolToStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        var gaming = value is bool b && b;
        return gaming ? Color.FromArgb(40, 180, 50, 220)
                      : Color.FromArgb(25, 120, 120, 120);
    }
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class BoolToStatusTextConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is bool b && b ? "GAMING MODE ACTIVE" : "Idle - waiting for game launch";
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class BoolToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is bool b && b ? "" : ""; // controller vs clock
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class BoolToGamingBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        var gaming = value is bool b && b;
        return new SolidColorBrush(gaming ? Color.FromArgb(255, 200, 100, 220)
                                          : Color.FromArgb(255, 150, 150, 150));
    }
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class InvertBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class TempToColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        var temp = value is int i ? i : 0;
        var color = temp >= 80 ? Color.FromArgb(255, 220, 60, 60)
                  : temp >= 70 ? Color.FromArgb(255, 220, 170, 50)
                               : Color.FromArgb(255, 80, 200, 100);
        return new SolidColorBrush(color);
    }
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class AlertToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is bool b && b ? "" : ""; // warning vs checkmark
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class AlertToColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        var alert = value is bool b && b;
        return new SolidColorBrush(alert ? Color.FromArgb(255, 220, 60, 60)
                                         : Color.FromArgb(255, 80, 200, 100));
    }
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class PinToTextConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is bool b && b
            ? "Affinities active - processes pinned to assigned CPU zones"
            : "Affinities inactive - all processes running on all cores";
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class PinToColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        var on = value is bool b && b;
        return new SolidColorBrush(on ? Color.FromArgb(255, 80, 220, 100)
                                      : Color.FromArgb(255, 220, 160, 40));
    }
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class PinToBgConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        var on = value is bool b && b;
        return new SolidColorBrush(on ? Color.FromArgb(55, 60, 200, 80)
                                      : Color.FromArgb(55, 220, 140, 30));
    }
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class PinToBorderConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        var on = value is bool b && b;
        return new SolidColorBrush(on ? Color.FromArgb(160, 60, 200, 80)
                                      : Color.FromArgb(160, 220, 140, 30));
    }
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class PinToOnOffConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is bool b && b ? "ON" : "OFF";
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class ReportCountConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        $"Reports ({value})";
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class DoubleToF0Converter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is double d ? $"{d:F0}" : "0";
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

internal static class RateFormat
{
    // Auto-scales a KB/s rate: stays KB/s below 1 MB/s, switches to MB/s above.
    public static (string value, string unit) Scale(int kbps) =>
        kbps >= 1024
            ? ((kbps / 1024.0).ToString("0.0"), "MB/s")
            : (kbps.ToString(), "KB/s");
}

/// <summary>KB/s int -> scaled numeric string ("720" or "4.3").</summary>
public class RateValueConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        RateFormat.Scale(value is int i ? i : 0).value;
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

/// <summary>KB/s int -> unit string ("KB/s" or "MB/s") matching the scaled value.</summary>
public class RateUnitConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        RateFormat.Scale(value is int i ? i : 0).unit;
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

/// <summary>KB/s int -> full scaled rate string ("720 KB/s" or "4.3 MB/s").</summary>
public class RateConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        var (v, u) = RateFormat.Scale(value is int i ? i : 0);
        return $"{v} {u}";
    }
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}
