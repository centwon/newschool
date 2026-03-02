using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using Windows.System;
using Windows.UI;

namespace NewSchool.Pages;

/// <summary>
/// 진도 매트릭스 페이지
/// </summary>
public sealed partial class ProgressMatrixPage : Page
{
    private List<Course> _courses = [];
    private Course? _selectedCourse;
    private List<string> _rooms = [];
    private List<CourseSection> _sections = [];
    private ProgressMatrixData? _matrixData;

    // 선택된 셀 추적
    private readonly HashSet<(int SectionId, string Room)> _selectedCells = new();

    // 셀 Border 참조 (선택 상태 업데이트용)
    private readonly Dictionary<(int SectionId, string Room), Border> _cellBorders = new();

    // 컨텍스트 메뉴
    private MenuFlyout? _cellContextMenu;

    public ProgressMatrixPage()
    {
        this.InitializeComponent();
        Loaded += OnPageLoaded;
        CreateContextMenu();
    }

    private void CreateContextMenu()
    {
        _cellContextMenu = new MenuFlyout();

        var completeItem = new MenuFlyoutItem
        {
            Text = "완료 처리",
            Icon = new FontIcon { Glyph = "\uE73E" }
        };
        completeItem.Click += OnMarkCompleteClick;

        var incompleteItem = new MenuFlyoutItem
        {
            Text = "미완료 처리",
            Icon = new FontIcon { Glyph = "\uE711" }
        };
        incompleteItem.Click += OnMarkIncompleteClick;

        var makeupItem = new MenuFlyoutItem
        {
            Text = "보강 처리",
            Icon = new FontIcon { Glyph = "\uE710" }
        };
        makeupItem.Click += OnMakeupClick;

        var mergeItem = new MenuFlyoutItem
        {
            Text = "병합 (같은 학급 2개 이상)",
            Icon = new FontIcon { Glyph = "\uE71B" }
        };
        mergeItem.Click += OnMergeClick;

        var skipItem = new MenuFlyoutItem
        {
            Text = "건너뛰기",
            Icon = new FontIcon { Glyph = "\uE893" }
        };
        skipItem.Click += OnSkipClick;

        var clearItem = new MenuFlyoutItem
        {
            Text = "선택 해제",
            Icon = new FontIcon { Glyph = "\uE8BB" }
        };
        clearItem.Click += OnClearSelectionClick;

        _cellContextMenu.Items.Add(completeItem);
        _cellContextMenu.Items.Add(incompleteItem);
        _cellContextMenu.Items.Add(new MenuFlyoutSeparator());
        _cellContextMenu.Items.Add(makeupItem);
        _cellContextMenu.Items.Add(mergeItem);
        _cellContextMenu.Items.Add(skipItem);
        _cellContextMenu.Items.Add(new MenuFlyoutSeparator());
        _cellContextMenu.Items.Add(clearItem);
    }

