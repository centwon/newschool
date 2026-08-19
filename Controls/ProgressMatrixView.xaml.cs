using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NewSchool.Models;
using NewSchool.Repositories;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace NewSchool.Controls;

/// <summary>
/// 진도 관리 — 단원 × 학급(강의실) 매트릭스.
///
/// 1.0 정리에서 통째로 걷어냈다가 되살렸다. 그때 함께 사라진 것들 중
/// <b>일정 동기화</b>(자동배치 <c>Schedule</c> 테이블이 원천이었다)와 <b>엑셀 내보내기</b>
/// (<c>ReportExportService</c>)는 원천 자체가 없어서 되살리지 않았다. 대신 결과를 밖으로
/// 빼는 길은 CSV 로 열어 두었다.
/// </summary>
public sealed partial class ProgressMatrixView : UserControl
{
    private Course? _selectedCourse;
    private List<string> _rooms = [];
    private List<CourseSection> _sections = [];
    private readonly Dictionary<(int SectionId, string Room), LessonProgress> _progress = [];

    private readonly HashSet<(int SectionId, string Room)> _selectedCells = [];
    private readonly Dictionary<(int SectionId, string Room), Border> _cellBorders = [];

    // 진도 유형 색은 의미색이라 테마 리소스에 대응이 없다. 반투명(알파 96)이라
    // 밝은 테마·어두운 테마 어디서도 글자를 가리지 않는다.
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush CompletedBg = new(ColorHelper.FromArgb(96, 76, 175, 80));
    private static readonly SolidColorBrush MakeupBg = new(ColorHelper.FromArgb(96, 33, 150, 243));
    private static readonly SolidColorBrush MergedBg = new(ColorHelper.FromArgb(96, 156, 39, 176));
    private static readonly SolidColorBrush SkippedBg = new(ColorHelper.FromArgb(96, 255, 152, 0));
    private static readonly SolidColorBrush CancelledBg = new(ColorHelper.FromArgb(96, 244, 67, 54));

    private MenuFlyout? _cellMenu;

    public ProgressMatrixView()
    {
        this.InitializeComponent();
        BuildContextMenu();
        UpdateEmptyState();
    }

    #region 로드

    /// <summary>
    /// 대상 수업을 바꾼다.
    /// </summary>
    public async Task LoadAsync(Course? course)
    {
        _selectedCourse = course;
        _rooms = course?.RoomList ?? [];
        _selectedCells.Clear();

        TxtMatrixTitle.Text = course == null ? "진도" : $"진도 — {course.DisplayName}";
        BtnAnalyze.IsEnabled = course != null;
        BtnExport.IsEnabled = course != null;

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _sections = [];
        _progress.Clear();

        if (_selectedCourse == null)
        {
            BuildMatrix();
            return;
        }

        try
        {
            using (var sectionRepo = new CourseSectionRepository(SchoolDatabase.DbPath))
            {
                _sections = await sectionRepo.GetByCourseAsync(_selectedCourse.No);
            }

            using (var progressRepo = new LessonProgressRepository(SchoolDatabase.DbPath))
            {
                foreach (var row in await progressRepo.GetByCourseAsync(_selectedCourse.No))
                    _progress[(row.CourseSectionId, row.Room)] = row;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixView] 진도 로드 실패: {ex.Message}");
            ShowWarning($"진도를 불러오지 못했습니다.\n{ex.Message}");
        }

        BuildMatrix();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _selectedCells.Clear();
        await ReloadAsync();
    }

    #endregion

    #region 매트릭스 그리기

