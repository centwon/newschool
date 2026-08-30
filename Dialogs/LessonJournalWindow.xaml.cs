using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Board;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using Windows.Graphics;

namespace NewSchool.Dialogs;

/// <summary>
/// 수업 일지 작성·편집 창.
///
/// 수업 일지는 게시판 글(<c>board.db</c> 의 <see cref="Post"/>)이지만, 한 줄 적자고
/// 게시판 편집기 페이지로 화면을 갈아 끼울 이유는 없다. 머리 정보(날짜·교시·교과·강의실·단원)와
/// 본문·첨부를 한 창에 놓고 저장까지 여기서 끝낸다.
///
/// 게시글에는 교시·강의실·단원을 담을 구조화된 칸이 없다(Post 는 Category·Subject·Title·Content 뿐).
/// 그래서 <b>제목</b>(<see cref="LessonJournalTitle"/>)과 <b>본문 첫 줄</b>로 옮겨 담는다 —
/// 크로스 DB 외래키를 만들지 않으면서 본문은 PlainText 로 검색에 걸린다.
///
/// 창 구조는 형제인 <c>MemoEditDialog</c> 와 같다(Window + Result + ShowDialogAsync).
/// </summary>
public sealed partial class LessonJournalWindow : Window
{
    private readonly Post _post;
    private readonly bool _isNew;
    private readonly LessonSlotSeed? _seed;
    private readonly TaskCompletionSource<bool> _dialogResult = new();

    private List<Course> _courses = [];
    private List<CourseSection> _sections = [];

    /// <summary>제목이 아직 머리 정보를 따라다니는가. 사용자가 제목을 직접 고치면 꺼진다.</summary>
    private bool _titleFollowsHeader = true;

    private bool _isLoading = true;

    /// <summary>저장했으면 true.</summary>
    public bool Result { get; private set; }

    /// <summary>저장된 글 번호(저장했을 때만 의미 있다).</summary>
    public int SavedPostNo { get; private set; }

    /// <summary>새 수업 일지. <paramref name="seed"/> 가 있으면 시간표 칸의 값으로 채워 연다.</summary>
    public LessonJournalWindow(LessonSlotSeed? seed = null)
    {
        _post = new Post
        {
            DateTime = DateTime.Now,
            User = Settings.AuthorName,
            Category = LessonJournalComposer.Category,
            Subject = LessonJournalComposer.Subject
        };
        _isNew = true;
        _seed = seed;

        Initialize("수업 일지 쓰기");
    }

    /// <summary>이미 써 둔 수업 일지 편집.</summary>
    public LessonJournalWindow(Post post)
    {
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _isNew = false;

        Initialize("수업 일지");
    }

    private void Initialize(string title)
    {
        InitializeComponent();

        Title = title;
        SetWindowSize(980, 760);

        // 안내·오류 대화상자가 메인 창이 아니라 이 창 위에 뜨도록 등록한다.
        NewSchool.Controls.MessageBox.TrackWindow(this);

        FileList.Category = LessonJournalComposer.Category;

        TxtTitle.TextChanged += OnTitleEdited;
        DpDate.DateChanged += (_, _) => OnHeaderChanged();
        CmbPeriod.SelectionChanged += (_, _) => OnHeaderChanged();
        CmbSection.SelectionChanged += (_, _) => OnSectionChanged();

        // 강의실은 목록에서 고르기도 하고 직접 적기도 한다(IsEditable). SelectionChanged 만 듣던
        // 때는 목록에 없는 강의실을 타이핑하면 제목이 옛 강의실 그대로 저장됐다.
        CmbRoom.RegisterPropertyChangedCallback(ComboBox.TextProperty, (_, _) => OnHeaderChanged());

        Closed += OnWindowClosed;
    }

    #region 창 크기 · 위치

    private void SetWindowSize(int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow.GetFromWindowId(windowId).Resize(new SizeInt32(width, height));
    }

