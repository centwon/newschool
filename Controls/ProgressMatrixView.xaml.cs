using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NewSchool.Dialogs;
using NewSchool.Models;
using NewSchool.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace NewSchool.Controls;

/// <summary>
/// 진도 관리 — 단원 × 학급(강의실) 표. 칸마다 날짜 하나를 보여 준다:
/// 완료한 단원은 <b>초록 칸에 완료한 날</b>, 아직인 단원은 <b>회색 글자로 예정일</b>.
///
/// <para><b>마지막 표시 기준</b>(<see cref="ProgressPlanner"/>). 학급마다 완료로 표시한 단원 가운데
/// 가장 뒤의 것까지는 모두 끝난 것으로 보고(표시를 놓친 앞 단원은 날짜 없이 ✓), 그 뒤 단원은 그
/// 완료 다음 수업부터 예상 차시만큼 실제 수업 칸을 소모해 예정일을 다시 잡는다. 학급 머리에는
/// 남은 시간과 남은 차시를 둔다 — 교사에게 필요한 것은 "학기 안에 끝나나" 다.</para>
///
/// <para>예전에는 칸마다 보강·병합·건너뜀·결강을 따로 적었다. 결강·보강은 단원이 아니라 <b>시간</b>의
/// 문제라 시간표 변경·시수 조정이 맡고, 병합은 뒤 단원만 완료로 표시하면 같은 결과가 된다.
/// 그래서 칸은 완료했나와 그 날짜만 담는다. 예전 기록 가운데 보강·병합·건너뜀은 이미 완료로
/// 저장돼 있어(<c>IsCompleted</c>) 그대로 완료로 읽히고, 결강은 미완료로 읽힌다.</para>
/// </summary>
public sealed partial class ProgressMatrixView : UserControl
{
    private Course? _selectedCourse;
    private CourseProgressPlan? _plan;

    private readonly Dictionary<(int SectionNo, string Room), Border> _cellBorders = [];

    // 완료 색은 의미색이라 테마 리소스에 대응이 없다. 반투명(알파 96)이라
    // 밝은 테마·어두운 테마 어디서도 글자를 가리지 않는다.
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush CompletedBg = new(ColorHelper.FromArgb(96, 76, 175, 80));

    private static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

    public ProgressMatrixView()
    {
        this.InitializeComponent();
        LegendDone.Background = CompletedBg;
        LegendOverdue.Background = ThemeBrush("SubtleFillColorSecondaryBrush");
        UpdateEmptyState();
    }

    #region 로드

    /// <summary>
    /// 대상 수업을 바꾼다.
    /// </summary>
    public async Task LoadAsync(Course? course)
    {
        _selectedCourse = course;

        TxtMatrixTitle.Text = course == null ? "진도" : $"진도 — {course.DisplayName}";
        BtnExport.IsEnabled = course != null;

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _plan = null;

        if (_selectedCourse != null)
        {
            try
            {
                _plan = await CourseProgressPlan.LoadAsync(_selectedCourse, DateTime.Today);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProgressMatrixView] 진도 로드 실패: {ex.Message}");
                ShowWarning($"진도를 불러오지 못했습니다.\n{ex.Message}");
            }
        }

        BuildMatrix();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    #endregion

    #region 표 그리기

