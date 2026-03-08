using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NewSchool.ViewModels;
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

        public TimetableControl()
        {
            this.InitializeComponent();
            this.DataContextChanged += TimetableControl_DataContextChanged;
        }

        /// <summary>
        /// 교사 시간표 로드 (Lesson 기반)
        /// </summary>
        public async Task LoadTeacherScheduleAsync(string teacherId, int year, int semester)
        {
            DisplayMode = TimetableDisplayMode.Teacher;
            using var service = new NewSchool.Services.LessonService();
            var viewModel = await service.GetTeacherTimetableViewModelAsync(teacherId, year, semester);
            DataContext = viewModel;
        }

        /// <summary>
        /// 현재 사용자(교사) 시간표 로드
        /// </summary>
        public async Task LoadMyScheduleAsync()
        {
            DisplayMode = TimetableDisplayMode.Teacher;
            using var service = new NewSchool.Services.LessonService();
            var viewModel = await service.GetMyTimetableViewModelAsync();
            DataContext = viewModel;
        }

        /// <summary>
        /// 학급 시간표 로드 (Lesson 기반)
        /// </summary>
        public async Task LoadClassScheduleAsync(int year, int semester, int grade, int classNum)
        {
            DisplayMode = TimetableDisplayMode.Class;
            using var service = new NewSchool.Services.LessonService();
            var viewModel = await service.GetClassTimetableViewModelAsync(year, semester, grade, classNum);
            DataContext = viewModel;
        }

        /// <summary>
        /// DataContext가 변경될 때 시간표 셀 생성
        /// </summary>
        private void TimetableControl_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            if (DataContext is TimetableViewModel viewModel)
            {
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

                // 과목명
                var subjectText = new TextBlock
                {
                    Text = item.SubjectName,
                    FontSize = 12,
                    //FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
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

                border.Child = stackPanel;
                border.Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            }

            return border;
        }
    }
}
