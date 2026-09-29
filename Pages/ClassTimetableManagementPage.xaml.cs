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

    /// <summary>
    /// 지금 화면에 떠 있는 시간표가 어느 학급 것인지 — [조회] 로 읽었을 때만 정해진다.
    ///
    /// <para>이 화면은 필터를 바꿔도 [조회] 전까지 다시 읽지 않는다. 그런데 [편집]·[삭제] 는
    /// <b>지금 필터</b>의 학급으로 움직였다: 3학년 1반을 조회해 두고 필터만 2반으로 바꾼 뒤 [편집] 을
    /// 누르면 2반 편집 창에 <b>1반 시간표</b>(<c>_timetables</c>)가 채워져, 저장하면 1반 시간표가
    /// 2반에 들어갔다. [삭제] 는 화면에 없는 학급을 지웠다.</para>
    /// </summary>
    private (int Year, int Semester, int Grade, int Class)? _loaded;

    /// <summary>[편집]·[삭제] 전에 — 화면의 시간표가 지금 필터의 학급 것인지 본다.</summary>
    private async Task<bool> EnsureShowingSelectedClassAsync()
    {
        var selected = (YearSemPicker.Year, YearSemPicker.Semester, ClassFilter.Grade, ClassFilter.ClassNum);
        if (_loaded == selected) return true;

        await MessageBox.ShowAsync(
            $"{selected.Grade}학년 {selected.ClassNum}반({selected.Year}학년도 {selected.Semester}학기)은 아직 조회하지 않았습니다.\n" +
            "[조회] 를 먼저 눌러 이 학급의 시간표를 불러와 주세요.",
            "알림");
        return false;
    }

    public ClassTimetableManagementPage()
    {
        this.InitializeComponent();

        // 교시 행 — 개수는 PeriodCounts.MaxSupported 하나가 정한다. 예전에는 XAML 에 일곱 줄을 손으로
        // 적고 그리기도 1~7 로 돌아서, 바로 이 화면의 편집 창(ClassTimetableEditDialog)과 학급 시간표
        // 컨트롤(TimetableControl)이 받는 8교시가 저장은 돼도 여기서는 보이지 않았다.
        for (int period = 1; period <= PeriodCounts.MaxSupported; period++)
            TimetableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
    }

    private async void YearSemPicker_YearSemesterChanged(object sender, YearSemesterChangedEventArgs e)
    {
        await ClassFilter.LoadAsync(e.Year, e.Semester);
    }

    /// <summary>
    /// 조회 버튼 클릭
    /// </summary>
    private async void OnLoadClick(object sender, RoutedEventArgs e)
    {
        // 유효성 검사
        if (YearSemPicker.Year == 0 || YearSemPicker.Semester == 0 ||
            ClassFilter.Grade == 0 || ClassFilter.ClassNum == 0)
        {
            await MessageBox.ShowAsync("학년도, 학기, 학년, 반을 모두 선택해주세요.", "알림");
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
            int year = YearSemPicker.Year;
            int semester = YearSemPicker.Semester;
            int grade = ClassFilter.Grade;
            int classNo = ClassFilter.ClassNum;
            string schoolCode = Settings.SchoolCode.Value;

            using var repo = new ClassTimetableRepository(SchoolDatabase.DbPath);
            _timetables = await repo.GetByClassAsync(schoolCode, year, semester, grade, classNo);
            _loaded = (year, semester, grade, classNo);

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
            await MessageBox.ShowAsync($"시간표 조회 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 테마 브러시를 이름으로 가져온다.
    ///
    /// <para>이 화면은 셀을 XAML 이 아니라 코드로 만들기 때문에, 형제 시간표들
    /// (<c>WeeklyTimetableView</c>·<c>CourseTimetableBoard</c>·<c>TimetableControl</c>)이
    /// XAML 에서 <c>ThemeResource</c> 로 지키던 규칙에서 혼자 새어나가 있었다. 셀 배경을
    /// <c>Colors.White</c> 로 박아 놓고 과목명에는 색을 주지 않아, <b>다크 테마에서는
    /// 흰 배경에 흰 글씨가 되어 시간표가 통째로 보이지 않았다</b>.</para>
    /// </summary>
    private static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

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

        // 교시 열 (1 ~ PeriodCounts.MaxSupported — 생성자 주석)
        for (int period = 1; period <= PeriodCounts.MaxSupported; period++)
        {
            var border = new Border
            {
                BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Background = ThemeBrush("SubtleFillColorSecondaryBrush")
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

        // 시간표 셀 (요일 1-5, 교시 1 ~ PeriodCounts.MaxSupported)
        for (int day = 1; day <= 5; day++)
        {
            for (int period = 1; period <= PeriodCounts.MaxSupported; period++)
            {
                var timetable = _timetables.FirstOrDefault(t => 
                    t.DayOfWeek == day && t.Period == period);

                var border = new Border
                {
                    BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush"),
                    BorderThickness = new Thickness(0, 0, day == 5 ? 0 : 1, 1),
                    Background = ThemeBrush("LayerFillColorDefaultBrush"),
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
                            Foreground = ThemeBrush("TextFillColorSecondaryBrush")
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
        if (YearSemPicker.Year == 0 || ClassFilter.Grade == 0 || ClassFilter.ClassNum == 0)
        {
            return;
        }
        if (!await EnsureShowingSelectedClassAsync()) return;

        int year = YearSemPicker.Year;
        int semester = YearSemPicker.Semester;
        int grade = ClassFilter.Grade;
        int classNo = ClassFilter.ClassNum;
        string schoolCode = Settings.SchoolCode.Value;
        var dialog = new ClassTimetableEditDialog(schoolCode, year, semester, grade, classNo, _timetables);
        dialog.XamlRoot = this.XamlRoot;

        var result = await MessageBox.ShowDialogAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            await LoadTimetableAsync();
            await MessageBox.ShowAsync("시간표가 저장되었습니다.", "완료");
        }
    }

    /// <summary>
    /// 시간표 삭제 버튼 클릭
    /// </summary>
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (YearSemPicker.Year == 0 || ClassFilter.Grade == 0 || ClassFilter.ClassNum == 0)
        {
            return;
        }
        if (!await EnsureShowingSelectedClassAsync()) return;

        int year = YearSemPicker.Year;
        int semester = YearSemPicker.Semester;
        int grade = ClassFilter.Grade;
        int classNo = ClassFilter.ClassNum;

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
                await MessageBox.ShowAsync($"{count}개의 시간표가 삭제되었습니다.", "완료");
                
                // UI 초기화
                EmptyState.Visibility = Visibility.Visible;
                TimetableContainer.Visibility = Visibility.Collapsed;
                _timetables.Clear();
                _loaded = null;
            }
            else
            {
                await MessageBox.ShowAsync("삭제할 시간표가 없습니다.", "알림");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"시간표 삭제 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
    }

}
