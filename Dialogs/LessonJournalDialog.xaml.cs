using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;

namespace NewSchool.Dialogs;

/// <summary>
/// 수업 일지 새 글의 머리 정보를 모으는 다이얼로그.
///
/// 게시글에는 교시·강의실·단원을 담을 구조화된 칸이 없다(Post 는 Category·Subject·
/// Title·Content 뿐). 그래서 <b>제목과 본문 첫 줄</b>로 옮겨 담는다 —
/// 크로스 DB 외래키를 만들지 않으면서도 본문은 PlainText 로 검색에 걸린다.
///
///   제목    3/6 3교시 역사 1-1
///   본문1줄 1-1-1 덧셈과 뺄셈의 혼합 계산 (p.8~11)
///
/// 둘 다 사용자가 편집기에서 고칠 수 있는 <b>기본값</b>이다.
/// </summary>
public sealed partial class LessonJournalDialog : ContentDialog
{
    /// <summary>생성된 제목(예: "3/6 3교시 역사 1-1")</summary>
    public string GeneratedTitle { get; private set; } = string.Empty;

    /// <summary>본문 첫 줄(단원을 고르지 않았으면 빈 문자열)</summary>
    public string GeneratedFirstLine { get; private set; } = string.Empty;

    private List<Course> _courses = [];
    private List<CourseSection> _sections = [];

    public LessonJournalDialog()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
        this.PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        DpDate.Date = DateTimeOffset.Now;

        // 교시 — 학교 설정의 요일별 교시 수 중 가장 많은 날을 기준으로 채운다
        var pc = PeriodCounts.Parse(Settings.PeriodsPerDay.Value);
        int periods = Math.Max(1, new[] { pc.Mon, pc.Tue, pc.Wed, pc.Thu, pc.Fri }.Max());
        for (int i = 1; i <= periods; i++)
            CmbPeriod.Items.Add(new ComboBoxItem { Content = $"{i}교시", Tag = i });
        CmbPeriod.SelectedIndex = 0;

        try
        {
            using var service = new CourseService();
            _courses = await service.GetMyCoursesAsync();
            CmbCourse.ItemsSource = _courses;
            if (_courses.Count > 0) CmbCourse.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            ShowError($"교과 목록을 불러오지 못했습니다.\n{ex.Message}");
        }

        DpDate.DateChanged += (_, _) => UpdatePreview();
        CmbPeriod.SelectionChanged += (_, _) => UpdatePreview();
        CmbRoom.SelectionChanged += (_, _) => UpdatePreview();
        CmbSection.SelectionChanged += (_, _) => UpdatePreview();

        UpdatePreview();
    }

    private async void CmbCourse_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbCourse.SelectedItem is not Course course)
        {
            CmbRoom.ItemsSource = null;
            CmbSection.ItemsSource = null;
            UpdatePreview();
            return;
        }

        // 강의실 — 교과에 등록된 목록. 직접 입력도 가능(IsEditable).
        var rooms = course.RoomList;
        CmbRoom.ItemsSource = rooms;
        if (rooms.Count > 0) CmbRoom.SelectedIndex = 0;

        await LoadSectionsAsync(course.No);
        UpdatePreview();
    }

    private async Task LoadSectionsAsync(int courseNo)
    {
        CmbSection.Items.Clear();
        _sections = [];

        try
        {
            using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
            _sections = await repo.GetByCourseAsync(courseNo);

            foreach (var s in _sections.OrderBy(x => x.SortOrder))
            {
                CmbSection.Items.Add(new ComboBoxItem
                {
                    Content = $"{s.FullPath} {s.SectionName}",
                    Tag = s.No
                });
            }

            CmbSection.IsEnabled = _sections.Count > 0;
            TxtSectionHint.Text = _sections.Count > 0
                ? string.Empty
                : "이 교과에 등록된 단원이 없습니다. 수업 관리 → 단원 관리에서 추가할 수 있습니다.";
        }
        catch (Exception ex)
        {
            CmbSection.IsEnabled = false;
            TxtSectionHint.Text = $"단원을 불러오지 못했습니다: {ex.Message}";
        }
    }

    private CourseSection? SelectedSection =>
        CmbSection.SelectedItem is ComboBoxItem item && item.Tag is int no
            ? _sections.FirstOrDefault(s => s.No == no)
            : null;

    /// <summary>제목: "3/6 3교시 역사 1-1" — 빈 조각은 건너뛴다.</summary>
    private string BuildTitle()
    {
        var parts = new List<string>();

        if (DpDate.Date is { } d) parts.Add($"{d.Month}/{d.Day}");
        if (CmbPeriod.SelectedItem is ComboBoxItem p && p.Tag is int period) parts.Add($"{period}교시");
        if (CmbCourse.SelectedItem is Course c && !string.IsNullOrWhiteSpace(c.Subject)) parts.Add(c.Subject);

        var room = (CmbRoom.SelectedItem as string) ?? CmbRoom.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(room)) parts.Add(room);

        return string.Join(" ", parts);
    }

    /// <summary>본문 첫 줄: "1-1-1 덧셈과 뺄셈의 혼합 계산 (p.8~11)"</summary>
    private string BuildFirstLine()
    {
        var s = SelectedSection;
        if (s == null) return string.Empty;

        var line = $"{s.FullPath} {s.SectionName}";
        var pages = s.PageRangeDisplay;   // "p.8~12" 또는 ""
        return string.IsNullOrEmpty(pages) ? line : $"{line} ({pages})";
    }

    private void UpdatePreview()
    {
        TxtPreviewTitle.Text = BuildTitle();
        var first = BuildFirstLine();
        TxtPreviewBody.Text = string.IsNullOrEmpty(first) ? "(단원 없음)" : first;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var title = BuildTitle();
        if (string.IsNullOrWhiteSpace(title))
        {
            args.Cancel = true;
            ShowError("날짜·교시·교과 중 하나 이상은 있어야 합니다.");
            return;
        }

        GeneratedTitle = title;
        GeneratedFirstLine = BuildFirstLine();
    }

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }
}
