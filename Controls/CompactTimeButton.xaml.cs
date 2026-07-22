using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NewSchool.Controls;

/// <summary>
/// 컴팩트 시간 입력 컨트롤. [시] : [분] [버튼] 형태로,
/// 시·분은 키보드로 직접 입력하고, 버튼을 누르면 표준 TimePickerFlyout(24시간제)이 뜬다.
/// API 는 기존 사용처와 호환: <see cref="Time"/>(TimeSpan) + <see cref="TimeChanged"/>.
/// </summary>
public sealed partial class CompactTimeButton : UserControl
{
    private bool _syncing;   // 프로그램적으로 TextBox.Text 를 갱신하는 중(TextChanged 재진입 방지)
    private bool _editing;   // 사용자가 타이핑 중 → UpdateDisplay 가 박스 텍스트를 덮어쓰지 않도록

    public event EventHandler<TimeSpan>? TimeChanged;

    public TimeSpan Time
    {
        get => (TimeSpan)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    /// <summary>선택적 헤더 라벨. 비우면 라벨을 숨긴다(대화상자 등 라벨이 별도인 경우).</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(CompactTimeButton),
            new PropertyMetadata(string.Empty, OnHeaderChanged));

    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CompactTimeButton c)
        {
            string text = e.NewValue as string ?? string.Empty;
            c.HeaderText.Text = text;
            c.HeaderText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public static readonly DependencyProperty TimeProperty =
        DependencyProperty.Register(nameof(Time), typeof(TimeSpan), typeof(CompactTimeButton),
            new PropertyMetadata(TimeSpan.Zero, OnTimePropertyChanged));

    private static void OnTimePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CompactTimeButton c)
            c.UpdateDisplay();
    }

    public CompactTimeButton()
    {
        this.InitializeComponent();
        UpdateDisplay();
    }

    /// <summary>Time → 화면 반영. 타이핑 중이면 박스 텍스트는 건드리지 않고 플라이아웃만 동기화.</summary>
    private void UpdateDisplay()
    {
        Picker.Time = new TimeSpan(Time.Hours, Time.Minutes, 0);

        if (_editing) return;

        _syncing = true;
        HourBox.Text = Time.Hours.ToString("D2");
        MinuteBox.Text = Time.Minutes.ToString("D2");
        _syncing = false;
    }

    private void HourBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (int.TryParse(HourBox.Text, out int h))
        {
            h = Math.Clamp(h, 0, 23);
            CommitFromText(new TimeSpan(h, Time.Minutes, 0));
        }
    }

    private void MinuteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (int.TryParse(MinuteBox.Text, out int m))
        {
            m = Math.Clamp(m, 0, 59);
            CommitFromText(new TimeSpan(Time.Hours, m, 0));
        }
    }

    /// <summary>타이핑으로 값을 반영 — 박스 텍스트는 유지(캐럿 튐 방지), 플라이아웃만 동기화.</summary>
    private void CommitFromText(TimeSpan t)
    {
        _editing = true;
        Time = t;          // OnTimePropertyChanged → UpdateDisplay(플라이아웃만)
        _editing = false;
        TimeChanged?.Invoke(this, Time);
    }

    /// <summary>포커스를 잃으면 2자리로 정규화(예: "7" → "07", 범위 초과 입력 보정).</summary>
    private void Box_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdateDisplay();
    }

    private void Picker_TimePicked(TimePickerFlyout sender, TimePickedEventArgs args)
    {
        Time = args.NewTime;   // UpdateDisplay 로 박스·플라이아웃 모두 갱신
        TimeChanged?.Invoke(this, Time);
    }
}
