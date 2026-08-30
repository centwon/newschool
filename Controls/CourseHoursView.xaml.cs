using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Helpers;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;

namespace NewSchool.Controls;

/// <summary>
/// 시수 관리 — 학기의 주차별 수업 가능 시수를 <b>학급(강의실)별 열</b>로 펼쳐 보여주고,
/// 칸마다 손으로 고칠 수 있게 한다.
///
/// 자동값은 시간표 배치(<see cref="Lesson"/>)와 학사일정으로 매번 계산한다.
/// 저장하는 것은 <b>고친 칸</b>뿐이다 — 시간표나 학사일정이 바뀌면 저장해 둔 자동값은
/// 그 순간 거짓이 되기 때문이다. 자동값과 같은 값을 넣으면 조정을 지운다.
///
/// 예전 시수 관리 탭은 연간 수업 계획(SubjectYearPlan)에 매달려 있어서 계획을 다 세우기
/// 전에는 숫자가 하나도 안 나왔다. 지금은 배치와 학사일정만 있으면 바로 계산된다.
/// </summary>
public sealed partial class CourseHoursView : UserControl
{
    private Course? _selectedCourse;
    private List<SchoolSchedule> _schedules = [];
    private List<Lesson> _lessons = [];
    private List<string> _rooms = [];
    private List<WeeklyHoursWeek> _weeks = [];
    private Dictionary<(string Room, int Week), CourseWeeklyHours> _adjustments = [];

    private int _year;
    private int _semester;
    private int _sectionHours;

    /// <summary>학교의 학년 수 (0 = 모름 → 학사일정 판정이 종전 기준으로 돈다)</summary>
    private int _gradeCount;

    /// <summary>학사일정에서 유추한 학기 기간</summary>
    private SemesterRange _range;

    /// <summary>합계 줄 다시 그리기용 — (학급 → 합계 TextBlock)</summary>
    private readonly Dictionary<string, TextBlock> _roomTotalCells = [];

    public CourseHoursView()
    {
        this.InitializeComponent();
    }

    #region 로드

    /// <summary>
    /// 학년도·학기와 대상 수업을 받는다.
    /// </summary>
    public async Task LoadAsync(int year, int semester, Course? course)
    {
        _year = year;
        _semester = semester;

        // 학사일정은 탭을 열 때마다 다시 읽는다. 기간을 학사일정으로 좁히므로 기간보다 먼저.
        //
        // ⚠ 예전에는 "학년도가 그대로면 다시 읽지 않는다"고 한 번만 읽었다. 그런데 학사일정은
        //    [설정 → 학사일정] 에서 <b>앱을 켜 둔 채로</b> 내려받는다 — 그러면 이 탭만 옛 목록을
        //    들고 그 세션 내내 버텼다. 겨울방학을 방금 받아 왔는데도 방학 주에 수업일이
        //    5일로 잡히고, 주차도 방학까지 그대로 늘어서 있는 식이다.
        //    154행짜리 조회 한 번이고, 탭이 낡았을 때만 오므로 캐시할 값이 아니었다.
        await LoadSchedulesAsync();

        // 기간도 방금 읽은 학사일정으로 다시 유추한다. 학년도·학기가 그대로여도 학사일정이
        // 바뀌면 방학 경계가 움직인다 — 옛 기간을 들고 있으면 위에서 새로 읽은 보람이 없다.
        _range = WeeklyHoursCalculator.ResolveSemesterRange(_year, _semester, _schedules);

        _selectedCourse = course;
        BtnClearAdjustments.IsEnabled = course != null;

        await LoadSectionHoursAsync(course?.No ?? 0);
        await RebuildAsync();
    }

    /// <summary>시간표 배치가 바뀌었다 — 자동값을 다시 계산한다.</summary>
    public Task RefreshAsync() => RebuildAsync();

