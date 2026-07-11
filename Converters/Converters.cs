using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GameOptimizer.Converters;

// Converters fire on every bound update (once per 1s snapshot) - brushes are
// immutable here, so each converter shares static instances instead of
// allocating a new SolidColorBrush per call.
public class PctToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Red    = new(Color.FromArgb(255, 220, 60, 60));
    private static readonly SolidColorBrush Yellow = new(Color.FromArgb(255, 220, 170, 50));
    private static readonly SolidColorBrush Green  = new(Color.FromArgb(255, 80, 200, 100));

    public object Convert(object value, Type t, object p, string l)
    {
        var pct = value is int i ? i : 0;
        return pct > 80 ? Red : pct > 50 ? Yellow : Green;
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
    private static readonly SolidColorBrush Gaming = new(Color.FromArgb(255, 200, 100, 220));
    private static readonly SolidColorBrush Idle   = new(Color.FromArgb(255, 150, 150, 150));

    public object Convert(object value, Type t, object p, string l) =>
        value is bool b && b ? Gaming : Idle;
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
    private static readonly SolidColorBrush Red    = new(Color.FromArgb(255, 220, 60, 60));
    private static readonly SolidColorBrush Yellow = new(Color.FromArgb(255, 220, 170, 50));
    private static readonly SolidColorBrush Green  = new(Color.FromArgb(255, 80, 200, 100));

    public object Convert(object value, Type t, object p, string l)
    {
        var temp = value is int i ? i : 0;
        return temp >= 80 ? Red : temp >= 70 ? Yellow : Green;
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
    private static readonly SolidColorBrush Alert = new(Color.FromArgb(255, 220, 60, 60));
    private static readonly SolidColorBrush Ok    = new(Color.FromArgb(255, 80, 200, 100));

    public object Convert(object value, Type t, object p, string l) =>
        value is bool b && b ? Alert : Ok;
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
    private static readonly SolidColorBrush On  = new(Color.FromArgb(255, 80, 220, 100));
    private static readonly SolidColorBrush Off = new(Color.FromArgb(255, 220, 160, 40));

    public object Convert(object value, Type t, object p, string l) =>
        value is bool b && b ? On : Off;
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class PinToBgConverter : IValueConverter
{
    private static readonly SolidColorBrush On  = new(Color.FromArgb(55, 60, 200, 80));
    private static readonly SolidColorBrush Off = new(Color.FromArgb(55, 220, 140, 30));

    public object Convert(object value, Type t, object p, string l) =>
        value is bool b && b ? On : Off;
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class PinToBorderConverter : IValueConverter
{
    private static readonly SolidColorBrush On  = new(Color.FromArgb(160, 60, 200, 80));
    private static readonly SolidColorBrush Off = new(Color.FromArgb(160, 220, 140, 30));

    public object Convert(object value, Type t, object p, string l) =>
        value is bool b && b ? On : Off;
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