    private void BuildMatrix()
    {
        MatrixGrid.Children.Clear();
        MatrixGrid.RowDefinitions.Clear();
        MatrixGrid.ColumnDefinitions.Clear();
        _cellBorders.Clear();

        if (_selectedCourse == null || _sections.Count == 0 || _rooms.Count == 0)
        {
            UpdateEmptyState();
            UpdateSummary();
            return;
        }

        MatrixEmptyState.Visibility = Visibility.Collapsed;
        MatrixScroll.Visibility = Visibility.Visible;

        // 열: 연번 · 단원명 · 학급들 · 완료 수
        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

        const int roomStartCol = 2;
        foreach (var _ in _rooms)
            MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });

        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

        MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        foreach (var _ in _sections)
            MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });

        AddHeaderCell(0, 0, "#");
        AddHeaderCell(0, 1, "단원");
        for (int i = 0; i < _rooms.Count; i++)
            AddHeaderCell(0, roomStartCol + i, _rooms[i]);
        AddHeaderCell(0, roomStartCol + _rooms.Count, "완료");

        for (int row = 0; row < _sections.Count; row++)
        {
            var section = _sections[row];
            int gridRow = row + 1;

            AddDataCell(gridRow, 0, (row + 1).ToString());
            AddDataCell(gridRow, 1, section.SectionName, section.ShortInfo);

            int completed = 0;
            for (int col = 0; col < _rooms.Count; col++)
            {
                var progress = _progress.GetValueOrDefault((section.No, _rooms[col]));
                AddProgressCell(gridRow, roomStartCol + col, section.No, _rooms[col], progress);

                if (progress?.IsCompleted == true) completed++;
            }

            AddDataCell(gridRow, roomStartCol + _rooms.Count, $"{completed}/{_rooms.Count}");
        }

        int summaryRow = _sections.Count + 1;
        AddHeaderCell(summaryRow, 0, "");
        AddHeaderCell(summaryRow, 1, "합계");

        int total = 0;
        for (int col = 0; col < _rooms.Count; col++)
        {
            int count = CountCompleted(_rooms[col]);
            total += count;
            AddDataCell(summaryRow, roomStartCol + col, count.ToString());
        }
        AddDataCell(summaryRow, roomStartCol + _rooms.Count, total.ToString());

        UpdateCellSelectionVisuals();
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

        if (!string.IsNullOrEmpty(text))
            ToolTipService.SetToolTip(border, text);

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

    private void AddProgressCell(int row, int col, int sectionId, string room, LessonProgress? progress)
    {
        var border = new Border
        {
            Style = CellStyle("MatrixDataCellStyle"),
            Background = BackgroundFor(progress),
            Tag = (sectionId, room),
            IsTabStop = true,
            UseSystemFocusVisuals = true
        };

        border.Child = new TextBlock
        {
            Text = progress?.ShortStatus ?? "",
            Style = CellStyle("MatrixStatusTextStyle")
        };

        if (progress != null)
            ToolTipService.SetToolTip(border, progress.TooltipText);

        border.PointerPressed += OnCellPointerPressed;
        border.KeyDown += OnCellKeyDown;
        border.ContextFlyout = _cellMenu;

        _cellBorders[(sectionId, room)] = border;

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        MatrixGrid.Children.Add(border);
    }

    private static Brush BackgroundFor(LessonProgress? progress)
    {
        if (progress == null) return TransparentBrush;

        return progress.ProgressType switch
        {
            ProgressType.Normal when progress.IsCompleted => CompletedBg,
            ProgressType.Makeup => MakeupBg,
            ProgressType.Merged => MergedBg,
            ProgressType.Skipped => SkippedBg,
            ProgressType.Cancelled => CancelledBg,
            _ => TransparentBrush
        };
    }

    private void RefreshCell(int sectionId, string room)
    {
        if (!_cellBorders.TryGetValue((sectionId, room), out var border)) return;

        var progress = _progress.GetValueOrDefault((sectionId, room));

        border.Background = BackgroundFor(progress);
        if (border.Child is TextBlock text)
            text.Text = progress?.ShortStatus ?? "";

        ToolTipService.SetToolTip(border, progress?.TooltipText);
    }

    private void UpdateEmptyState()
    {
        bool ready = _selectedCourse != null && _sections.Count > 0 && _rooms.Count > 0;

        MatrixEmptyState.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        MatrixScroll.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;

        if (_selectedCourse == null)
        {
            TxtMatrixEmpty.Text = "수업을 먼저 선택하세요";
            TxtMatrixEmptyHint.Text = "위쪽 필터의 [수업] 에서 진도를 볼 수업을 고르세요";
        }
        else if (_rooms.Count == 0)
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

    #region 칸 고르기

    private void OnCellPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ValueTuple<int, string> tag)
            return;

        var key = (tag.Item1, tag.Item2);
        var point = e.GetCurrentPoint(border);

        // 오른쪽 클릭: 고르지 않은 칸이면 그 칸만 고르고 메뉴를 연다
        if (point.Properties.IsRightButtonPressed)
        {
            if (!_selectedCells.Contains(key))
            {
                _selectedCells.Clear();
                _selectedCells.Add(key);
                UpdateCellSelectionVisuals();
            }

            UpdateMenuState();
            return;
        }

        bool ctrl = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        ToggleCell(key, ctrl);
        border.Focus(FocusState.Programmatic);
        e.Handled = true;
    }

    private void OnCellKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ValueTuple<int, string> tag)
            return;

        if (e.Key is not (VirtualKey.Space or VirtualKey.Enter)) return;

        bool ctrl = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        ToggleCell((tag.Item1, tag.Item2), ctrl);
        e.Handled = true;
    }

    private void ToggleCell((int SectionId, string Room) key, bool ctrl)
    {
        if (ctrl)
        {
            if (!_selectedCells.Add(key))
                _selectedCells.Remove(key);
        }
        else if (_selectedCells.Count == 1 && _selectedCells.Contains(key))
        {
            _selectedCells.Clear();
        }
        else
        {
            _selectedCells.Clear();
            _selectedCells.Add(key);
        }

        UpdateCellSelectionVisuals();
        UpdateSummary();
    }

    private void UpdateCellSelectionVisuals()
    {
        foreach (var (key, border) in _cellBorders)
        {
            if (_selectedCells.Contains(key))
            {
                border.BorderBrush = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
                border.BorderThickness = new Thickness(2);
            }
            else
            {
                // 로컬 값을 지워 스타일(ThemeResource)로 되돌린다
                border.ClearValue(Border.BorderBrushProperty);
                border.ClearValue(Border.BorderThicknessProperty);
            }
        }
    }

    #endregion

    #region 컨텍스트 메뉴 · 처리

    private void BuildContextMenu()
    {
        _cellMenu = new MenuFlyout();

        AddMenuItem("완료 처리", "", OnMarkCompleteClick);
        AddMenuItem("미완료로 되돌리기", "", OnMarkIncompleteClick);
        _cellMenu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem("보강 처리", "", OnMakeupClick);
        AddMenuItem("병합 (같은 학급 2개 이상)", "", OnMergeClick);
        AddMenuItem("건너뛰기", "", OnSkipClick);
        AddMenuItem("결강 처리", "", OnCancelClick);
        _cellMenu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem("선택 해제", "", OnClearSelectionClick);
    }

    private void AddMenuItem(string text, string glyph, RoutedEventHandler handler)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += handler;
        _cellMenu!.Items.Add(item);
    }

    private void UpdateMenuState()
    {
        if (_cellMenu == null) return;

        bool canMerge = _selectedCells.Count >= 2
                        && _selectedCells.Select(c => c.Room).Distinct().Count() == 1;

        foreach (var item in _cellMenu.Items.OfType<MenuFlyoutItem>())
        {
            if (item.Text.StartsWith("병합", StringComparison.Ordinal))
                item.IsEnabled = canMerge;
        }
    }

    private async void OnMarkCompleteClick(object sender, RoutedEventArgs e)
        => await ApplyAsync("완료", (repo, sectionId, room) => repo.MarkAsCompletedAsync(sectionId, room, DateTime.Today));

    private async void OnMarkIncompleteClick(object sender, RoutedEventArgs e)
        => await ApplyAsync("미완료", (repo, sectionId, room) => repo.MarkAsIncompleteAsync(sectionId, room));

    private async void OnSkipClick(object sender, RoutedEventArgs e)
    {
        var reason = await AskTextAsync("건너뛰기", "사유 (선택)");
        if (reason == null) return;

        await ApplyAsync("건너뛰기", (repo, sectionId, room) => repo.MarkAsSkippedAsync(sectionId, room, reason));
    }

    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        var reason = await AskTextAsync("결강 처리", "사유 (선택)");
        if (reason == null) return;

        await ApplyAsync("결강", (repo, sectionId, room) => repo.MarkAsCancelledAsync(sectionId, room, reason));
    }

    private async void OnMakeupClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCells.Count == 0)
        {
            ShowWarning("보강할 칸을 먼저 고르세요.");
            return;
        }

        var picker = new CalendarDatePicker
        {
            Date = DateTimeOffset.Now,
            PlaceholderText = "보강 날짜"
        };

        var dialog = new ContentDialog
        {
            Title = "보강 날짜",
            Content = picker,
            PrimaryButtonText = "확인",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        if (await MessageBox.ShowDialogAsync(dialog) != ContentDialogResult.Primary || !picker.Date.HasValue)
            return;

        var date = picker.Date.Value.DateTime.Date;
        await ApplyAsync("보강", (repo, sectionId, room) => repo.MarkAsMakeupAsync(sectionId, room, date));
    }

    private async void OnMergeClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCells.Count < 2)
        {
            ShowWarning("병합하려면 같은 학급의 단원 2개 이상을 고르세요.");
            return;
        }

        var rooms = _selectedCells.Select(c => c.Room).Distinct().ToList();
        if (rooms.Count > 1)
        {
            ShowWarning("병합은 같은 학급 안에서만 됩니다.");
            return;
        }

        if (!await MessageBox.ShowConfirmAsync(
                $"{rooms[0]} 의 {_selectedCells.Count}개 단원을 한 차시로 병합합니다.\n병합한 단원은 모두 완료로 바뀝니다.",
                "단원 병합", "병합", "취소"))
            return;

        await ApplyAsync("병합", (repo, sectionId, room) => repo.MarkAsMergedAsync(sectionId, room, DateTime.Today));
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        _selectedCells.Clear();
        UpdateCellSelectionVisuals();
        UpdateSummary();
    }

    /// <summary>
    /// 고른 칸들에 같은 처리를 적용한다.
    ///
    /// 예전에는 결과를 보지 않고 무조건 성공으로 알려, 한 건도 저장되지 않아도
    /// "0개 완료 처리" 라는 성공 메시지가 떴다. 그래서 시도 수와 반영 수를 따로 센다.
    /// </summary>
    private async Task ApplyAsync(string label, Func<LessonProgressRepository, int, string, Task<bool>> action)
    {
        if (_selectedCourse == null) return;

        if (_selectedCells.Count == 0)
        {
            ShowWarning($"{label} 처리할 칸을 먼저 고르세요.");
            return;
        }

        int attempted = _selectedCells.Count;
        int done = 0;

        try
        {
            using var repo = new LessonProgressRepository(SchoolDatabase.DbPath);

            foreach (var (sectionId, room) in _selectedCells.ToList())
            {
                if (await action(repo, sectionId, room))
                    done++;
            }

            // 화면을 DB 와 다시 맞춘다 — 루프 중간에 터지면 일부만 반영된 상태다.
            foreach (var row in await repo.GetByCourseAsync(_selectedCourse.No))
                _progress[(row.CourseSectionId, row.Room)] = row;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixView] {label} 처리 실패: {ex.Message}");
            ShowWarning($"{label} 처리 중 오류가 났습니다.\n{ex.Message}");
        }

        BuildMatrix();

        if (done < attempted)
        {
            ShowWarning(done == 0
                ? $"{label} 처리가 반영되지 않았습니다."
                : $"{attempted}개 중 {done}개만 {label} 처리됐습니다.");
        }
    }

    private async Task<string?> AskTextAsync(string title, string placeholder)
    {
        if (_selectedCells.Count == 0)
        {
            ShowWarning("칸을 먼저 고르세요.");
            return null;
        }

        var box = new TextBox { PlaceholderText = placeholder, AcceptsReturn = false };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = "확인",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        return await MessageBox.ShowDialogAsync(dialog) == ContentDialogResult.Primary
            ? box.Text?.Trim() ?? string.Empty
            : null;
    }

    #endregion

    #region 격차 분석 · 내보내기

    private async void OnAnalyzeClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null || _rooms.Count == 0)
        {
            ShowWarning("강의실이 있는 수업에서만 격차를 볼 수 있습니다.");
            return;
        }

        try
        {
            using var repo = new LessonProgressRepository(SchoolDatabase.DbPath);
            var gaps = await repo.GetProgressGapsAsync(_selectedCourse.No, _rooms);

            var panel = new StackPanel { Spacing = 8 };

            int maxGap = gaps.Count > 0 ? gaps.Max(g => g.GapFromMax) : 0;
            panel.Children.Add(new TextBlock
            {
                Text = $"최대 격차 {maxGap}단원",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            foreach (var gap in gaps.OrderByDescending(g => g.CompletedCount))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"{gap.Room} — {gap.CompletedCount}/{gap.TotalCount}단원 ({gap.CompletionRate}%) · {gap.StatusDisplay}"
                });
            }

            if (gaps.Count == 0)
                panel.Children.Add(new TextBlock { Text = "아직 기록된 진도가 없습니다." });

            var dialog = new ContentDialog
            {
                Title = "격차 분석",
                Content = panel,
                CloseButtonText = "닫기",
                XamlRoot = this.XamlRoot
            };

            await MessageBox.ShowDialogAsync(dialog);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixView] 격차 분석 실패: {ex.Message}");
            ShowWarning($"격차를 분석하지 못했습니다.\n{ex.Message}");
        }
    }

    private async void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null || _sections.Count == 0 || _rooms.Count == 0)
        {
            ShowWarning("내보낼 진도표가 없습니다.");
            return;
        }

        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            var subject = Helpers.FileNameHelper.Sanitize(_selectedCourse.Subject);
            if (subject.Length == 0) subject = "진도";
            picker.SuggestedFileName = $"{subject}_진도현황_{DateTime.Today:yyyyMMdd}";
            picker.FileTypeChoices.Add("CSV 파일", new List<string> { ".csv" });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            await FileIO.WriteTextAsync(file, GenerateCsv(), Windows.Storage.Streams.UnicodeEncoding.Utf8);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixView] CSV 내보내기 실패: {ex.Message}");
            ShowWarning($"CSV 내보내기 중 오류가 났습니다.\n{ex.Message}");
        }
    }

    private string GenerateCsv()
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');

        sb.Append("연번,단원번호,단원명");
        foreach (var room in _rooms)
            sb.Append(',').Append(Escape(room));
        sb.AppendLine();

        for (int i = 0; i < _sections.Count; i++)
        {
            var section = _sections[i];
            sb.Append(i + 1).Append(',')
              .Append(Escape(section.FullPath)).Append(',')
              .Append(Escape(section.SectionName));

            foreach (var room in _rooms)
            {
                var progress = _progress.GetValueOrDefault((section.No, room));
                sb.Append(',').Append(Escape(CellText(progress)));
            }

            sb.AppendLine();
        }

        return sb.ToString();

        static string CellText(LessonProgress? progress)
        {
            if (progress == null) return "";
            if (!progress.IsCompleted && progress.ProgressType == ProgressType.Normal) return "";

            return progress.CompletedDate.HasValue
                ? $"{progress.ProgressTypeDisplay} {progress.CompletedDate:M/d}"
                : progress.ProgressTypeDisplay;
        }

        static string Escape(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
                return $"\"{field.Replace("\"", "\"\"")}\"";
            return field;
        }
    }

    #endregion

    #region Helper

    private int CountCompleted(string room)
        => _sections.Count(s => _progress.GetValueOrDefault((s.No, room))?.IsCompleted == true);

    private void UpdateSummary()
    {
        if (_selectedCourse == null || _sections.Count == 0 || _rooms.Count == 0)
        {
            TxtMatrixSummary.Text = "";
            return;
        }

        var counts = _rooms.Select(CountCompleted).ToList();
        int max = counts.Max();
        int min = counts.Min();

        var leading = _rooms
            .Where((_, i) => counts[i] == max)
            .ToList();

        var text = $"단원 {_sections.Count}개 · 학급 {_rooms.Count}곳 · 선두 {string.Join(", ", leading)} ({max}단원) · 최대 격차 {max - min}단원";

        if (_selectedCells.Count > 0)
            text += $" · 선택 {_selectedCells.Count}칸";

        TxtMatrixSummary.Text = text;
    }

    private void ShowWarning(string message)
    {
        MatrixInfoBar.Message = message;
        MatrixInfoBar.IsOpen = true;
    }

    #endregion
}
