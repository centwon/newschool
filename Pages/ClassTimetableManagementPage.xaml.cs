using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;

namespace NewSchool.Pages;

/// <summary>
/// 학급 시간표 관리 페이지
/// </summary>
public sealed partial class ClassTimetableManagementPage : Page
{
    //private bool _isInitialized = false;
    private List<ClassTimetable> _timetables = new();

    public ClassTimetableManagementPage()
    {
        this.InitializeComponent();
        this.Loaded += ClassTimetableManagementPage_Loaded;
    }

    private void ClassTimetableManagementPage_Loaded(object sender, RoutedEventArgs e)
    {
        // SchoolFilterPicker가 자동으로 초기화함
    }
    /// <summary>
    /// 조회 버튼 클릭
    /// </summary>
    private async void OnLoadClick(object sender, RoutedEventArgs e)
    {
        // 유효성 검사
        if (FilterPicker.SelectedYear == 0 || FilterPicker.SelectedSemester == 0 ||
            FilterPicker.SelectedGrade == 0 || FilterPicker.SelectedClass == 0)
        {
            await MessageBox.ShowAsync("알림", "학년도, 학기, 학년, 반을 모두 선택해주세요.");
            return;
        }

        await LoadTimetableAsync();
    }

    /// <summary>
    /// 시간표 로드
    /// </summary>
    private async System.Threading.Tasks.Task LoadTimetableAsync()
    {
        try
        {
            int year = FilterPicker.SelectedYear;
            int semester = FilterPicker.SelectedSemester;
            int grade = FilterPicker.SelectedGrade;
            int classNo = FilterPicker.SelectedClass;
            string schoolCode = Settings.SchoolCode.Value;

            using var repo = new ClassTimetableRepository(SchoolDatabase.DbPath);
            _timetables = await repo.GetByClassAsync(schoolCode, year, semester, grade, classNo);

            // 제목 설정
            TxtTitle.Text = $"{grade}학년 {classNo}반 시간표 ({year}학년도 {semester}학기)";

            // 시간표 그리드 그리기
            DrawTimetable();

            // UI 업데이트
            EmptyState.Visibility = Visibility.Collapsed;
            TimetableContainer.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            await MessageBox.ShowAsync("오류", $"시간표 조회 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    /// <summary>
    /// 시간표 그리드 그리기
    /// </summary>
    private void DrawTimetable()
    {
        // 기존 셀 제거 (헤더 제외)
        var cellsToRemove = TimetableGrid.Children
            .Where(c => Grid.GetRow((FrameworkElement)c) > 0)
            .ToList();
        foreach (var cell in cellsToRemove)
        {
            TimetableGrid.Children.Remove(cell);
        }

        // 교시 열 (1-7)
        for (int period = 1; period <= 7; period++)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Background = new SolidColorBrush(Colors.WhiteSmoke)
            };
            Grid.SetRow(border, period);
            Grid.SetColumn(border, 0);

            var textBlock = new TextBlock
            {
                Text = $"{period}교시",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            border.Child = textBlock;
            TimetableGrid.Children.Add(border);
        }

        // 시간표 셀 (요일 1-5, 교시 1-7)
        for (int day = 1; day <= 5; day++)
        {
            for (int period = 1; period <= 7; period++)
            {
                var timetable = _timetables.FirstOrDefault(t => 
                    t.DayOfWeek == day && t.Period == period);

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Colors.LightGray),
                    BorderThickness = new Thickness(0, 0, day == 5 ? 0 : 1, 1),
                    Background = new SolidColorBrush(Colors.White),
                    Padding = new Thickness(8),
                    MinHeight = 80
                };
                Grid.SetRow(border, period);
                Grid.SetColumn(border, day);

                if (timetable != null)
                {
                    var stackPanel = new StackPanel
                    {
                        Spacing = 4
                    };

                    var subjectText = new TextBlock
                    {
                        Text = timetable.SubjectName,
                        FontSize = 14,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    };
                    stackPanel.Children.Add(subjectText);

                    if (!string.IsNullOrEmpty(timetable.TeacherName))
                    {
                        var teacherText = new TextBlock
                        {
                            Text = timetable.TeacherName,
                            FontSize = 12,
                            Foreground = new SolidColorBrush(Colors.Gray)
                        };
                        stackPanel.Children.Add(teacherText);
                    }
                    // Room은 학급 시간표에서 표시하지 않음

                    border.Child = stackPanel;
                }

                TimetableGrid.Children.Add(border);
            }
        }
    }

    /// <summary>
    /// 시간표 편집 버튼 클릭
    /// </summary>
    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (FilterPicker.SelectedYear == 0 || FilterPicker.SelectedGrade == 0 || FilterPicker.SelectedClass == 0)
        {
            return;
        }

        int year = FilterPicker.SelectedYear;
        int semester = FilterPicker.SelectedSemester;
        int grade = FilterPicker.SelectedGrade;
        int classNo = FilterPicker.SelectedClass;
        string schoolCode = Settings.SchoolCode.Value;
        var dialog = new ClassTimetableEditDialog(schoolCode, year, semester, grade, classNo, _timetables);
        dialog.XamlRoot = this.XamlRoot;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await LoadTimetableAsync();
        }
        await MessageBox.ShowAsync("완료", "시간표가 저장되었습니다.");

    }

    /// <summary>
    /// 시간표 삭제 버튼 클릭
    /// </summary>
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (FilterPicker.SelectedYear == 0 || FilterPicker.SelectedGrade == 0 || FilterPicker.SelectedClass == 0)
        {
            return;
        }

        int year = FilterPicker.SelectedYear;
        int semester = FilterPicker.SelectedSemester;
        int grade = FilterPicker.SelectedGrade;
        int classNo = FilterPicker.SelectedClass;

        // 확인 다이얼로그
        var confirmed = await MessageBox.ShowConfirmAsync(
            $"{grade}학년 {classNo}반의 시간표를 삭제하시겠습니까?",
            "시간표 삭제", "삭제", "취소");
        if (!confirmed) return;

        try
        {
            string schoolCode = Settings.SchoolCode.Value;

            using var repo = new ClassTimetableRepository(SchoolDatabase.DbPath);
            int count = await repo.DeleteByClassAsync(schoolCode, year, semester, grade, classNo);

            if (count > 0)
            {
                await MessageBox.ShowAsync("완료", $"{count}개의 시간표가 삭제되었습니다.");
                
                // UI 초기화
                EmptyState.Visibility = Visibility.Visible;
                TimetableContainer.Visibility = Visibility.Collapsed;
                _timetables.Clear();
            }
            else
            {
                await MessageBox.ShowAsync("알림", "삭제할 시간표가 없습니다.");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync("오류", $"시간표 삭제 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

}
