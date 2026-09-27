using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NewSchool.Models;
using NewSchool.ViewModels;

namespace NewSchool.Controls;

/// <summary>
/// 학급 시간표 표시 UserControl(과목 + 교사). 학급 일지(<c>ClassDiaryBox</c>)가 DataContext 에
/// <see cref="TimetableViewModel"/> 을 넣어 쓴다.
/// 월~금 × <see cref="PeriodCounts.MaxSupported"/> 교시 그리드(행은 코드에서 만든다).
///
/// <para>예전에는 교사용 모드(과목 + 강의실, 그 주 변경 얹기, 칸 누르기)도 있어 수업 홈의 내 시간표를
/// 그렸다. 수업 홈이 <see cref="WeeklyTimetableView"/> 의 간단 모드로 옮겨 가면서(2026-09-27) 쓰는 곳이
/// 없어져 걷어냈다 — 교사 시간표는 이제 그 컨트롤 하나가 그린다.</para>
/// </summary>
public sealed partial class TimetableControl : UserControl
{
    public TimetableControl()
    {
        this.InitializeComponent();
        BuildPeriodRows();
        this.DataContextChanged += TimetableControl_DataContextChanged;
    }

    /// <summary>
    /// 교시 행과 왼쪽 교시 번호 열을 만든다 — 개수는 <see cref="PeriodCounts.MaxSupported"/> 하나가 정한다.
    ///
    /// <para>XAML 에 <c>RowDefinition</c> 일곱 개와 번호 <c>Border</c> 일곱 개를 손으로 적어 두었을
    /// 때는, 설정에서 8교시를 허용해도 이 격자에는 그릴 자리가 없어 <b>그 교시 수업만 조용히
    /// 사라졌다.</b> 상한을 늘릴 때 잊는 파일이 생기지 않도록 여기서 만든다.</para>
    ///
    /// <para>번호 칸은 <b>0번 열</b>에 놓는다 — <see cref="RemoveExistingCells"/> 가
    /// "행&gt;0 그리고 열&gt;0" 만 걷어내므로 다시 그릴 때 살아남는다.</para>
    /// </summary>
    private void BuildPeriodRows()
    {
        for (int period = 1; period <= PeriodCounts.MaxSupported; period++)
        {
            TimetableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var label = new Border
            {
                Padding = new Thickness(4),
                Background = (SolidColorBrush)Application.Current.Resources["LayerFillColorDefaultBrush"],
                Child = new TextBlock
                {
                    Text = period.ToString(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Medium
                }
            };

            Grid.SetRow(label, period);
            Grid.SetColumn(label, 0);
            TimetableGrid.Children.Add(label);
        }
    }

    private void TimetableControl_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (DataContext is TimetableViewModel viewModel)
        {
            UpdateTimetable(viewModel);
        }
    }

    /// <summary>
    /// 시간표 업데이트
    /// </summary>
    private void UpdateTimetable(TimetableViewModel viewModel)
    {
        // 제목 설정
        //TitleTextBlock.Text = viewModel.Title;

        // 기존 셀 제거 (헤더는 유지)
        RemoveExistingCells();

        // 새 셀 생성
        CreateTimetableCells(viewModel);
    }

    /// <summary>
    /// 기존에 동적으로 생성된 셀 제거
    /// </summary>
    private void RemoveExistingCells()
    {
        var cellsToRemove = TimetableGrid.Children
            .Where(child => Grid.GetRow(child as FrameworkElement) > 0 && 
                           Grid.GetColumn(child as FrameworkElement) > 0)
            .ToList();

        foreach (var cell in cellsToRemove)
        {
            TimetableGrid.Children.Remove(cell);
        }
    }

    /// <summary>
    /// 시간표 셀 생성 (월~금 × <see cref="PeriodCounts.MaxSupported"/> 교시)
    /// </summary>
    private void CreateTimetableCells(TimetableViewModel viewModel)
    {
        for (int day = 1; day <= 5; day++) // 월~금
        {
            for (int period = 1; period <= PeriodCounts.MaxSupported; period++)
            {
                var item = viewModel.GetItem(day, period);
                var cell = item != null
                    ? CreateCell(item)
                    : new Border
                    {
                        Padding = new Thickness(2),
                        Background = (SolidColorBrush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]
                    };
                Grid.SetRow(cell, period); // period (1~7)
                Grid.SetColumn(cell, day); // day (1~5)
                TimetableGrid.Children.Add(cell);
            }
        }
    }

    /// <summary>
    /// 개별 셀 생성
    /// </summary>
    private Border CreateCell(TimetableItemViewModel item)
    {
        var border = new Border
        {
            Padding = new Thickness(2)
        };

        if (item.IsEmpty)
        {
            // 빈 시간
            border.Background = (SolidColorBrush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        }
        else
        {
            // 수업 정보
            var stackPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 0
            };

            // 과목명 — 학급 시간표에는 그 날의 변경(휴강·교체…)이 얹히지 않는다
            stackPanel.Children.Add(new TextBlock
            {
                Text = item.SubjectName,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

            // 교사명
            if (!string.IsNullOrEmpty(item.TeacherName))
            {
                stackPanel.Children.Add(new TextBlock
                {
                    Text = item.TeacherName,
                    FontSize = 10,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    TextAlignment = TextAlignment.Center
                });
            }

            border.Child = stackPanel;
            border.Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        }

        return border;
    }
}