    private async Task LoadSchedulesAsync()
    {
        _schedules = [];

        var schoolCode = Settings.SchoolCode.Value;
        if (string.IsNullOrEmpty(schoolCode) || _year == 0) return;

        try
        {
            using var repo = new SchoolScheduleRepository(SchoolDatabase.DbPath);
            _schedules = await repo.GetBySchoolYearAsync(schoolCode, _year);
        }
        catch (Exception ex)
        {
            // 학사일정이 없으면 휴업일을 빼지 못할 뿐 계산 자체는 된다. 다만 조용히 넘어가면
            // "왜 방학 주에도 시수가 잡히지" 를 알 길이 없어서 알려 준다.
            Debug.WriteLine($"[CourseHoursView] 학사일정 로드 실패: {ex.Message}");
            ShowWarning($"학사일정을 불러오지 못해 휴업일을 빼지 못했습니다.\n{ex.Message}");
        }
    }

    private async Task LoadSectionHoursAsync(int courseNo)
    {
        _sectionHours = 0;
        if (courseNo <= 0) return;

        try
        {
            using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
            var sections = await repo.GetByCourseAsync(courseNo);
            _sectionHours = sections.Sum(s => s.EstimatedHours);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseHoursView] 단원 차시 합계 실패: {ex.Message}");
        }
    }

    #endregion

    #region 계산 · 표 그리기

    private async Task RebuildAsync()
    {
        _lessons = [];
        _rooms = [];
        _weeks = [];
        _adjustments = [];

        if (_selectedCourse == null)
        {
            ShowEmpty("수업을 먼저 선택하세요", "위쪽 필터의 [수업] 에서 시수를 볼 수업을 고르세요");
            UpdateCards();
            return;
        }

        var start = _range.Start;
        var end = _range.End;

        try
        {
            using (var lessonRepo = new LessonRepository(SchoolDatabase.DbPath))
            {
                // IsRecurring·IsCancelled 로 거르던 조건은 그 열들과 함께 없앴다 —
                // 둘 다 늘 기본값이라 한 줄도 걸러 내지 않던 필터다.
                _lessons = await lessonRepo.GetByCourseAsync(_selectedCourse.No);
            }

            using (var hoursRepo = new CourseWeeklyHoursRepository(SchoolDatabase.DbPath))
            {
                _adjustments = await hoursRepo.GetByCourseAsync(_selectedCourse.No);
            }

            // 학년 수를 알아야 "1·2학년만 수련회" 같은 날을 그 학년 수업일에서 뺄 수 있다.
            _gradeCount = await SchoolProfile.GetGradeCountAsync();

            _rooms = WeeklyHoursCalculator.ResolveRooms(_selectedCourse, _lessons);
            _weeks = WeeklyHoursCalculator.Calculate(
                _selectedCourse, _lessons, _schedules, start, end, _gradeCount);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseHoursView] 주차별 시수 계산 실패: {ex.Message}");
            ShowWarning($"주차별 시수를 계산하지 못했습니다.\n{ex.Message}");
        }

        UpdateCards();

        if (_rooms.Count == 0)
        {
            ShowEmpty("아직 배치된 시간표가 없습니다",
                "[수업 시간표 입력] 탭에서 이 수업을 주간 배치판에 놓으면, 학급별 주차 시수가 여기에 만들어집니다.");
            return;
        }

        if (_weeks.Count == 0)
        {
            ShowEmpty("학기 기간에 수업할 주가 없습니다",
                "학사일정을 내려받으면 학기 기간을 제대로 잡습니다 (설정 → 학사일정).");
            return;
        }

        BuildTable();
        UpdateSummary();
    }

    private void ShowEmpty(string title, string hint)
    {
        HoursGrid.Children.Clear();
        HoursGrid.RowDefinitions.Clear();
        HoursGrid.ColumnDefinitions.Clear();
        _roomTotalCells.Clear();

        TxtHoursEmpty.Text = title;
        TxtHoursEmptyHint.Text = hint;
        HoursEmptyState.Visibility = Visibility.Visible;
        HoursScroll.Visibility = Visibility.Collapsed;
        TxtHoursSummary.Text = "";
    }

    private Style CellStyle(string key) => (Style)Resources[key];

    /// <summary>
    /// 열: 주차 · 기간 · 일수 · [학급들] · 합계 · 비고
    /// </summary>
    private void BuildTable()
    {
        HoursEmptyState.Visibility = Visibility.Collapsed;
        HoursScroll.Visibility = Visibility.Visible;

        HoursGrid.Children.Clear();
        HoursGrid.RowDefinitions.Clear();
        HoursGrid.ColumnDefinitions.Clear();
        _roomTotalCells.Clear();

        HoursGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });   // 주차
        HoursGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });  // 기간
        HoursGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });   // 일수

        int roomStart = 3;
        foreach (var _ in _rooms)
            HoursGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

        int eventsCol = roomStart + _rooms.Count;
        HoursGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 }); // 비고

        // 머리 + 주차들 + 합계
        HoursGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in _weeks)
            HoursGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        HoursGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddHeader(0, 0, "주차");
        AddHeader(0, 1, "기간");
        AddHeader(0, 2, "일수", center: true);
        for (int i = 0; i < _rooms.Count; i++)
            AddHeader(0, roomStart + i, _rooms[i], center: true);
        AddHeader(0, eventsCol, "비고 (학사일정)");

        for (int i = 0; i < _weeks.Count; i++)
        {
            var week = _weeks[i];
            int row = i + 1;

            AddText(row, 0, week.WeekDisplay, "HoursTextStyle");
            AddText(row, 1, week.PeriodDisplay, "HoursMutedTextStyle");
            AddText(row, 2, week.TeachingDays.ToString(), "HoursMutedTextStyle", center: true);

            for (int c = 0; c < _rooms.Count; c++)
                AddInput(row, roomStart + c, week, _rooms[c]);

            AddText(row, eventsCol, week.EventsDisplay, "HoursMutedTextStyle", tooltip: week.EventsDisplay);
        }

        BuildTotalRow(roomStart, eventsCol, _weeks.Count + 1);
    }

    /// <summary>
    /// 맨 아래 합계 줄 — 학급마다 학기 전체 시수를 더한다.
    /// 주차별 합(가로 합계)은 학급이 여럿일 때 "이 주에 내가 몇 시간 들어가나" 밖에 답하지 못해서 뺐다.
    /// 정작 필요한 숫자는 "이 학급을 학기 동안 몇 시간 만나나" 다.
    /// </summary>
    private void BuildTotalRow(int roomStart, int eventsCol, int row)
    {
        AddHeader(row, 0, "합계");
        AddHeader(row, 1, $"{_weeks.Count}주");
        AddHeader(row, 2, _weeks.Sum(w => w.TeachingDays).ToString(), center: true);

        foreach (var room in _rooms)
        {
            var block = new TextBlock
            {
                Text = TotalForRoom(room).ToString(),
                Style = CellStyle("HoursTotalTextStyle")
            };

            _roomTotalCells[room] = block;
            AddCell(row, roomStart + _rooms.IndexOf(room), block, header: true);
        }

        AddHeader(row, eventsCol, "");
    }

    private void AddCell(int row, int column, FrameworkElement content, bool header = false)
    {
        var border = new Border
        {
            Style = CellStyle(header ? "HoursHeaderCellStyle" : "HoursCellStyle"),
            Child = content
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        HoursGrid.Children.Add(border);
    }

    private void AddHeader(int row, int column, string text, bool center = false)
    {
        var block = new TextBlock { Text = text, Style = CellStyle("HoursHeaderTextStyle") };
        if (center) block.HorizontalAlignment = HorizontalAlignment.Center;

        AddCell(row, column, block, header: true);
    }

    private void AddText(int row, int column, string text, string styleKey, bool center = false, string? tooltip = null)
    {
        var block = new TextBlock { Text = text, Style = CellStyle(styleKey) };
        if (center) block.HorizontalAlignment = HorizontalAlignment.Center;

        if (!string.IsNullOrEmpty(tooltip))
            ToolTipService.SetToolTip(block, tooltip);

        AddCell(row, column, block);
    }

    /// <summary>
    /// 학급별 시수 입력 칸. 자동값과 다르면 강조해서 "손으로 고친 칸"임을 드러낸다.
    /// </summary>
    private void AddInput(int row, int column, WeeklyHoursWeek week, string room)
    {
        int auto = week.AutoFor(room);
        int effective = EffectiveFor(week, room);

        var box = new TextBox
        {
            Text = effective.ToString(),
            Style = CellStyle("HoursInputStyle"),
            Tag = (week, room)
        };

        ToolTipService.SetToolTip(box, $"{room} · {week.Number}주차 (자동 {auto}시간)");
        ApplyAdjustedLook(box, auto, effective);

        box.LostFocus += OnHoursBoxLostFocus;
        box.KeyDown += OnHoursBoxKeyDown;

        AddCell(row, column, box);
    }

    private void ApplyAdjustedLook(TextBox box, int auto, int effective)
    {
        if (effective == auto)
        {
            box.ClearValue(TextBox.ForegroundProperty);
            box.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        }
        else
        {
            box.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
            box.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }
    }

    #endregion

    #region 편집 · 저장

    private void OnHoursBoxKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;

        // Enter 로도 확정되게 한다 — 표를 훑으며 고칠 때 매번 다른 칸을 눌러 포커스를 옮기는 건 번거롭다.
        if (sender is TextBox box)
        {
            e.Handled = true;
            _ = CommitAsync(box);
        }
    }

    private void OnHoursBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            _ = CommitAsync(box);
    }

    private async Task CommitAsync(TextBox box)
    {
        if (_selectedCourse == null) return;
        if (box.Tag is not ValueTuple<WeeklyHoursWeek, string> tag) return;

        var (week, room) = tag;
        int auto = week.AutoFor(room);
        int current = EffectiveFor(week, room);

        // 숫자가 아니면 되돌린다 — 빈 칸으로 두면 "0시간" 인지 "자동" 인지 알 수 없다.
        if (!int.TryParse(box.Text?.Trim(), out int value) || value < 0 || value > 40)
        {
            box.Text = current.ToString();
            ApplyAdjustedLook(box, auto, current);
            return;
        }

        if (value == current) return;

        try
        {
            using var repo = new CourseWeeklyHoursRepository(SchoolDatabase.DbPath);

            if (value == auto)
            {
                // 자동값과 같아지면 조정을 남길 이유가 없다. 남겨 두면 시간표가 바뀌어도
                // 옛 숫자가 고정으로 버틴다.
                await repo.DeleteAsync(_selectedCourse.No, room, week.Number);
                _adjustments.Remove((room, week.Number));
            }
            else
            {
                var adjustment = new CourseWeeklyHours
                {
                    CourseNo = _selectedCourse.No,
                    Room = room,
                    Week = week.Number,
                    WeekStart = week.StartDate,
                    PlannedHours = value
                };

                await repo.UpsertAsync(adjustment);
                _adjustments[(room, week.Number)] = adjustment;
            }

            box.Text = value.ToString();
            ApplyAdjustedLook(box, auto, value);

            if (_roomTotalCells.TryGetValue(room, out var totalBlock))
                totalBlock.Text = TotalForRoom(room).ToString();

            UpdateCards();
            UpdateSummary();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseHoursView] 시수 저장 실패: {ex.Message}");
            ShowWarning($"시수를 저장하지 못했습니다.\n{ex.Message}");

            box.Text = current.ToString();
            ApplyAdjustedLook(box, auto, current);
        }
    }

    private async void OnClearAdjustmentsClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null) return;

        if (_adjustments.Count == 0)
        {
            ShowWarning("손으로 고친 칸이 없습니다.");
            return;
        }

        if (!await MessageBox.ShowConfirmAsync(
                $"'{_selectedCourse.Subject}' 에서 손으로 고친 {_adjustments.Count}칸을 모두 자동값으로 되돌립니다.",
                "조정 지우기", "되돌리기", "취소"))
            return;

        try
        {
            using var repo = new CourseWeeklyHoursRepository(SchoolDatabase.DbPath);
            await repo.DeleteByCourseAsync(_selectedCourse.No);

            await RebuildAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseHoursView] 조정 전체 삭제 실패: {ex.Message}");
            ShowWarning($"조정을 지우지 못했습니다.\n{ex.Message}");
        }
    }

    #endregion

    #region 카드 · 요약

    private void UpdateCards()
    {
        RoomChips.Children.Clear();

        if (_selectedCourse == null)
        {
            TxtPlacementSummary.Text = "수업을 선택하세요";
            TxtScheduleSummary.Text = "";
            TxtSemesterRange.Text = "";
            TxtStatWeeks.Text = "0주";
            TxtStatDays.Text = "0일";
            TxtStatHours.Text = "0";
            TxtStatSectionHours.Text = "0";
            return;
        }

        // 시간표 정보 — 주당 몇 시간을, 몇 학급에 배치했는가
        int placed = _lessons.Count;
        TxtPlacementSummary.Text = _rooms.Count == 0
            ? $"배치 없음 (주당 시수 {_selectedCourse.Unit}시간)"
            : $"주당 {placed}시간 ({_rooms.Count}개 학급) · 수업 개설의 주당 시수 {_selectedCourse.Unit}시간";

        foreach (var room in _rooms)
        {
            var slots = _lessons
                .Where(l => (string.IsNullOrWhiteSpace(l.Room) ? WeeklyHoursCalculator.UnassignedRoom : l.Room) == room)
                .OrderBy(l => l.DayOfWeek).ThenBy(l => l.Period)
                .Select(l => $"{l.DayName}{l.Period}");

            RoomChips.Children.Add(new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = $"{room}: {string.Join(", ", slots)}",
                    FontSize = 12
                }
            });
        }

        // 학사일정
        int holidays = _schedules.Count(s => !s.IsDeleted && Helpers.SchoolCalendar.IsNonTeachingDay(s));
        TxtScheduleSummary.Text = _schedules.Count == 0
            ? "학사일정이 없습니다 (설정 → 학사일정에서 NEIS 자료를 내려받으면 휴업일을 빼고 계산합니다)"
            : $"{_schedules.Count(s => !s.IsDeleted)}개 일정 · 휴업일·공휴일 {holidays}일";

        // 유추한 기간인지 관례값인지 드러낸다 — 관례값이면 방학이 섞여 숫자를 믿으면 안 된다.
        TxtSemesterRange.Text = _range.FromSchedule
            ? $"학기 {_range.Display}"
            : $"학기 {_range.Display} — 관례값입니다. 학사일정을 내려받으면 방학을 뺀 기간으로 잡힙니다.";

        TxtSemesterRange.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            _range.FromSchedule ? "TextFillColorSecondaryBrush" : "SystemFillColorCautionBrush"];

        // 통계
        TxtStatWeeks.Text = $"{_weeks.Count}주";
        TxtStatDays.Text = $"{_weeks.Sum(w => w.TeachingDays)}일";
        TxtStatHours.Text = _weeks.Sum(TotalFor).ToString();
        TxtStatSectionHours.Text = _sectionHours.ToString();
    }

    private void UpdateSummary()
    {
        if (_selectedCourse == null || _weeks.Count == 0)
        {
            TxtHoursSummary.Text = "";
            return;
        }

        int autoTotal = _weeks.Sum(w => w.AutoTotal);
        int effectiveTotal = _weeks.Sum(TotalFor);

        var text = $"자동 {autoTotal}시간 · 조정 반영 {effectiveTotal}시간";

        if (_adjustments.Count > 0)
            text += $" (손으로 고친 칸 {_adjustments.Count}개)";

        if (_sectionHours > 0)
        {
            int perRoom = _rooms.Count > 0 ? effectiveTotal / _rooms.Count : effectiveTotal;
            int diff = perRoom - _sectionHours;
            var state = diff switch
            {
                0 => "딱 맞음",
                > 0 => $"{diff}시간 여유",
                _ => $"{-diff}시간 부족"
            };

            text += $" · 학급당 {perRoom}시간, 단원 전체 {_sectionHours}차시 대비 {state}";
        }

        TxtHoursSummary.Text = text;
    }

    #endregion

    #region 이벤트 · Helper

    private int EffectiveFor(WeeklyHoursWeek week, string room)
        => _adjustments.TryGetValue((room, week.Number), out var adjustment)
            ? adjustment.PlannedHours
            : week.AutoFor(room);

    /// <summary>그 주의 모든 학급 합 — 통계 카드와 요약 줄에서 쓴다</summary>
    private int TotalFor(WeeklyHoursWeek week)
        => _rooms.Sum(room => EffectiveFor(week, room));

    /// <summary>그 학급의 학기 전체 시수 — 합계 줄에서 쓴다</summary>
    private int TotalForRoom(string room)
        => _weeks.Sum(week => EffectiveFor(week, room));

    private void ShowWarning(string message)
    {
        HoursInfoBar.Message = message;
        HoursInfoBar.IsOpen = true;
    }

    #endregion
}