    private void BuildMatrix()
    {
        MatrixGrid.Children.Clear();
        MatrixGrid.RowDefinitions.Clear();
        MatrixGrid.ColumnDefinitions.Clear();
        _cellBorders.Clear();

        var plan = _plan;
        if (plan == null || plan.Sections.Count == 0 || plan.Rooms.Count == 0)
        {
            UpdateEmptyState();
            UpdateSummary();
            return;
        }

        MatrixEmptyState.Visibility = Visibility.Collapsed;
        MatrixScroll.Visibility = Visibility.Visible;

        // 열: 연번 · 단원명 · 차시 · 학급들
        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

        const int roomStartCol = 3;
        foreach (var _ in plan.Rooms)
            MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });   // "36시간 · 12차시" 가 잘리지 않게

        MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in plan.Sections)
            MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

        AddHeaderCell(0, 0, "#");
        AddHeaderCell(0, 1, "단원");
        AddHeaderCell(0, 2, "차시");
        for (int i = 0; i < plan.Rooms.Count; i++)
            AddRoomHeader(0, roomStartCol + i, plan.Rooms[i], plan.Forecasts.GetValueOrDefault(plan.Rooms[i]));

        for (int row = 0; row < plan.Sections.Count; row++)
        {
            var section = plan.Sections[row];
            int gridRow = row + 1;

            AddDataCell(gridRow, 0, (row + 1).ToString());
            AddDataCell(gridRow, 1, $"{section.FullPath} {section.SectionName}", section.ShortInfo);
            AddDataCell(gridRow, 2, section.EstimatedHours.ToString());

            for (int col = 0; col < plan.Rooms.Count; col++)
            {
                var room = plan.Rooms[col];
                var cell = plan.Forecasts.GetValueOrDefault(room)?.Sections.GetValueOrDefault(section.No);
                AddProgressCell(gridRow, roomStartCol + col, section, room, cell);
            }
        }

        UpdateSummary();
    }

    private Style CellStyle(string key) => (Style)Resources[key];

    private void AddHeaderCell(int row, int col, string text)
    {
        var border = new Border
        {
            Style = CellStyle("MatrixHeaderCellStyle"),
            Child = new TextBlock { Text = text, Style = CellStyle("MatrixHeaderTextStyle") }
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        MatrixGrid.Children.Add(border);
    }

    /// <summary>학급 머리 — 이름, "남은 N시간 · M차시", 여유·모자람.</summary>
    private void AddRoomHeader(int row, int col, string room, RoomForecast? forecast)
    {
        var panel = new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = room, Style = CellStyle("MatrixHeaderTextStyle") });

        if (forecast != null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"{forecast.RemainingHours}시간 · {forecast.RemainingUnits}차시",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            int balance = forecast.Balance;
            panel.Children.Add(new TextBlock
            {
                Text = balance switch
                {
                    0 => "딱 맞음",
                    > 0 => $"{balance}시간 여유",
                    _ => $"{-balance}시간 모자람"
                },
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = ThemeBrush(balance < 0 ? "SystemFillColorCriticalBrush" : "SystemFillColorSuccessBrush")
            });
        }

        var border = new Border { Style = CellStyle("MatrixHeaderCellStyle"), Child = panel };
        ToolTipService.SetToolTip(border,
            forecast == null
                ? room
                : $"{room} — 마지막으로 완료한 단원 뒤로 남은 수업 {forecast.RemainingHours}시간, 남은 단원 {forecast.RemainingUnits}차시");

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        MatrixGrid.Children.Add(border);
    }

    private void AddDataCell(int row, int col, string text, string? tooltip = null)
    {
        var border = new Border
        {
            Style = CellStyle("MatrixDataCellStyle"),
            Child = new TextBlock { Text = text, Style = CellStyle("MatrixDataTextStyle") }
        };

        if (!string.IsNullOrEmpty(tooltip))
            ToolTipService.SetToolTip(border, tooltip);

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        MatrixGrid.Children.Add(border);
    }

    private void AddProgressCell(int row, int col, CourseSection section, string room, SectionForecast? cell)
    {
        var text = new TextBlock
        {
            Text = CellText(cell),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var border = new Border
        {
            Style = CellStyle("MatrixDataCellStyle"),
            Background = TransparentBrush,
            Tag = (section.No, room),
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            Child = text
        };

        switch (cell?.Kind)
        {
            case SectionForecastKind.Done:
                border.Background = CompletedBg;
                if (cell.IsBaseline)
                {
                    // 마지막으로 표시한 단원 — 여기서부터 예정을 다시 센다
                    border.BorderBrush = ThemeBrush("SystemFillColorSuccessBrush");
                    border.BorderThickness = new Thickness(0, 0, 1, 3);
                    text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                }
                break;

            case SectionForecastKind.Planned when cell.IsOverdue:
                border.Background = ThemeBrush("SubtleFillColorSecondaryBrush");
                text.Foreground = ThemeBrush("TextFillColorSecondaryBrush");
                break;

            case SectionForecastKind.Planned:
                text.Foreground = ThemeBrush("TextFillColorTertiaryBrush");
                break;
        }

        ToolTipService.SetToolTip(border, CellTooltip(cell));
        SetCellName(border, section, room, cell);

        border.Tapped += OnCellTapped;
        border.KeyDown += OnCellKeyDown;
        border.ContextRequested += OnCellContextRequested;

        _cellBorders[(section.No, room)] = border;

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        MatrixGrid.Children.Add(border);
    }

    private static void SetCellName(Border border, CourseSection section, string room, SectionForecast? cell)
        => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(border,
            $"{room} {section.SectionName}: {CellTooltip(cell) ?? "예정 없음"}");

    private static string CellText(SectionForecast? cell) => cell?.Kind switch
    {
        SectionForecastKind.Done when cell.Date is { } d => $"{d:M/d}",
        SectionForecastKind.Done => "✓",
        SectionForecastKind.Planned when cell.Date is { } d => $"{d:M/d}",
        _ => ""
    };

    private static string? CellTooltip(SectionForecast? cell) => cell?.Kind switch
    {
        SectionForecastKind.Done when cell.Date is { } d =>
            $"완료 {d:M/d}" + (cell.Period > 0 ? $" {cell.Period}교시" : "") + (cell.IsBaseline ? " — 마지막으로 표시한 단원" : ""),
        SectionForecastKind.Done => "뒤 단원을 완료로 표시해 끝난 것으로 봅니다",
        SectionForecastKind.Planned when cell.IsOverdue => $"예정 {cell.Date:M/d} — 지났습니다(표시를 놓쳤거나 늦어지는 중)",
        SectionForecastKind.Planned => $"예정 {cell.Date:M/d}",
        SectionForecastKind.Unscheduled => "남은 수업 시간으로는 학기 안에 닿지 않습니다",
        _ => null
    };

    private void UpdateEmptyState()
    {
        bool ready = _plan != null && _plan.Sections.Count > 0 && _plan.Rooms.Count > 0;

        MatrixEmptyState.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        MatrixScroll.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;

        if (_selectedCourse == null)
        {
            TxtMatrixEmpty.Text = "수업을 먼저 선택하세요";
            TxtMatrixEmptyHint.Text = "위쪽 필터의 [수업] 에서 진도를 볼 수업을 고르세요";
        }
        else if (_plan != null && _plan.Rooms.Count == 0)
        {
            TxtMatrixEmpty.Text = "강의실이 없습니다";
            TxtMatrixEmptyHint.Text = "진도는 학급(강의실)별로 따로 기록합니다. [수업 개설] 탭에서 이 수업의 강의실을 먼저 넣어 주세요.";
        }
        else
        {
            TxtMatrixEmpty.Text = "등록된 단원이 없습니다";
            TxtMatrixEmptyHint.Text = "[단원 관리] 탭에서 단원을 먼저 만들면 여기에 진도표가 생깁니다.";
        }
    }

    #endregion

    #region 칸 메뉴

    private async void OnCellTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Border border) await ShowCellMenuAsync(border, null);
    }

    private async void OnCellKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Space or VirtualKey.Enter)) return;
        e.Handled = true;
        if (sender is Border border) await ShowCellMenuAsync(border, null);
    }

    private async void OnCellContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (sender is Border border)
            await ShowCellMenuAsync(border, e.TryGetPosition(border, out var p) ? p : null);
    }

    /// <summary>
    /// 칸 메뉴: 완료로 표시(그 학급의 지난 수업 칸에서 고름) · 완료 취소 · 수업 일지 쓰기 · 이 단원의 수업 일지.
    /// </summary>
    private async Task ShowCellMenuAsync(Border border, Windows.Foundation.Point? at)
    {
        var plan = _plan;
        if (plan == null || border.Tag is not ValueTuple<int, string> tag) return;

        var (sectionNo, room) = tag;
        var section = plan.Sections.FirstOrDefault(s => s.No == sectionNo);
        if (section == null) return;

        var cell = plan.Forecasts.GetValueOrDefault(room)?.Sections.GetValueOrDefault(sectionNo);
        bool recorded = cell?.Kind == SectionForecastKind.Done && cell.IsRecorded;

        var menu = new MenuFlyout();

        // ① 완료로 표시 / 날짜 바꾸기
        var mark = new MenuFlyoutSubItem
        {
            Text = recorded ? "완료 날짜 바꾸기" : "완료로 표시",
            Icon = new FontIcon { Glyph = "" }
        };

        var past = plan.PastSlots(room, DateTime.Today);
        for (int i = 0; i < past.Count; i++)
        {
            var slot = past[i];
            var item = new MenuFlyoutItem { Text = i == 0 ? $"{slot.Display} · 최근 수업" : slot.Display };
            item.Click += async (_, _) => await MarkAsync(section, room, slot.Date, slot.Period);
            mark.Items.Add(item);
        }

        if (past.Count > 0) mark.Items.Add(new MenuFlyoutSeparator());
        var other = new MenuFlyoutItem { Text = "다른 날…" };
        other.Click += async (_, _) => await MarkOnPickedDateAsync(section, room);
        mark.Items.Add(other);
        menu.Items.Add(mark);

        // ② 완료 취소 — 기록이 있는 칸만. 앞 단원은 기록이 없으니 취소할 것도 없다.
        if (recorded)
        {
            var undo = new MenuFlyoutItem { Text = "완료 취소", Icon = new FontIcon { Glyph = "" } };
            undo.Click += async (_, _) => await UnmarkAsync(section, room);
            menu.Items.Add(undo);
        }

        menu.Items.Add(new MenuFlyoutSeparator());

        // ③ 수업 일지 쓰기 — 그 학급의 가장 최근 수업 칸으로
        var write = new MenuFlyoutItem { Text = "수업 일지 쓰기", Icon = new FontIcon { Glyph = "" } };
        write.Click += async (_, _) =>
        {
            var last = past.Count > 0 ? past[0] : new LessonSlot(DateTime.Today, 0, room);
            var seed = new LessonSlotSeed(last.Date, last.Period, plan.Course.No, plan.Course.Subject, room);
            if (await LessonJournalComposer.OpenOrComposeAsync(seed))
                await ReloadAsync();
        };
        menu.Items.Add(write);

        // ④ 이 단원의 수업 일지 — 앞 단원을 완료한 날부터 이 단원을 완료한 날(아직이면 오늘)까지
        var (from, to) = JournalSpan(plan, section, room, cell);
        var journals = await LessonJournalComposer.FindForRoomAsync(plan.Course.Subject, room, from, to);

        var list = new MenuFlyoutSubItem
        {
            Text = $"이 단원의 수업 일지 ({journals.Count})",
            Icon = new FontIcon { Glyph = "" }
        };
        if (journals.Count == 0)
        {
            list.Items.Add(new MenuFlyoutItem { Text = "아직 없습니다", IsEnabled = false });
        }
        foreach (var (date, period, post) in journals)
        {
            var item = new MenuFlyoutItem { Text = period > 0 ? $"{date:M/d} {period}교시" : $"{date:M/d}" };
            int postNo = post.No;
            item.Click += async (_, _) =>
            {
                if (await LessonJournalComposer.OpenPostAsync(postNo))
                    await ReloadAsync();
            };
            list.Items.Add(item);
        }
        menu.Items.Add(list);

        if (at is { } point)
            menu.ShowAt(border, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = point });
        else
            menu.ShowAt(border);
    }

    /// <summary>
    /// "이 단원의 수업 일지" 를 찾을 기간. 시작은 이 단원보다 앞에서 날짜가 기록된 가장 늦은 완료
    /// (없으면 학년도 시작), 끝은 이 단원을 완료한 날(아직이면 오늘).
    /// </summary>
    private static (DateTime From, DateTime To) JournalSpan(
        CourseProgressPlan plan, CourseSection section, string room, SectionForecast? cell)
    {
        var from = new DateTime(Math.Max(1, plan.Course.Year), 3, 1);
        foreach (var s in plan.Sections)
        {
            if (s.No == section.No) break;
            if (plan.Progress.TryGetValue((s.No, room), out var p) && p.IsCompleted && p.CompletedDate is { } d && d.Date > from)
                from = d.Date;
        }

        var to = cell?.Kind == SectionForecastKind.Done && cell.Date is { } done ? done : DateTime.Today;
        if (to < from) to = from;
        return (from, to);
    }

    private async Task MarkAsync(CourseSection section, string room, DateTime date, int period)
    {
        try
        {
            if (!await CourseProgressPlan.MarkCompletedAsync(section.No, room, date, period))
                ShowWarning("완료 표시가 반영되지 않았습니다.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixView] 완료 표시 실패: {ex.Message}");
            ShowWarning($"완료를 표시하지 못했습니다.\n{ex.Message}");
        }

        await ReloadAsync();
    }

    private async Task MarkOnPickedDateAsync(CourseSection section, string room)
    {
        var picker = new CalendarDatePicker
        {
            Date = DateTimeOffset.Now,
            PlaceholderText = "완료한 날",
            MaxDate = DateTimeOffset.Now
        };

        var dialog = new ContentDialog
        {
            Title = $"{room} · {section.SectionName} 완료한 날",
            Content = picker,
            PrimaryButtonText = "완료로 표시",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        if (await MessageBox.ShowDialogAsync(dialog) != ContentDialogResult.Primary || !picker.Date.HasValue)
            return;

        await MarkAsync(section, room, picker.Date.Value.DateTime.Date, period: 0);
    }

    private async Task UnmarkAsync(CourseSection section, string room)
    {
        try
        {
            if (!await CourseProgressPlan.MarkIncompleteAsync(section.No, room))
                ShowWarning("완료 취소가 반영되지 않았습니다.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixView] 완료 취소 실패: {ex.Message}");
            ShowWarning($"완료를 취소하지 못했습니다.\n{ex.Message}");
        }

        await ReloadAsync();
    }

    #endregion

    #region 내보내기

    private async void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (_plan == null || _plan.Sections.Count == 0 || _plan.Rooms.Count == 0)
        {
            ShowWarning("내보낼 진도표가 없습니다.");
            return;
        }

        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            var subject = Helpers.FileNameHelper.Sanitize(_plan.Course.Subject);
            if (subject.Length == 0) subject = "진도";
            picker.SuggestedFileName = $"{subject}_진도현황_{DateTime.Today:yyyyMMdd}";
            picker.FileTypeChoices.Add("CSV 파일", new List<string> { ".csv" });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            await FileIO.WriteTextAsync(file, GenerateCsv(_plan), Windows.Storage.Streams.UnicodeEncoding.Utf8);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixView] CSV 내보내기 실패: {ex.Message}");
            ShowWarning($"CSV 내보내기 중 오류가 났습니다.\n{ex.Message}");
        }
    }

    /// <summary>화면과 같은 규칙으로 쓴다 — 완료(날짜), 완료(앞 단원), 예정 날짜, 빈칸.</summary>
    private static string GenerateCsv(CourseProgressPlan plan)
    {
        var sb = new StringBuilder();
        sb.Append('﻿');

        sb.Append("연번,단원번호,단원명,차시");
        foreach (var room in plan.Rooms)
            sb.Append(',').Append(Escape(room));
        sb.AppendLine();

        for (int i = 0; i < plan.Sections.Count; i++)
        {
            var section = plan.Sections[i];
            sb.Append(i + 1).Append(',')
              .Append(Escape(section.FullPath)).Append(',')
              .Append(Escape(section.SectionName)).Append(',')
              .Append(section.EstimatedHours);

            foreach (var room in plan.Rooms)
            {
                var cell = plan.Forecasts.GetValueOrDefault(room)?.Sections.GetValueOrDefault(section.No);
                sb.Append(',').Append(Escape(cell?.Kind switch
                {
                    SectionForecastKind.Done when cell.Date is { } d => $"완료 {d:M/d}",
                    SectionForecastKind.Done => "완료",
                    SectionForecastKind.Planned => $"예정 {cell.Date:M/d}",
                    _ => ""
                }));
            }

            sb.AppendLine();
        }

        sb.Append(",,남은 시간 · 남은 차시,");
        foreach (var room in plan.Rooms)
        {
            var f = plan.Forecasts.GetValueOrDefault(room);
            sb.Append(',').Append(Escape(f == null ? "" : $"{f.RemainingHours}시간 · {f.RemainingUnits}차시"));
        }
        sb.AppendLine();

        return sb.ToString();

        // 인용 규칙은 한 벌만 둔다 — 손수 짠 사본은 \r 와 엑셀 수식 해석(= + - @) 처리가 빠져 있었다.
        static string Escape(string field) => Services.CsvExportService.Escape(field);
    }

    #endregion

    #region Helper

    private void UpdateSummary()
    {
        var plan = _plan;
        if (plan == null || plan.Sections.Count == 0 || plan.Rooms.Count == 0)
        {
            TxtMatrixSummary.Text = "";
            return;
        }

        int units = plan.Sections.Sum(s => Math.Max(0, s.EstimatedHours));
        var short_ = plan.Rooms.Where(r => plan.Forecasts.GetValueOrDefault(r)?.Balance < 0).ToList();

        var text = $"단원 {plan.Sections.Count}개({units}차시) · 학급 {plan.Rooms.Count}곳";
        text += short_.Count == 0
            ? " · 모든 학급이 학기 안에 끝납니다"
            : $" · 시간이 모자란 학급: {string.Join(", ", short_)}";

        TxtMatrixSummary.Text = text;
    }

    private void ShowWarning(string message)
    {
        MatrixInfoBar.Message = message;
        MatrixInfoBar.IsOpen = true;
    }

    #endregion
}
