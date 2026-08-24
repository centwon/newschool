using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace NewSchool.Controls
{
    /// <summary>
    /// 시간표 표시 모드
    /// </summary>
    public enum TimetableDisplayMode
    {
        /// <summary>교사용 시간표 (과목 + 강의실)</summary>
        Teacher,
        /// <summary>학급용 시간표 (과목 + 교사)</summary>
        Class
    }

    /// <summary>
    /// 시간표 표시 UserControl
    /// 5일(월~금) x 7교시 그리드
    /// </summary>
    public sealed partial class TimetableControl : UserControl
    {
        /// <summary>
        /// 시간표 표시 모드 (Teacher: 과목+강의실, Class: 과목+교사)
        /// </summary>
        public static readonly DependencyProperty DisplayModeProperty =
            DependencyProperty.Register(
                nameof(DisplayMode),
                typeof(TimetableDisplayMode),
                typeof(TimetableControl),
                new PropertyMetadata(TimetableDisplayMode.Class));

        public TimetableDisplayMode DisplayMode
        {
            get => (TimetableDisplayMode)GetValue(DisplayModeProperty);
            set => SetValue(DisplayModeProperty, value);
        }

        /// <summary>
        /// 보고 있는 주의 월요일. null 이면 요일만 있는 <b>평소 시간표</b>다(날짜·변경 표시 없음).
        /// </summary>
        public DateTime? WeekMonday { get; private set; }

        /// <summary>
        /// 수업이 든 칸을 누를 수 있게 한다(기본 꺼짐). 켜면 <see cref="SlotInvoked"/> 가 뜨고,
        /// 무엇을 할지는 부르는 화면이 정한다 — 이 컨트롤은 학급 시간표에도 쓰이므로
        /// 동작을 안에 넣지 않는다. 빈 칸과 휴강 칸은 누를 수 없다.
        /// </summary>
        public bool IsSlotClickable { get; set; }

        /// <summary>수업이 든 칸을 눌렀다(<see cref="IsSlotClickable"/> 이 켜져 있을 때만).</summary>
        public event EventHandler<TimetableItemViewModel>? SlotInvoked;

        public TimetableControl()
        {
            this.InitializeComponent();
            this.DataContextChanged += TimetableControl_DataContextChanged;
        }

        /// <summary>
        /// 현재 사용자(교사)의 <b>그 주</b> 시간표. 평소 시간표에 그 주의 변경
        /// (휴강·교체·보강·대강)을 얹어 보여 준다. 읽기 전용이다 —
        /// 변경을 넣고 고치는 곳은 수업 관리의 [주별 시간표 확인 및 변경] 탭이다.
        /// </summary>
        public async Task LoadMyWeekScheduleAsync(DateTime anyDateInWeek)
        {
            DisplayMode = TimetableDisplayMode.Teacher;

            var monday = anyDateInWeek.Date;
            while (monday.DayOfWeek != DayOfWeek.Monday)
                monday = monday.AddDays(-1);

            WeekMonday = monday;

            TimetableViewModel viewModel;
            using (var service = new NewSchool.Services.LessonService())
            {
                viewModel = await service.GetMyTimetableViewModelAsync();
            }

            await ApplyWeekChangesAsync(viewModel, monday);

            UpdateDayHeaders(monday);
            DataContext = viewModel;
        }

        /// <summary>
        /// 그 주 각 날짜의 변경을 평소 시간표 위에 얹는다.
        /// 규칙은 오늘 화면과 같은 <see cref="NewSchool.Services.TimetableChangeMerger"/> 를 쓴다 —
        /// 두 화면이 서로 다른 답을 내면 안 된다.
        /// </summary>
        private static async Task ApplyWeekChangesAsync(TimetableViewModel viewModel, DateTime monday)
        {
            try
            {
                using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);

                for (int day = 1; day <= 5; day++)
                {
                    var changes = await repo.GetByDateAsync(Settings.User.Value, monday.AddDays(day - 1));
                    if (changes.Count == 0) continue;

                    var slots = Enumerable.Range(1, 7)
                        .Select(p => viewModel.GetItem(day, p))
                        .Where(i => i is { IsEmpty: false })
                        .Select(i => i!)
                        .ToList();

                    foreach (var merged in NewSchool.Services.TimetableChangeMerger.Apply(slots, changes, day))
                    {
                        // 병합 결과를 격자의 제자리에 옮겨 적는다.
                        // 보강·대강은 새 항목으로 나오므로 그 교시의 빈 칸을 채우게 된다.
                        var cell = viewModel.GetItem(day, merged.Period);
                        if (cell == null || ReferenceEquals(cell, merged)) continue;

                        cell.IsEmpty = false;
                        cell.CourseNo = merged.CourseNo;
                        cell.SubjectName = merged.SubjectName;
                        cell.Room = merged.Room;
                        cell.ChangeKind = merged.ChangeKind;
                        cell.ChangeMemo = merged.ChangeMemo;
                    }
                }
            }
            catch (Exception ex)
            {
                // 변경을 못 읽었다고 평소 시간표까지 버리지는 않는다.
                Debug.WriteLine($"[TimetableControl] 주간 변경 반영 실패: {ex.Message}");
            }
        }

        /// <summary>요일 아래 날짜를 채운다. 오늘은 굵게.</summary>
        private void UpdateDayHeaders(DateTime monday)
        {
            var labels = new[] { DayDate1, DayDate2, DayDate3, DayDate4, DayDate5 };

            for (int i = 0; i < labels.Length; i++)
            {
                var date = monday.AddDays(i);

                labels[i].Text = date.ToString("M/d");
                labels[i].Visibility = Visibility.Visible;
                labels[i].FontWeight = date == DateTime.Today
                    ? Microsoft.UI.Text.FontWeights.Bold
                    : Microsoft.UI.Text.FontWeights.Normal;
                labels[i].Foreground = (SolidColorBrush)Application.Current.Resources[
                    date == DateTime.Today ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];
            }
        }

        // 외부 로드 진입점 셋(LoadTeacherScheduleAsync·LoadMyScheduleAsync·
        // LoadClassScheduleAsync)은 호출부가 없어 지웠다(39차). 이 컨트롤은 Loaded 에서
        // 스스로 내 시간표를 읽어 오고, 다른 시간표는 DataContext 를 직접 넣어 보여 준다.

        /// <summary>
        /// DataContext가 변경될 때 시간표 셀 생성
        /// </summary>
        /// <summary>
        /// 지금 그려 놓은 시간표에 수업이 한 칸이라도 있는가.
        /// 호출부가 <b>빈 시간표</b>에 안내를 얹을지 판단할 때 쓴다.
        /// </summary>
        public bool HasAnyLesson { get; private set; }

        private void TimetableControl_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            if (DataContext is TimetableViewModel viewModel)
            {
                HasAnyLesson = viewModel.Items.Any(i => !i.IsEmpty);
                UpdateTimetable(viewModel);
            }
        }

        /// <summary>
        /// 시간표 업데이트
        /// </summary>
        private void UpdateTimetable(TimetableViewModel viewModel)
        {
            // 제목 설정
            //TitleTextBlock.Text = viewModel.Title;

            // 기존 셀 제거 (헤더는 유지)
            RemoveExistingCells();

            // 새 셀 생성
            CreateTimetableCells(viewModel);
        }

        /// <summary>
        /// 기존에 동적으로 생성된 셀 제거
        /// </summary>
        private void RemoveExistingCells()
        {
            var cellsToRemove = TimetableGrid.Children
                .Where(child => Grid.GetRow(child as FrameworkElement) > 0 && 
                               Grid.GetColumn(child as FrameworkElement) > 0)
                .ToList();

            foreach (var cell in cellsToRemove)
            {
                TimetableGrid.Children.Remove(cell);
            }
        }

        /// <summary>
        /// 시간표 셀 생성 (5일 x 7교시)
        /// </summary>
        private void CreateTimetableCells(TimetableViewModel viewModel)
        {
            for (int day = 1; day <= 5; day++) // 월~금
            {
                for (int period = 1; period <= 7; period++) // 1~7교시
                {
                    var item = viewModel.GetItem(day, period);
                    var cell = item != null
                        ? CreateCell(item)
                        : new Border
                        {
                            Padding = new Thickness(2),
                            Background = (SolidColorBrush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]
                        };
                    Grid.SetRow(cell, period); // period (1~7)
                    Grid.SetColumn(cell, day); // day (1~5)
                    TimetableGrid.Children.Add(cell);
                }
            }
        }

        /// <summary>
        /// 개별 셀 생성
        /// </summary>
        private Border CreateCell(TimetableItemViewModel item)
        {
            var border = new Border
            {
                Padding = new Thickness(2)
            };

            if (item.IsEmpty)
            {
                // 빈 시간
                border.Background = (SolidColorBrush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
            }
            else
            {
                // 수업 정보
                var stackPanel = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 0
                };

                // 과목명 — 주 단위로 볼 때는 (교)/(보)/(대)/(휴) 표식이 앞에 붙는다
                var subjectText = new TextBlock
                {
                    Text = item.SubjectWithPrefix,
                    FontSize = 12,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };

                if (item.IsCancelled)
                {
                    subjectText.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                    subjectText.Opacity = 0.6;
                }

                stackPanel.Children.Add(subjectText);

                // 교사용 시간표: 강의실 표시
                if (DisplayMode == TimetableDisplayMode.Teacher)
                {
                    if (!string.IsNullOrEmpty(item.Room))
                    {
                        var roomText = new TextBlock
                        {
                            Text = item.Room,
                            FontSize = 10,
                            Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                            TextAlignment = TextAlignment.Center
                        };
                        stackPanel.Children.Add(roomText);
                    }
                }
                // 학급용 시간표: 교사명 표시
                else
                {
                    if (!string.IsNullOrEmpty(item.TeacherName))
                    {
                        var teacherText = new TextBlock
                        {
                            Text = item.TeacherName,
                            FontSize = 10,
                            Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                            TextAlignment = TextAlignment.Center
                        };
                        stackPanel.Children.Add(teacherText);
                    }
                }

                // 휴강은 누를 수 없다 — 하지 않은 수업의 일지를 쓸 일은 없다.
                bool clickable = IsSlotClickable && !item.IsCancelled;

                // 툴팁은 늘 가장 안쪽 요소에 건다. 버튼이 끼면 테두리에 건 툴팁은 가려진다.
                FrameworkElement content = clickable ? CreateSlotButton(item, stackPanel) : stackPanel;
                border.Child = content;

                border.Background = (SolidColorBrush)Application.Current.Resources[
                    item.HasChange ? "SystemFillColorCautionBackgroundBrush" : "CardBackgroundFillColorDefaultBrush"];

                string? tooltip = item.HasChange ? item.ChangeTooltip : null;
                if (clickable)
                    tooltip = tooltip == null ? SlotClickHint : $"{tooltip} · {SlotClickHint}";

                if (tooltip != null)
                    ToolTipService.SetToolTip(content, tooltip);
            }

            return border;
        }

        private const string SlotClickHint = "눌러서 수업 일지 쓰기";

        /// <summary>
        /// 칸 내용을 누를 수 있게 감싼다. 배경을 투명으로 두어 시간표 모양은 그대로 두고,
        /// 눌림·포커스·키보드 조작은 Button 이 알아서 해 준다.
        /// </summary>
        private Button CreateSlotButton(TimetableItemViewModel item, FrameworkElement content)
        {
            var button = new Button
            {
                Content = content,
                Tag = item,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            button.Click += OnSlotClick;
            return button;
        }

        private void OnSlotClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: TimetableItemViewModel item })
                SlotInvoked?.Invoke(this, item);
        }
    }
}