    #region Initialization

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await LoadCoursesAsync();
    }

    private async Task LoadCoursesAsync()
    {
        ShowLoading("수업 목록 로딩 중...");

        try
        {
            using var courseRepo = new CourseRepository(SchoolDatabase.DbPath);
            _courses = await courseRepo.GetByTeacherAsync(
                Settings.User.Value,
                Settings.WorkYear.Value,
                Settings.WorkSemester.Value);

            CmbCourse.ItemsSource = _courses;

            if (_courses.Count > 0)
            {
                CmbCourse.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixPage] 수업 로드 실패: {ex.Message}");
            ShowError("수업 목록을 불러오는데 실패했습니다.");
        }
        finally
        {
            HideLoading();
        }
    }

    #endregion

    #region Event Handlers - Header

    private async void OnCourseSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbCourse.SelectedItem is not Course course)
        {
            DisableButtons();
            return;
        }

        _selectedCourse = course;
        _rooms = course.RoomList;
        ClearSelection();

        await LoadMatrixAsync();
        EnableButtons();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null) return;
        ClearSelection();
        await LoadMatrixAsync();
    }

    #endregion

    #region Event Handlers - Selection Actions

    private async void OnMarkCompleteClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null || _selectedCells.Count == 0) return;

        ShowLoading("완료 처리 중...");

        try
        {
            using var progressRepo = new LessonProgressRepository(SchoolDatabase.DbPath);

            foreach (var (sectionId, room) in _selectedCells)
            {
                await progressRepo.MarkAsCompletedAsync(sectionId, room, DateTime.Today);
                UpdateCellCompletion(sectionId, room, true);
            }

            int count = _selectedCells.Count;
            ClearSelection();
            await LoadMatrixAsync();
            ShowSuccess($"{count}개 단원 완료 처리됨");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixPage] 완료 처리 실패: {ex.Message}");
            ShowError($"완료 처리 실패: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    private async void OnMarkIncompleteClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null || _selectedCells.Count == 0) return;

        ShowLoading("미완료 처리 중...");

        try
        {
            using var progressRepo = new LessonProgressRepository(SchoolDatabase.DbPath);

            foreach (var (sectionId, room) in _selectedCells)
            {
                await progressRepo.MarkAsIncompleteAsync(sectionId, room);
                UpdateCellCompletion(sectionId, room, false);
            }

            int count = _selectedCells.Count;
            ClearSelection();
            await LoadMatrixAsync();
            ShowSuccess($"{count}개 단원 미완료 처리됨");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixPage] 미완료 처리 실패: {ex.Message}");
            ShowError($"미완료 처리 실패: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    private async void OnMakeupClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null || _selectedCells.Count == 0)
        {
            ShowInfo("보강할 셀을 먼저 선택해주세요.");
            return;
        }

        // 보강 날짜 선택 다이얼로그
        var dialog = new ContentDialog
        {
            Title = "보강 날짜 선택",
            PrimaryButtonText = "확인",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var datePicker = new CalendarDatePicker
        {
            Date = DateTimeOffset.Now,
            PlaceholderText = "보강 날짜"
        };

        dialog.Content = datePicker;

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || !datePicker.Date.HasValue)
            return;

        ShowLoading("보강 처리 중...");

        try
        {
            using var progressRepo = new LessonProgressRepository(SchoolDatabase.DbPath);
            using var sectionRepo = new CourseSectionRepository(SchoolDatabase.DbPath);
            using var scheduleRepo = new ScheduleRepository(SchoolDatabase.DbPath);
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);

            var service = new ProgressSyncService(progressRepo, sectionRepo, scheduleRepo, mapRepo);

            // 학급별로 그룹화하여 처리
            var byRoom = _selectedCells.GroupBy(c => c.Room);

            int totalAffected = 0;
            foreach (var group in byRoom)
            {
                var sectionIds = group.Select(c => c.SectionId).ToList();
                var syncResult = await service.AddMakeupLessonAsync(
                    _selectedCourse.No,
                    group.Key,
                    sectionIds,
                    datePicker.Date.Value.DateTime);

                totalAffected += syncResult.AffectedCount;
            }

            ClearSelection();
            await LoadMatrixAsync();
            ShowSuccess($"{totalAffected}개 단원 보강 처리 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixPage] 보강 처리 실패: {ex.Message}");
            ShowError($"보강 처리 실패: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    private async void OnMergeClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null || _selectedCells.Count < 2)
        {
            ShowInfo("병합하려면 같은 학급의 2개 이상 단원을 선택해주세요.");
            return;
        }

        // 같은 학급인지 확인
        var rooms = _selectedCells.Select(c => c.Room).Distinct().ToList();
        if (rooms.Count > 1)
        {
            ShowWarning("병합은 같은 학급의 단원만 가능합니다.");
            return;
        }

        var room = rooms[0];
        var sectionIds = _selectedCells.Select(c => c.SectionId).ToList();

        // 확인 다이얼로그
        var confirmed = await MessageBox.ShowConfirmAsync(
            $"{room}의 {sectionIds.Count}개 단원을 병합하시겠습니까?\n병합된 단원은 모두 완료 처리됩니다.",
            "단원 병합", "병합", "취소");
        if (!confirmed)
            return;

        ShowLoading("병합 처리 중...");

        try
        {
            using var progressRepo = new LessonProgressRepository(SchoolDatabase.DbPath);
            using var sectionRepo = new CourseSectionRepository(SchoolDatabase.DbPath);
            using var scheduleRepo = new ScheduleRepository(SchoolDatabase.DbPath);
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);

            var service = new ProgressSyncService(progressRepo, sectionRepo, scheduleRepo, mapRepo);

            var syncResult = await service.MergeSectionsAsync(
                _selectedCourse.No,
                room,
                sectionIds,
                DateTime.Today);

            ClearSelection();
            await LoadMatrixAsync();

            if (syncResult.Success)
                ShowSuccess(syncResult.Message);
            else
                ShowError(syncResult.Message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixPage] 병합 처리 실패: {ex.Message}");
            ShowError($"병합 처리 실패: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    private async void OnSkipClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null || _selectedCells.Count == 0)
        {
            ShowInfo("건너뛸 셀을 먼저 선택해주세요.");
            return;
        }

        // 사유 입력 다이얼로그
        var dialog = new ContentDialog
        {
            Title = "건너뛰기",
            PrimaryButtonText = "확인",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var textBox = new TextBox
        {
            PlaceholderText = "사유 입력 (선택)",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 80
        };

        dialog.Content = textBox;

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        ShowLoading("건너뛰기 처리 중...");

        try
        {
            using var progressRepo = new LessonProgressRepository(SchoolDatabase.DbPath);
            using var sectionRepo = new CourseSectionRepository(SchoolDatabase.DbPath);
            using var scheduleRepo = new ScheduleRepository(SchoolDatabase.DbPath);
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);

            var service = new ProgressSyncService(progressRepo, sectionRepo, scheduleRepo, mapRepo);

            int totalAffected = 0;
            var byRoom = _selectedCells.GroupBy(c => c.Room);

            foreach (var group in byRoom)
            {
                var sectionIds = group.Select(c => c.SectionId).ToList();
                var syncResult = await service.SkipSectionsAsync(
                    _selectedCourse.No,
                    group.Key,
                    sectionIds,
                    string.IsNullOrEmpty(textBox.Text) ? null : textBox.Text);

                totalAffected += syncResult.AffectedCount;
            }

            ClearSelection();
            await LoadMatrixAsync();
            ShowSuccess($"{totalAffected}개 단원 건너뛰기 처리 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixPage] 건너뛰기 처리 실패: {ex.Message}");
            ShowError($"건너뛰기 처리 실패: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        ClearSelection();
    }

    #endregion

    #region Event Handlers - Tools

    private async void OnAnalyzeClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null) return;

        ShowLoading("격차 분석 중...");

        try
        {
            using var progressRepo = new LessonProgressRepository(SchoolDatabase.DbPath);
            using var sectionRepo = new CourseSectionRepository(SchoolDatabase.DbPath);
            using var scheduleRepo = new ScheduleRepository(SchoolDatabase.DbPath);
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);

            var service = new ProgressSyncService(progressRepo, sectionRepo, scheduleRepo, mapRepo);

            var analysis = await service.AnalyzeProgressGapAsync(_selectedCourse.No, _rooms);
            var suggestions = await service.SuggestSyncActionsAsync(_selectedCourse.No, _rooms);

            // 결과 표시 다이얼로그
            var content = new StackPanel { Spacing = 12 };

            // 격차 요약
            content.Children.Add(new TextBlock
            {
                Text = $"최대 격차: {analysis.MaxGap}단원",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            content.Children.Add(new TextBlock
            {
                Text = $"선두: {string.Join(", ", analysis.LeadingRooms)} ({analysis.MaxCompleted}단원)",
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 76, 175, 80))
            });

            if (analysis.BehindRooms.Count > 0)
            {
                content.Children.Add(new TextBlock
                {
                    Text = $"뒤처짐: {string.Join(", ", analysis.BehindRooms)}",
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 244, 67, 54))
                });
            }

            // 제안 목록
            if (suggestions.Count > 0)
            {
                content.Children.Add(new TextBlock
                {
                    Text = "제안:",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 0)
                });

                foreach (var suggestion in suggestions.Take(5))
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = $"{suggestion.TypeIcon} {suggestion.Description}",
                        TextWrapping = TextWrapping.Wrap
                    });
                }
            }

            var dialog = new ContentDialog
            {
                Title = "격차 분석 결과",
                Content = content,
                CloseButtonText = "닫기",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixPage] 격차 분석 실패: {ex.Message}");
            ShowError($"격차 분석 실패: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    private async void OnSyncClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null) return;

        var confirmed = await MessageBox.ShowConfirmAsync(
            "완료된 일정을 기반으로 진도를 동기화하시겠습니까?",
            "일정 동기화", "동기화", "취소");
        if (!confirmed)
            return;

        ShowLoading("동기화 중...");

        try
        {
            using var progressRepo = new LessonProgressRepository(SchoolDatabase.DbPath);
            using var sectionRepo = new CourseSectionRepository(SchoolDatabase.DbPath);
            using var scheduleRepo = new ScheduleRepository(SchoolDatabase.DbPath);
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);

            var service = new ProgressSyncService(progressRepo, sectionRepo, scheduleRepo, mapRepo);

            int totalAffected = 0;
            foreach (var room in _rooms)
            {
                var syncResult = await service.SyncProgressFromSchedulesAsync(_selectedCourse.No, room);
                totalAffected += syncResult.AffectedCount;
            }

            await LoadMatrixAsync();
            ShowSuccess($"{totalAffected}개 단원 동기화 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixPage] 동기화 실패: {ex.Message}");
            ShowError($"동기화 실패: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null) return;

        // 파일 저장 다이얼로그
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("Excel 파일", new List<string> { ".xlsx" });
        picker.SuggestedFileName = $"진도현황_{_selectedCourse.Subject}_{DateTime.Now:yyyyMMdd}";

        // WinUI 3에서 FileSavePicker 사용
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        ShowLoading("엑셀 내보내는 중...");

        try
        {
            using var progressRepo = new LessonProgressRepository(SchoolDatabase.DbPath);
            using var sectionRepo = new CourseSectionRepository(SchoolDatabase.DbPath);
            using var scheduleRepo = new ScheduleRepository(SchoolDatabase.DbPath);
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);

            var exportService = new ReportExportService(sectionRepo, scheduleRepo, mapRepo, progressRepo);

            var exportResult = await exportService.ExportProgressMatrixToExcelAsync(
                _selectedCourse,
                _rooms,
                file.Path);

            if (exportResult.Success)
            {
                ShowSuccess(exportResult.Message);
            }
            else
            {
                ShowError(exportResult.Message);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixPage] 내보내기 실패: {ex.Message}");
            ShowError($"내보내기 실패: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    #endregion

    #region Matrix Building

    private async Task LoadMatrixAsync()
    {
        if (_selectedCourse == null) return;

        ShowLoading("진도 데이터 로딩 중...");

        try
        {
            using var progressRepo = new LessonProgressRepository(SchoolDatabase.DbPath);
            using var sectionRepo = new CourseSectionRepository(SchoolDatabase.DbPath);
            using var scheduleRepo = new ScheduleRepository(SchoolDatabase.DbPath);
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);

            var service = new ProgressSyncService(progressRepo, sectionRepo, scheduleRepo, mapRepo);

            _matrixData = await service.GetProgressMatrixAsync(_selectedCourse.No, _rooms);
            _sections = _matrixData.Sections;

            BuildMatrix();
            UpdateSummary();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProgressMatrixPage] 매트릭스 로드 실패: {ex.Message}");
            ShowError($"진도 데이터 로드 실패: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    private void BuildMatrix()
    {
        MatrixGrid.Children.Clear();
        MatrixGrid.RowDefinitions.Clear();
        MatrixGrid.ColumnDefinitions.Clear();
        _cellBorders.Clear();

        if (_matrixData == null || _sections.Count == 0 || _rooms.Count == 0)
            return;

        // 열 정의: 번호 + 단원명 + 학급들 + 합계
        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });  // 번호
        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) }); // 단원명

        foreach (var room in _rooms)
        {
            MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        }

        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // 합계

        // 행 정의: 헤더 + 단원들 + 합계
        MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) }); // 헤더

        foreach (var section in _sections)
        {
            MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        }

        MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) }); // 합계

        // 헤더 행 추가
        AddHeaderCell(0, 0, "#");
        AddHeaderCell(0, 1, "단원");

        for (int i = 0; i < _rooms.Count; i++)
        {
            AddHeaderCell(0, i + 2, _rooms[i]);
        }

        AddHeaderCell(0, _rooms.Count + 2, "완료");

        // 데이터 행 추가
        for (int row = 0; row < _sections.Count; row++)
        {
            var section = _sections[row];

            // 번호
            AddDataCell(row + 1, 0, (row + 1).ToString());

            // 단원명
            AddDataCell(row + 1, 1, section.SectionName, section.ShortInfo);

            // 학급별 진도
            int completedCount = 0;
            for (int col = 0; col < _rooms.Count; col++)
            {
                var cell = _matrixData.GetCell(section.No, _rooms[col]);
                AddProgressCell(row + 1, col + 2, section.No, _rooms[col], cell);

                if (cell?.IsCompleted == true)
                    completedCount++;
            }

            // 완료 수
            AddDataCell(row + 1, _rooms.Count + 2, $"{completedCount}/{_rooms.Count}");
        }

        // 합계 행
        AddHeaderCell(_sections.Count + 1, 0, "");
        AddHeaderCell(_sections.Count + 1, 1, "합계");

        for (int col = 0; col < _rooms.Count; col++)
        {
            var room = _rooms[col];
            var stats = _matrixData.StatsByRoom.GetValueOrDefault(room);
            var completedCount = stats?.CompletedCount ?? 0;
            AddDataCell(_sections.Count + 1, col + 2, $"{completedCount}");
        }

        int totalCompleted = _matrixData.StatsByRoom.Values.Sum(s => s.CompletedCount);
        AddDataCell(_sections.Count + 1, _rooms.Count + 2, totalCompleted.ToString());
    }

    private void AddHeaderCell(int row, int col, string text)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 240, 240, 240)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 200, 200, 200)),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(8, 4, 8, 4)
        };

        var textBlock = new TextBlock
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        border.Child = textBlock;

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        MatrixGrid.Children.Add(border);
    }

    private void AddDataCell(int row, int col, string text, string? tooltip = null)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 220, 220, 220)),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(8, 4, 8, 4)
        };

        var textBlock = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        if (!string.IsNullOrEmpty(tooltip))
        {
            ToolTipService.SetToolTip(border, tooltip);
        }

        border.Child = textBlock;

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        MatrixGrid.Children.Add(border);
    }

    private void AddProgressCell(int row, int col, int sectionId, string room, ProgressMatrixCell? cell)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 220, 220, 220)),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Tag = new ProgressCellTag { SectionId = sectionId, Room = room }
        };

        // 상태에 따른 배경색 및 아이콘
        string icon = "";
        Color bgColor = Colors.Transparent;

        if (cell?.Progress != null)
        {
            bgColor = cell.Progress.ProgressType switch
            {
                ProgressType.Normal when cell.IsCompleted => ColorHelper.FromArgb(96, 76, 175, 80),
                ProgressType.Makeup => ColorHelper.FromArgb(96, 33, 150, 243),
                ProgressType.Merged => ColorHelper.FromArgb(96, 156, 39, 176),
                ProgressType.Skipped => ColorHelper.FromArgb(96, 255, 152, 0),
                ProgressType.Cancelled => ColorHelper.FromArgb(96, 244, 67, 54),
                _ => Colors.Transparent
            };

            icon = cell.Progress.ShortStatus;
            ToolTipService.SetToolTip(border, cell.Progress.TooltipText);
        }

        border.Background = new SolidColorBrush(bgColor);

        // 내용: 아이콘 표시
        var textBlock = new TextBlock
        {
            Text = icon,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        border.Child = textBlock;

        // 클릭 이벤트
        border.PointerPressed += OnCellPointerPressed;
        border.PointerEntered += OnCellPointerEntered;
        border.PointerExited += OnCellPointerExited;

        // Border 참조 저장
        _cellBorders[(sectionId, room)] = border;

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        MatrixGrid.Children.Add(border);
    }

    #endregion

    #region Cell Interaction

    private void OnCellPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ProgressCellTag tag)
            return;

        var point = e.GetCurrentPoint(border);
        var key = (tag.SectionId, tag.Room);

        // 우클릭: 컨텍스트 메뉴 표시
        if (point.Properties.IsRightButtonPressed)
        {
            // 클릭한 셀이 선택되어 있지 않으면 해당 셀만 선택
            if (!_selectedCells.Contains(key))
            {
                _selectedCells.Clear();
                _selectedCells.Add(key);
                UpdateAllCellSelectionVisuals();
            }

            // 컨텍스트 메뉴 표시
            ShowContextMenu(border, e);
            e.Handled = true;
            return;
        }

        // 좌클릭: 선택 처리
        // Ctrl 클릭: 토글 선택
        // 일반 클릭: 단일 선택 또는 토글
        bool isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isCtrlPressed)
        {
            // 토글
            if (_selectedCells.Contains(key))
            {
                _selectedCells.Remove(key);
            }
            else
            {
                _selectedCells.Add(key);
            }
        }
        else
        {
            // 이미 선택된 셀을 다시 클릭하면 선택 해제
            if (_selectedCells.Count == 1 && _selectedCells.Contains(key))
            {
                _selectedCells.Clear();
            }
            else
            {
                // 새로운 단일 선택
                _selectedCells.Clear();
                _selectedCells.Add(key);
            }
        }

        UpdateAllCellSelectionVisuals();
    }

    private void ShowContextMenu(Border border, PointerRoutedEventArgs e)
    {
        if (_cellContextMenu == null) return;

        // 병합 메뉴 아이템 활성화/비활성화
        var rooms = _selectedCells.Select(c => c.Room).Distinct().ToList();
        bool canMerge = _selectedCells.Count >= 2 && rooms.Count == 1;

        foreach (var item in _cellContextMenu.Items)
        {
            if (item is MenuFlyoutItem menuItem && menuItem.Text.StartsWith("병합"))
            {
                menuItem.IsEnabled = canMerge;
            }
        }

        // 마우스 위치에 메뉴 표시
        var position = e.GetCurrentPoint(border).Position;
        _cellContextMenu.ShowAt(border, position);
    }

    private void OnCellPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is ProgressCellTag tag)
        {
            var key = (tag.SectionId, tag.Room);
            if (!_selectedCells.Contains(key))
            {
                border.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 100, 100, 100));
            }
        }
    }

    private void OnCellPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is ProgressCellTag tag)
        {
            var key = (tag.SectionId, tag.Room);
            if (!_selectedCells.Contains(key))
            {
                border.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 220, 220, 220));
            }
        }
    }

    private void UpdateAllCellSelectionVisuals()
    {
        foreach (var kvp in _cellBorders)
        {
            var border = kvp.Value;
            bool isSelected = _selectedCells.Contains(kvp.Key);

            if (isSelected)
            {
                border.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 120, 212));
                border.BorderThickness = new Thickness(2);
            }
            else
            {
                border.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 220, 220, 220));
                border.BorderThickness = new Thickness(0, 0, 1, 1);
            }
        }
    }

    private void ClearSelection()
    {
        _selectedCells.Clear();
        UpdateAllCellSelectionVisuals();
    }

    #endregion

    #region Data Update

    /// <summary>
    /// 내부 데이터의 셀 완료 상태 업데이트
    /// </summary>
    private void UpdateCellCompletion(int sectionId, string room, bool isCompleted)
    {
        if (_matrixData == null) return;

        var cell = _matrixData.GetCell(sectionId, room);
        if (cell?.Progress != null)
        {
            cell.Progress.IsCompleted = isCompleted;
            cell.Progress.CompletedDate = isCompleted ? DateTime.Today : null;
        }
        else if (cell != null && isCompleted)
        {
            // Progress가 없었는데 완료됨 - 새 Progress 객체 생성
            cell.Progress = new LessonProgress
            {
                CourseSectionId = sectionId,
                Room = room,
                IsCompleted = true,
                CompletedDate = DateTime.Today,
                ProgressType = ProgressType.Normal
            };
        }

        // 학급별 통계 업데이트
        if (_matrixData.StatsByRoom.TryGetValue(room, out var stats))
        {
            // 완료 수 재계산
            int completedCount = _matrixData.Cells
                .Where(c => c.Room == room && c.IsCompleted)
                .Count();
            stats.CompletedCount = completedCount;
        }
    }

    private void UpdateSummary()
    {
        if (_matrixData == null) return;

        TxtTotalSections.Text = _sections.Count.ToString();

        // 학급별 통계가 없으면 초기값 표시
        if (_matrixData.StatsByRoom.Count == 0)
        {
            TxtLeadingRoom.Text = "-";
            TxtMaxGap.Text = "0";
            return;
        }

        // 선두 학급
        var maxCompleted = _matrixData.StatsByRoom.Values.Max(s => s.CompletedCount);
        var leadingRooms = _matrixData.StatsByRoom
            .Where(kvp => kvp.Value.CompletedCount == maxCompleted)
            .Select(kvp => kvp.Key)
            .ToList();

        TxtLeadingRoom.Text = leadingRooms.Count > 0
            ? $"{string.Join(", ", leadingRooms)} ({maxCompleted})"
            : "-";

        // 최대 격차
        var minCompleted = _matrixData.StatsByRoom.Values.Min(s => s.CompletedCount);
        TxtMaxGap.Text = (maxCompleted - minCompleted).ToString();
    }

    #endregion

    #region UI Helpers

    private void EnableButtons()
    {
        BtnAnalyze.IsEnabled = true;
        BtnSync.IsEnabled = true;
        BtnExport.IsEnabled = true;
    }

    private void DisableButtons()
    {
        BtnAnalyze.IsEnabled = false;
        BtnSync.IsEnabled = false;
        BtnExport.IsEnabled = false;
    }

    private void ShowLoading(string message)
    {
        TxtLoadingMessage.Text = message;
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    private void HideLoading()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private async void ShowInfo(string message)
    {
        await MessageBox.ShowAsync(message, "안내");
    }

    private async void ShowSuccess(string message)
    {
        await MessageBox.ShowAsync(message, "완료");
    }

    private async void ShowWarning(string message)
    {
        await MessageBox.ShowAsync(message, "경고");
    }

    private async void ShowError(string message)
    {
        await MessageBox.ShowErrorAsync(message);
    }

    #endregion
}

/// <summary>
/// 진도 셀 태그 데이터
/// </summary>
public class ProgressCellTag
{
    public int SectionId { get; set; }
    public string Room { get; set; } = string.Empty;
}
