using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Dialogs;

/// <summary>
/// 학급 시간표 편집 다이얼로그
/// </summary>
public sealed partial class ClassTimetableEditDialog : ContentDialog
{
    private readonly string _schoolCode;
    private readonly int _year;
    private readonly int _semester;
    private readonly int _grade;
    private readonly int _class;
    private ObservableCollection<ClassTimetable> _timetables = new();

    public ClassTimetableEditDialog(
        string schoolCode, int year, int semester, int grade, int classNo,
        List<ClassTimetable> existingTimetables)
    {
        this.InitializeComponent();

        _schoolCode = schoolCode;
        _year = year;
        _semester = semester;
        _grade = grade;
        _class = classNo;

        TxtClassInfo.Text = $"{grade}학년 {classNo}반 시간표 ({year}학년도 {semester}학기)";

        // 기존 시간표 로드
        if (existingTimetables != null)
        {
            foreach (var timetable in existingTimetables.OrderBy(t => t.DayOfWeek).ThenBy(t => t.Period))
            {
                _timetables.Add(timetable);
            }
        }

        TimetableListView.ItemsSource = _timetables;
    }

    /// <summary>
    /// 추가 버튼 클릭
    /// </summary>
    private void OnAddClick(object sender, RoutedEventArgs e)
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

        if (string.IsNullOrWhiteSpace(TxtSubject.Text))
        {
            ShowError("과목명을 입력해주세요.");
            return;
        }

        int dayOfWeek = int.Parse(((ComboBoxItem)CBoxDayOfWeek.SelectedItem).Tag.ToString()!);
        int period = int.Parse(((ComboBoxItem)CBoxPeriod.SelectedItem).Tag.ToString()!);

        // 중복 체크
        if (_timetables.Any(t => t.DayOfWeek == dayOfWeek && t.Period == period))
        {
            ShowError("이미 추가된 시간표입니다.");
            return;
        }

        // 시간표 추가
        var timetable = new ClassTimetable
        {
            SchoolCode = _schoolCode,
            Year = _year,
            Semester = _semester,
            Grade = _grade,
            Class = _class,
            DayOfWeek = dayOfWeek,
            Period = period,
            SubjectName = TxtSubject.Text.Trim(),
            TeacherName = TxtTeacher.Text.Trim()
        };

        _timetables.Add(timetable);

        // 정렬
        var sorted = _timetables.OrderBy(t => t.DayOfWeek).ThenBy(t => t.Period).ToList();
        _timetables.Clear();
        foreach (var item in sorted)
        {
            _timetables.Add(item);
        }

        // 입력 폼 초기화
        CBoxDayOfWeek.SelectedItem = null;
        CBoxPeriod.SelectedItem = null;
        TxtSubject.Text = string.Empty;
        TxtTeacher.Text = string.Empty;
    }

    /// <summary>
    /// 삭제 버튼 클릭
    /// </summary>
    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var timetable = button?.Tag as ClassTimetable;
        if (timetable == null) return;

        _timetables.Remove(timetable);
    }

    /// <summary>
    /// 저장 버튼 클릭
    /// </summary>
    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();

        try
        {
            using var repo = new ClassTimetableRepository(SchoolDatabase.DbPath);

            // 1. 기존 시간표 삭제
            await repo.DeleteByClassAsync(_schoolCode, _year, _semester, _grade, _class);

            // 2. 새 시간표 저장
            foreach (var timetable in _timetables)
            {
                timetable.SchoolCode = _schoolCode;
                timetable.Year = _year;
                timetable.Semester = _semester;
                timetable.Grade = _grade;
                timetable.Class = _class;

                await repo.CreateAsync(timetable);
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
    /// 에러 메시지 표시 (InfoBar 사용)
    /// </summary>
    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }
}