    private void CenterOnParent(Window parent)
    {
        var parentHwnd = WinRT.Interop.WindowNative.GetWindowHandle(parent);
        var parentWindow = AppWindow.GetFromWindowId(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(parentHwnd));

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));

        var pos = parentWindow.Position;
        var size = parentWindow.Size;
        var mine = appWindow.Size;

        appWindow.Move(new PointInt32(
            pos.X + (size.Width - mine.Width) / 2,
            pos.Y + (size.Height - mine.Height) / 2));
    }

    #endregion

    /// <summary>창을 띄우고 닫힐 때까지 기다린다. 저장했으면 true.</summary>
    public async Task<bool> ShowDialogAsync(Window? parent = null)
    {
        if (parent != null) CenterOnParent(parent);

        // 메인 창이 '항상 위에' 면 이 창도 같은 레벨로 올린다 — 아니면 뒤로 숨는다.
        if (Settings.TopMost.Value)
            MainWindow.SetAlwaysOnTop(this, true);

        // 메인 창과 같은 테마로 연다
        NewSchool.Helpers.ThemeHelper.Apply(this);

        Activate();
        await LoadAsync();
        return await _dialogResult.Task;
    }

    #region 로드

    private async Task LoadAsync()
    {
        try
        {
            // 교시 — 학교 설정의 요일별 교시 수 중 가장 많은 날을 기준으로 채운다
            var pc = PeriodCounts.Parse(Settings.PeriodsPerDay.Value);
            int periods = Math.Max(1, new[] { pc.Mon, pc.Tue, pc.Wed, pc.Thu, pc.Fri }.Max());
            for (int i = 1; i <= periods; i++)
                CmbPeriod.Items.Add(new ComboBoxItem { Content = $"{i}교시", Tag = i });

            using (var service = new CourseService())
                _courses = await service.GetMyCoursesAsync();

            CmbCourse.ItemsSource = _courses;

            if (_isNew) await LoadNewAsync();
            else await LoadExistingAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonJournalWindow] 로드 실패: {ex.Message}");
            await MessageBox.ShowErrorAsync("수업 일지를 여는 중 오류가 발생했습니다.", ex);
        }
        finally
        {
            _isLoading = false;
            UpdateTitleFromHeader();
            UpdateHint();
        }
    }

    private async Task LoadNewAsync()
    {
        DpDate.Date = new DateTimeOffset(_seed?.Date ?? DateTime.Today);
        SelectPeriod(_seed?.Period ?? 0);

        await SelectCourseAsync(
            _courses.FirstOrDefault(c => c.No == _seed?.CourseNo)
            ?? _courses.FirstOrDefault(c => c.Subject == _seed?.Subject)
            ?? _courses.FirstOrDefault());

        // 강의실은 교과를 고를 때 그 교과의 첫 강의실로 채워진다. 시간표 칸이 알려 준
        // 강의실이 있으면 그쪽이 맞으므로 뒤에서 다시 적는다.
        SetRoom(_seed?.Room);
    }

    private async Task LoadExistingAsync()
    {
        TxtTitle.Text = _post.Title ?? string.Empty;
        Editor.LoadFlow(_post.Content);

        await ApplyTitleToHeaderAsync(_post.Title);

        // 머리 정보로 그대로 되만들어지는 제목이면 계속 따라다녀도 안전하다.
        // 사용자가 손으로 고쳐 둔 제목이면 건드리지 않는다.
        _titleFollowsHeader = BuildTitle() == (_post.Title ?? string.Empty);

        if (_post.No > 0)
        {
            FileList.Post = _post;

            try
            {
                using var service = NewSchool.Board.Board.CreateService();
                FileList.LoadFiles(await service.GetPostFilesByPostAsync(_post.No), _post.Category);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LessonJournalWindow] 첨부 로드 실패: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 저장된 제목에서 머리 정보를 되읽는다. 과목명에 공백이 든 교과가 있어서
    /// (예: "생활과 윤리") 토막내기로는 갈라지지 않는다 — 담당 교과 목록과 맞춰 본다.
    /// 못 알아보는 조각은 그냥 두고 넘어간다.
    /// </summary>
    private async Task ApplyTitleToHeaderAsync(string? title)
    {
        var head = LessonJournalTitle.Head(title);
        if (head == null) return;

        var (month, day, period, tail) = head.Value;

        try
        {
            DpDate.Date = new DateTimeOffset(new DateTime(_post.DateTime.Year, month, day));
        }
        catch (ArgumentOutOfRangeException)
        {
            // 제목의 날짜가 그 해에 없는 날이면(2/30 등) 날짜는 손대지 않는다.
        }

        SelectPeriod(period);

        // 과목명이 가장 긴 것부터 맞춰 본다 — "윤리" 와 "생활과 윤리" 가 함께 있을 수 있다.
        var course = _courses
            .Where(c => !string.IsNullOrWhiteSpace(c.Subject) && tail.StartsWith(c.Subject, StringComparison.Ordinal))
            .OrderByDescending(c => c.Subject.Length)
            .FirstOrDefault();

        await SelectCourseAsync(course);

        if (course != null)
            SetRoom(tail[course.Subject.Length..].Trim());
    }

    /// <summary>교과를 고르고 그 교과의 강의실·단원을 채운다.</summary>
    private async Task SelectCourseAsync(Course? course)
    {
        CmbCourse.SelectedItem = course;
        if (course == null) return;

        CmbRoom.ItemsSource = course.RoomList;
        if (course.RoomList.Count > 0) CmbRoom.SelectedIndex = 0;

        await LoadSectionsAsync(course.No);
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
                CmbSection.Items.Add(new ComboBoxItem { Content = $"{s.FullPath} {s.SectionName}", Tag = s.No });

            CmbSection.IsEnabled = _sections.Count > 0;
        }
        catch (Exception ex)
        {
            CmbSection.IsEnabled = false;
            Debug.WriteLine($"[LessonJournalWindow] 단원 로드 실패: {ex.Message}");
        }
    }

    private void SelectPeriod(int period)
    {
        foreach (var obj in CmbPeriod.Items)
        {
            if (obj is ComboBoxItem { Tag: int p } item && p == period)
            {
                CmbPeriod.SelectedItem = item;
                return;
            }
        }

        if (_isNew) CmbPeriod.SelectedIndex = 0;
    }

    #endregion

    #region 머리 정보 → 제목 · 본문 첫 줄

    private async void CmbCourse_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        var course = CmbCourse.SelectedItem as Course;

        CmbRoom.ItemsSource = course?.RoomList;
        if (course is { RoomList.Count: > 0 }) CmbRoom.SelectedIndex = 0;

        if (course != null) await LoadSectionsAsync(course.No);
        else { CmbSection.Items.Clear(); CmbSection.IsEnabled = false; }

        OnHeaderChanged();
    }

    private void OnHeaderChanged()
    {
        if (_isLoading) return;
        UpdateTitleFromHeader();
    }

    private void UpdateTitleFromHeader()
    {
        if (!_titleFollowsHeader) return;

        var title = BuildTitle();

        // TextChanged 가 되돌아와 "사용자가 고쳤다" 로 오해하지 않게 잠근다.
        _isLoading = true;
        TxtTitle.Text = title;
        _isLoading = false;
    }

    private void OnTitleEdited(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        _titleFollowsHeader = false;
        UpdateHint();
    }

    private string BuildTitle() => LessonJournalTitle.Build(
        DpDate.Date?.DateTime,
        CmbPeriod.SelectedItem is ComboBoxItem { Tag: int period } ? period : 0,
        (CmbCourse.SelectedItem as Course)?.Subject,
        RoomText);

    /// <summary>
    /// 강의실 칸에 <b>보이는</b> 값. 편집 가능한 ComboBox 라 고른 항목과 적어 넣은 글이 갈릴 수 있는데,
    /// 제목에는 사용자가 보고 있는 쪽이 들어가야 한다.
    /// </summary>
    private string RoomText =>
        string.IsNullOrWhiteSpace(CmbRoom.Text)
            ? (CmbRoom.SelectedItem as string ?? string.Empty)
            : CmbRoom.Text;

    /// <summary>
    /// 강의실을 채운다. 담당 교과의 강의실 목록에 있으면 그 항목을 고르고,
    /// 없으면 고른 항목을 비운 뒤 직접 적은 값으로 둔다 — 둘이 어긋나면 제목이 화면과 달라진다.
    /// </summary>
    private void SetRoom(string? room)
    {
        if (string.IsNullOrWhiteSpace(room)) return;

        var match = (CmbRoom.ItemsSource as List<string>)?
            .FirstOrDefault(r => string.Equals(r, room, StringComparison.Ordinal));

        if (match != null)
        {
            CmbRoom.SelectedItem = match;
        }
        else
        {
            CmbRoom.SelectedItem = null;
            CmbRoom.Text = room;
        }
    }

    private CourseSection? SelectedSection =>
        CmbSection.SelectedItem is ComboBoxItem { Tag: int no }
            ? _sections.FirstOrDefault(s => s.No == no)
            : null;

    /// <summary>본문 첫 줄: "1-1-1 덧셈과 뺄셈의 혼합 계산 (p.8~11)"</summary>
    private string BuildFirstLine()
    {
        var s = SelectedSection;
        if (s == null) return string.Empty;

        var line = $"{s.FullPath} {s.SectionName}";
        var pages = s.PageRangeDisplay;   // "p.8~12" 또는 ""
        return string.IsNullOrEmpty(pages) ? line : $"{line} ({pages})";
    }

    /// <summary>
    /// 단원을 고르면 본문 첫 줄에 넣는다 — <b>본문이 비어 있을 때만</b>.
    /// 이미 쓴 본문에 끼워 넣으면 커서 위치에 따라 엉뚱한 자리에 들어가므로 손대지 않는다.
    /// </summary>
    private void OnSectionChanged()
    {
        if (_isLoading) return;

        var line = BuildFirstLine();

        if (line.Length > 0 && string.IsNullOrWhiteSpace(Editor.PlainText))
            Editor.InsertHtml($"<p>{WebUtility.HtmlEncode(line)}</p><p></p>");

        UpdateHint();
    }

    private void UpdateHint()
    {
        if (!_titleFollowsHeader)
        {
            TxtHint.Text = "제목을 직접 고쳤습니다 — 머리 정보를 바꿔도 제목은 그대로 둡니다.";
            return;
        }

        TxtHint.Text = SelectedSection != null && !string.IsNullOrWhiteSpace(Editor.PlainText)
            ? "단원은 본문이 비어 있을 때만 첫 줄에 들어갑니다."
            : string.Empty;
    }

    #endregion

    #region 저장

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // 제목이 비었으면 머리 정보로 한 번 더 만들어 본다(자동 추적을 꺼 둔 채 비운 경우).
        var title = string.IsNullOrWhiteSpace(TxtTitle.Text) ? BuildTitle() : TxtTitle.Text.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            await MessageBox.ShowErrorAsync("제목이 필요합니다. 날짜·교시·교과 중 하나 이상을 고르거나 제목을 직접 적어 주세요.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Editor.PlainText))
        {
            await MessageBox.ShowErrorAsync("본문을 적어 주세요.");
            return;
        }

        BtnSave.IsEnabled = false;

        try
        {
            // 이 창은 카테고리를 늘 '수업' 으로 고정하므로 실제로 바뀌는 일은 거의 없다.
            // 그래도 규칙은 하나로 둔다 — 카테고리를 대입하는 화면은 첨부도 함께 옮긴다.
            string oldCategory = _isNew ? string.Empty : (_post.Category ?? string.Empty);

            _post.Category = LessonJournalComposer.Category;
            _post.Subject = LessonJournalComposer.Subject;
            _post.Title = title;
            _post.Content = Editor.GetFlowBytes();
            _post.PlainText = Editor.PlainText;

            // 작성일시는 새 글일 때만 찍는다 — 고칠 때마다 밀면 '언제 쓴 일지'인지가 사라진다.
            if (_isNew) _post.DateTime = DateTime.Now;

            using var service = NewSchool.Board.Board.CreateCachedService();

            int postNo = await service.SavePostAsync(_post);
            if (postNo <= 0)
            {
                // 실패인데 창을 닫으면 쓴 내용이 조용히 날아간다. 열어 둔 채 알린다.
                await MessageBox.ShowErrorAsync("수업 일지 저장에 실패했습니다.");
                return;
            }

            await PostAttachments.MoveAllToCategoryAsync(service, postNo, oldCategory, _post.Category);

            _post.HasFile = await PostAttachments.ApplyAsync(service, FileList, postNo, _post.Category);

            SavedPostNo = postNo;
            Result = true;
            _dialogResult.TrySetResult(true);
            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonJournalWindow] 저장 실패: {ex.Message}");
            await MessageBox.ShowErrorAsync($"수업 일지 저장 중 오류가 발생했습니다.\n{ex.Message}", ex);
        }
        finally
        {
            BtnSave.IsEnabled = true;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _dialogResult.TrySetResult(false);
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // 제목표시줄 X 로 닫은 경우도 취소다(저장 경로에서 이미 결과를 넣었으면 무시된다).
        _dialogResult.TrySetResult(false);
        Editor?.Dispose();
    }

    #endregion
}
