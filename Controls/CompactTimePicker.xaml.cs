using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NewSchool.Controls;

public sealed partial class CompactTimePicker : UserControl
{
    private bool _updating;

    public event EventHandler<TimeSpan>? TimeChanged;

    public TimeSpan Time
    {
        get => (TimeSpan)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public static readonly DependencyProperty TimeProperty =
        DependencyProperty.Register(nameof(Time), typeof(TimeSpan), typeof(CompactTimePicker),
            new PropertyMetadata(TimeSpan.Zero, OnTimePropertyChanged));

    private static void OnTimePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CompactTimePicker picker)
            picker.UpdateDisplay();
    }

    public CompactTimePicker()
    {
        this.InitializeComponent();
    }

    private void UpdateDisplay()
    {
        if (_updating) return;
        _updating = true;
        HourBox.Value = Time.Hours;
        MinuteBox.Value = Time.Minutes;
        _updating = false;
    }

    private void HourBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updating) return;
        int hour = double.IsNaN(args.NewValue) ? 0 : (int)args.NewValue;
        hour = Math.Clamp(hour, 0, 23);
        _updating = true;
        Time = new TimeSpan(hour, Time.Minutes, 0);
        _updating = false;
        TimeChanged?.Invoke(this, Time);
    }

    private void MinuteBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updating) return;
        int minute = double.IsNaN(args.NewValue) ? 0 : (int)args.NewValue;
        minute = Math.Clamp(minute, 0, 59);
        _updating = true;
        Time = new TimeSpan(Time.Hours, minute, 0);
        _updating = false;
        TimeChanged?.Invoke(this, Time);
    }
}
