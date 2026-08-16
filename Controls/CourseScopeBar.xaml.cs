using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;

namespace NewSchool.Controls;

/// <summary>
/// 수업 관리 탭의 머리 필터 — 학년도 · 학기 · 학년 · 수업.
///
/// 탭마다 하나씩 둔다. 예전에는 페이지 헤더에 하나만 두고 탭들이 공유했는데,
/// 탭보다 <b>위</b>에 탭이 쓰는 컨트롤이 있으니 위아래 순서가 뒤집혀 보였다.
///
/// 목록은 스스로 읽지 않는다 — 페이지가 한 번 읽어 모든 바에 나눠 준다.
/// (학년도·학년 목록을 바마다 조회하면 탭 수만큼 같은 질의가 돌고,
///  바들끼리 선택이 어긋날 여지도 생긴다)
/// </summary>
public sealed partial class CourseScopeBar : UserControl
{
    /// <summary>바깥에서 값을 맞추는 동안 이벤트를 막는다.</summary>
    private bool _suppress;

    private bool _showCourse = true;
    private bool _showGrade = true;

    /// <summary>
    /// 수업 콤보 표시 여부. 수업 개설 탭처럼 목록 자체가 대상인 곳에서는 끈다.
    /// </summary>
    public bool ShowCourse
    {
        get => _showCourse;
        set
        {
            _showCourse = value;
            CourseHost.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 학년 콤보 표시 여부. 학년은 <b>목록을 거르는</b> 용도라, 대상이 수업 하나인 탭에서는 끈다.
    /// </summary>
    public bool ShowGrade
    {
        get => _showGrade;
        set
        {
            _showGrade = value;
            CBoxGrade.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public int Year => GetTag(CBoxYear);
    public int Semester => GetTag(CBoxSemester);

    /// <summary>0 = 전체</summary>
    public int Grade => GetTag(CBoxGrade);

    public Course? SelectedCourse => CBoxCourse.SelectedItem as Course;

    /// <summary>학년도·학기·학년 중 하나가 바뀌었다</summary>
    public event EventHandler? ScopeChanged;

    /// <summary>수업 콤보에서 다른 수업을 골랐다</summary>
    public event EventHandler<Course>? CourseChanged;

    public CourseScopeBar()
    {
        this.InitializeComponent();

        _suppress = true;
        CBoxSemester.Items.Add(new ComboBoxItem { Content = "1학기", Tag = 1 });
        CBoxSemester.Items.Add(new ComboBoxItem { Content = "2학기", Tag = 2 });
        _suppress = false;
    }

    #region 목록 채우기 (페이지가 호출)

    /// <summary>학년도 목록</summary>
    public void SetYears(IReadOnlyList<int> years, int selected)
    {
        _suppress = true;
        try
        {
            CBoxYear.Items.Clear();
            foreach (var year in years)
                CBoxYear.Items.Add(new ComboBoxItem { Content = $"{year}학년도", Tag = year });

            SelectByTag(CBoxYear, selected);
        }
        finally { _suppress = false; }
    }

    /// <summary>학년도만 조용히 옮긴다 (목록은 이미 같은 것을 들고 있다)</summary>
    public void SelectYear(int year)
    {
        _suppress = true;
        try { SelectByTag(CBoxYear, year); }
        finally { _suppress = false; }
    }

    /// <summary>학기 선택</summary>
    public void SetSemester(int semester)
    {
        _suppress = true;
        try { SelectByTag(CBoxSemester, semester); }
        finally { _suppress = false; }
    }

    /// <summary>학년 목록 — 맨 앞에 "전체"(0) 를 넣는다</summary>
    public void SetGrades(IReadOnlyList<int> grades, int selected)
    {
        _suppress = true;
        try
        {
            CBoxGrade.Items.Clear();
            CBoxGrade.Items.Add(new ComboBoxItem { Content = "전체 학년", Tag = 0 });
            foreach (var grade in grades)
                CBoxGrade.Items.Add(new ComboBoxItem { Content = $"{grade}학년", Tag = grade });

            SelectByTag(CBoxGrade, selected);
        }
        finally { _suppress = false; }
    }

    /// <summary>수업 목록</summary>
    public void SetCourses(IReadOnlyList<Course> courses, Course? selected)
    {
        _suppress = true;
        try
        {
            // ItemsSource 를 갈아 끼우면 선택이 날아가므로 항목을 직접 채운다.
            CBoxCourse.Items.Clear();
            foreach (var course in courses)
                CBoxCourse.Items.Add(course);

            CBoxCourse.SelectedItem = selected != null
                ? courses.FirstOrDefault(c => c.No == selected.No)
                : null;
        }
        finally { _suppress = false; }
    }

    /// <summary>수업만 조용히 맞춘다 (다른 바에서 골랐을 때)</summary>
    public void SelectCourse(Course? course)
    {
        _suppress = true;
        try
        {
            CBoxCourse.SelectedItem = course != null
                ? CBoxCourse.Items.OfType<Course>().FirstOrDefault(c => c.No == course.No)
                : null;
        }
        finally { _suppress = false; }
    }

    #endregion

    #region 이벤트

    private void OnScopeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        ScopeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCourseSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (CBoxCourse.SelectedItem is Course course)
            CourseChanged?.Invoke(this, course);
    }

    #endregion

    #region 헬퍼

    private static int GetTag(ComboBox combo)
        => combo.SelectedItem is ComboBoxItem item && item.Tag is int value ? value : 0;

    private static void SelectByTag(ComboBox combo, int tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem boxItem && boxItem.Tag is int value && value == tag)
            {
                combo.SelectedItem = boxItem;
                return;
            }
        }

        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    #endregion
}
