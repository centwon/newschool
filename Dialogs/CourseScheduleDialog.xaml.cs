using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Dialogs;

/// <summary>
/// 수업 시간표 배치 다이얼로그
/// Lesson 테이블 사용
/// </summary>
public sealed partial class CourseScheduleDialog : ContentDialog
{
    private readonly Course _course;
    private readonly ObservableCollection<Lesson> _lessons = [];

    public CourseScheduleDialog(Course course)
    {
        InitializeComponent();
        
        _course = course;
        
        LoadCourseInfo();
        LoadSchedulesAsync();
    }

    /// <summary>
    /// 수업 정보 로드
    /// </summary>
    private void LoadCourseInfo()
    {
        TxtCourseName.Text = _course.Subject;
        TxtCourseInfo.Text = $"{_course.Grade}학년 · {_course.Type} · 주당 {_course.Unit}시간";

        // 강의실 목록 추가
        CBoxRoom.Items.Clear();
        if (_course.RoomList != null && _course.RoomList.Count > 0)
        {
            foreach (var room in _course.RoomList)
            {
                CBoxRoom.Items.Add(room);
            }
        }
    }

    /// <summary>
    /// 시간표 배치 로드 (Lesson 테이블에서)
    /// </summary>
    private async void LoadSchedulesAsync()
    {
        try
        {
            using var repo = new LessonRepository(SchoolDatabase.DbPath);
            var lessons = await repo.GetByCourseAsync(_course.No);

            _lessons.Clear();
            foreach (var lesson in lessons.Where(l => l.IsRecurring).OrderBy(l => l.DayOfWeek).ThenBy(l => l.Period))
            {
                _lessons.Add(lesson);
            }

            ScheduleListView.ItemsSource = _lessons;
            UpdateUI();
        }
        catch (Exception ex)
        {
            ShowError($"시간표 조회 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    /// <summary>
    /// 시간표 추가 버튼 클릭
    /// </summary>
    private void OnAddScheduleClick(object sender, RoutedEventArgs e)
    {
        // 에러 메시지 숨기기
        ErrorInfoBar.IsOpen = false;

        // 유효성 검사
        if (CBoxDayOfWeek.SelectedItem == null)
        {
            ShowError("요일을 선택해주세요.");
            return;
        }

        if (CBoxPeriod.SelectedItem == null)
        {
            ShowError("교시를 선택해주세요.");
            return;
        }

        if (string.IsNullOrWhiteSpace(CBoxRoom.Text))
        {
            ShowError("강의실을 선택하거나 입력해주세요.");
            return;
        }

        var dayOfWeek = int.Parse(((ComboBoxItem)CBoxDayOfWeek.SelectedItem).Tag.ToString()!);
        var period = int.Parse(((ComboBoxItem)CBoxPeriod.SelectedItem).Tag.ToString()!);

        // 중복 체크
        if (_lessons.Any(l => l.DayOfWeek == dayOfWeek && l.Period == period))
        {
            ShowError("이미 추가된 시간표입니다.");
            return;
        }

        // Lesson 추가
        var lesson = new Lesson
        {
            Course = _course.No,
            Teacher = _course.TeacherID,
            Year = _course.Year,
            Semester = _course.Semester,
            DayOfWeek = dayOfWeek,
            Period = period,
            Grade = _course.Grade,
            Room = CBoxRoom.Text.Trim(),
            IsRecurring = true
        };

        _lessons.Add(lesson);
        
        // 정렬
        var sorted = _lessons.OrderBy(l => l.DayOfWeek).ThenBy(l => l.Period).ToList();
        _lessons.Clear();
        foreach (var item in sorted)
        {
            _lessons.Add(item);
        }

        // 입력 폼 초기화
        CBoxDayOfWeek.SelectedItem = null;
        CBoxPeriod.SelectedItem = null;
        CBoxRoom.Text = string.Empty;

        UpdateUI();
    }

    /// <summary>
    /// 시간표 삭제 버튼 클릭
    /// </summary>
    private void OnRemoveScheduleClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button?.Tag is not Lesson lesson) return;

        _lessons.Remove(lesson);
        UpdateUI();
    }

    /// <summary>
    /// 저장 버튼 클릭
    /// </summary>
    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();

        try
        {
            using var repo = new LessonRepository(SchoolDatabase.DbPath);

            // 1. 기존 정기 수업 삭제
            await repo.DeleteByCourseAsync(_course.No);

            // 2. 새 정기 수업 저장
            foreach (var lesson in _lessons)
            {
                lesson.Course = _course.No;
                lesson.Teacher = _course.TeacherID;
                lesson.Year = _course.Year;
                lesson.Semester = _course.Semester;
                lesson.Grade = _course.Grade;
                lesson.IsRecurring = true;
                await repo.CreateAsync(lesson);
            }
        }
        catch (Exception ex)
        {
            ShowError($"저장 중 오류가 발생했습니다.\n{ex.Message}");
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// UI 업데이트
    /// </summary>
    private void UpdateUI()
    {
        var hasSchedules = _lessons.Count > 0;
        
        EmptyState.Visibility = hasSchedules ? Visibility.Collapsed : Visibility.Visible;
        ScheduleListView.Visibility = hasSchedules ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 에러 메시지 표시 (InfoBar 사용)
    /// </summary>
    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }
}
