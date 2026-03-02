using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
using Windows.UI;

namespace NewSchool.Controls;

public sealed partial class MonthPicker : UserControl
{
    #region Events
    public event EventHandler<SelectedMonthChangedEventArgs>? SelectedMonthChanged;
    #endregion

    #region Dependency Properties
    public DateTime SelectedMonth
    {
        get => (DateTime)GetValue(SelectedMonthProperty);
        set => SetValue(SelectedMonthProperty, value);
    }

    public static readonly DependencyProperty SelectedMonthProperty =
        DependencyProperty.Register(
            nameof(SelectedMonth),
            typeof(DateTime),
            typeof(MonthPicker),
            new PropertyMetadata(DateTime.Now, OnSelectedMonthChanged));

    private static void OnSelectedMonthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MonthPicker picker && e.NewValue is DateTime newDate)
        {
            var oldDate = e.OldValue is DateTime ? (DateTime)e.OldValue : DateTime.Now;

            picker.UpdateButtonContent(newDate);
            picker.UpdateMonthHighlight();

            // 이벤트 발생
            picker.SelectedMonthChanged?.Invoke(
                picker,
                new SelectedMonthChangedEventArgs(newDate, oldDate)
            );
        }
    }
    #endregion

    #region Fields
    private readonly List<Border> _monthBorders;
    private Border? _currentHoveredBorder;
    private int _displayYear;
    private readonly SolidColorBrush _normalBrush;
    private readonly SolidColorBrush _hoverBrush;
    private readonly SolidColorBrush _selectedBrush;
    private readonly SolidColorBrush _accentBrush;
    #endregion

    #region Constructor
    public MonthPicker()
    {
        this.InitializeComponent();

        // 색상 초기화
        _normalBrush = new SolidColorBrush(Colors.Transparent);
        _hoverBrush = new SolidColorBrush(Color.FromArgb(255, 240, 249, 255));
        _selectedBrush = new SolidColorBrush(Color.FromArgb(255, 230, 244, 255));
        _accentBrush = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));

        // 월 Border 컬렉션 초기화
        _monthBorders = new List<Border>
        {
            Month1, Month2, Month3, Month4,
            Month5, Month6, Month7, Month8,
            Month9, Month10, Month11, Month12
        };

        // 초기 설정
        _displayYear = DateTime.Now.Year;
        UpdateYearDisplay();
        UpdateButtonContent(SelectedMonth);
        UpdateMonthHighlight();

        // Flyout 이벤트 연결
        PopMonth.Opened += OnFlyoutOpened;
        PopMonth.Closed += OnFlyoutClosed;
    }
    #endregion

    #region Event Handlers
    private void BtnMonth_Click(object sender, RoutedEventArgs e)
    {
        // Flyout이 자동으로 열립니다 (Button.Flyout 속성 때문에)
        _displayYear = SelectedMonth.Year;
        UpdateYearDisplay();
        UpdateMonthHighlight();
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        _displayYear++;
        UpdateYearDisplay();
        UpdateMonthHighlight();
        AnimateYearChange(true);
    }

    private void BtnPrevious_Click(object sender, RoutedEventArgs e)
    {
        _displayYear--;
        UpdateYearDisplay();
        UpdateMonthHighlight();
        AnimateYearChange(false);
    }

    private void Month_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is string tagStr && int.TryParse(tagStr, out int month))
        {
            // 선택된 월 설정
            SelectedMonth = new DateTime(_displayYear, month, 1);

            // 애니메이션 효과
            AnimateSelection(border);

            // Flyout 닫기 (약간의 딜레이 후)
            DispatcherQueue.TryEnqueue(async () =>
            {
                await System.Threading.Tasks.Task.Delay(150);
                PopMonth.Hide();
            });
        }
    }

    private void Month_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            _currentHoveredBorder = border;

            // 현재 선택된 월이 아닌 경우만 호버 효과 적용
            if (!IsSelectedMonth(border))
            {
                border.Background = _hoverBrush;
                AnimateHoverIn(border);
            }
        }
    }

    private void Month_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && border == _currentHoveredBorder)
        {
            _currentHoveredBorder = null;

            // 현재 선택된 월이 아닌 경우만 호버 해제
            if (!IsSelectedMonth(border))
            {
                border.Background = _normalBrush;
                AnimateHoverOut(border);
            }
        }
    }

    private void GridMonth_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                PopMonth.Hide();
                e.Handled = true;
                break;

            case VirtualKey.Left:
                NavigateMonth(-1);
                e.Handled = true;
                break;

            case VirtualKey.Right:
                NavigateMonth(1);
                e.Handled = true;
                break;

            case VirtualKey.Up:
                NavigateMonth(-4);
                e.Handled = true;
                break;

            case VirtualKey.Down:
                NavigateMonth(4);
                e.Handled = true;
                break;

            case VirtualKey.Enter:
            case VirtualKey.Space:
                SelectCurrentMonth();
                e.Handled = true;
                break;
        }
    }

    private void OnFlyoutOpened(object? sender, object e)
    {
        // Flyout이 열릴 때 현재 선택된 월 강조
        UpdateMonthHighlight();

        // 포커스 설정
        GridMonth.Focus(FocusState.Programmatic);
    }

    private void OnFlyoutClosed(object? sender, object e)
    {
        // 호버 상태 초기화
        _currentHoveredBorder = null;
    }
    #endregion

    #region Helper Methods
    private void UpdateButtonContent(DateTime date)
    {
        BtnMonth.Content = date.ToString("yyyy년 M월");
    }

    private void UpdateYearDisplay()
    {
        LbYear.Text = $"{_displayYear}년";
    }

    private void UpdateMonthHighlight()
    {
        var selectedYear = SelectedMonth.Year;
        var selectedMonth = SelectedMonth.Month;

        for (int i = 0; i < _monthBorders.Count; i++)
        {
            var border = _monthBorders[i];
            var month = i + 1;

            if (_displayYear == selectedYear && month == selectedMonth)
            {
                // 선택된 월
                border.Background = _selectedBrush;
                border.BorderBrush = _accentBrush;
                border.BorderThickness = new Thickness(2);

                if (border.Child is TextBlock text)
                {
                    text.Foreground = _accentBrush;
                    text.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                }
            }
            else
            {
                // 선택되지 않은 월
                border.Background = _normalBrush;
                border.BorderBrush = new SolidColorBrush(Colors.Transparent);
                border.BorderThickness = new Thickness(1);

                if (border.Child is TextBlock text)
                {
                    text.Foreground = new SolidColorBrush(Colors.Black);
                    text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                }
            }
        }
    }

    private bool IsSelectedMonth(Border border)
    {
        if (border.Tag is string tagStr && int.TryParse(tagStr, out int month))
        {
            return _displayYear == SelectedMonth.Year && month == SelectedMonth.Month;
        }
        return false;
    }

    private void NavigateMonth(int offset)
    {
        var currentMonth = SelectedMonth.Month;
        var newMonth = currentMonth + offset;
        var newYear = _displayYear;

        // 월 범위 조정
        if (newMonth < 1)
        {
            newMonth += 12;
            newYear--;
        }
        else if (newMonth > 12)
        {
            newMonth -= 12;
            newYear++;
        }

        // 년도가 변경된 경우
        if (newYear != _displayYear)
        {
            _displayYear = newYear;
            UpdateYearDisplay();
        }

        SelectedMonth = new DateTime(newYear, newMonth, 1);
    }

    private void SelectCurrentMonth()
    {
        var currentMonth = SelectedMonth.Month;

        if (currentMonth >= 1 && currentMonth <= 12)
        {
            var border = _monthBorders[currentMonth - 1];
            AnimateSelection(border);

            DispatcherQueue.TryEnqueue(async () =>
            {
                await System.Threading.Tasks.Task.Delay(150);
                PopMonth.Hide();
            });
        }
    }
    #endregion

    #region Animation Methods
    private void AnimateHoverIn(Border border)
    {
        // 간단한 스케일 변환 애니메이션
        var scaleTransform = new ScaleTransform
        {
            CenterX = border.ActualWidth / 2,
            CenterY = border.ActualHeight / 2
        };
        border.RenderTransform = scaleTransform;

        var storyboard = new Storyboard();

        var scaleXAnimation = new DoubleAnimation
        {
            From = 1.0,
            To = 1.05,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        var scaleYAnimation = new DoubleAnimation
        {
            From = 1.0,
            To = 1.05,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(scaleXAnimation, scaleTransform);
        Storyboard.SetTargetProperty(scaleXAnimation, "ScaleX");

        Storyboard.SetTarget(scaleYAnimation, scaleTransform);
        Storyboard.SetTargetProperty(scaleYAnimation, "ScaleY");

        storyboard.Children.Add(scaleXAnimation);
        storyboard.Children.Add(scaleYAnimation);
        storyboard.Begin();
    }

    private void AnimateHoverOut(Border border)
    {
        if (border.RenderTransform is ScaleTransform scaleTransform)
        {
            var storyboard = new Storyboard();

            var scaleXAnimation = new DoubleAnimation
            {
                From = scaleTransform.ScaleX,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var scaleYAnimation = new DoubleAnimation
            {
                From = scaleTransform.ScaleY,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            Storyboard.SetTarget(scaleXAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleXAnimation, "ScaleX");

            Storyboard.SetTarget(scaleYAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleYAnimation, "ScaleY");

            storyboard.Children.Add(scaleXAnimation);
            storyboard.Children.Add(scaleYAnimation);
            storyboard.Begin();
        }
    }

    private void AnimateSelection(Border border)
    {
        // 펄스 효과를 위한 스케일 애니메이션
        var scaleTransform = new ScaleTransform
        {
            CenterX = border.ActualWidth / 2,
            CenterY = border.ActualHeight / 2
        };
        border.RenderTransform = scaleTransform;

        var storyboard = new Storyboard();

        var scaleXAnimation = new DoubleAnimationUsingKeyFrames();
        scaleXAnimation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(0), Value = 1.0 });
        scaleXAnimation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(100), Value = 0.95 });
        scaleXAnimation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(200), Value = 1.0 });

        var scaleYAnimation = new DoubleAnimationUsingKeyFrames();
        scaleYAnimation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(0), Value = 1.0 });
        scaleYAnimation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(100), Value = 0.95 });
        scaleYAnimation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(200), Value = 1.0 });

        Storyboard.SetTarget(scaleXAnimation, scaleTransform);
        Storyboard.SetTargetProperty(scaleXAnimation, "ScaleX");

        Storyboard.SetTarget(scaleYAnimation, scaleTransform);
        Storyboard.SetTargetProperty(scaleYAnimation, "ScaleY");

        storyboard.Children.Add(scaleXAnimation);
        storyboard.Children.Add(scaleYAnimation);
        storyboard.Begin();
    }

    private void AnimateYearChange(bool forward)
    {
        // 년도 텍스트 페이드 효과
        var storyboard = new Storyboard();

        var fadeAnimation = new DoubleAnimationUsingKeyFrames();
        fadeAnimation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(0), Value = 1.0 });
        fadeAnimation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(100), Value = 0.3 });
        fadeAnimation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(200), Value = 1.0 });

        Storyboard.SetTarget(fadeAnimation, LbYear);
        Storyboard.SetTargetProperty(fadeAnimation, "Opacity");

        storyboard.Children.Add(fadeAnimation);
        storyboard.Begin();
    }
    #endregion
}
public class SelectedMonthChangedEventArgs : EventArgs
{
    public DateTime SelectedMonth { get; }
    public DateTime PreviousMonth { get; }

    public SelectedMonthChangedEventArgs(DateTime selectedMonth, DateTime previousMonth)
    {
        SelectedMonth = selectedMonth;
        PreviousMonth = previousMonth;
    }
}

