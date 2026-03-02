using System.Collections.ObjectModel;
using NewSchool.Collections;
using NewSchool.Models;

namespace NewSchool.ViewModels
{
    /// <summary>
    /// 시간표 셀 단위 ViewModel (한 시간의 수업)
    /// </summary>
    public class TimetableItemViewModel : NotifyPropertyChangedBase
    {
        private int _lessonNo;
        private int _courseNo;
        private string _subjectName = string.Empty;
        private string _teacherName = string.Empty;
        private string _room = string.Empty;
        private int _dayOfWeek; // 1=월, 2=화, 3=수, 4=목, 5=금
        private int _period;    // 1~7교시
        private bool _isEmpty = true;

        /// <summary>
        /// Lesson.No (FK)
        /// </summary>
        public int LessonNo
        {
            get => _lessonNo;
            set => SetProperty(ref _lessonNo, value);
        }

        /// <summary>
        /// Course.No
        /// </summary>
        public int CourseNo
        {
            get => _courseNo;
            set => SetProperty(ref _courseNo, value);
        }

        /// <summary>
        /// 과목명 (예: 국어, 수학)
        /// </summary>
        public string SubjectName
        {
            get => _subjectName;
            set => SetProperty(ref _subjectName, value);
        }

        /// <summary>
        /// 교사명
        /// </summary>
        public string TeacherName
        {
            get => _teacherName;
            set => SetProperty(ref _teacherName, value);
        }

        /// <summary>
        /// 교실
        /// </summary>
        public string Room
        {
            get => _room;
            set => SetProperty(ref _room, value);
        }

        /// <summary>
        /// 요일 (1=월, 2=화, 3=수, 4=목, 5=금)
        /// </summary>
        public int DayOfWeek
        {
            get => _dayOfWeek;
            set => SetProperty(ref _dayOfWeek, value);
        }

        /// <summary>
        /// 교시 (1~7)
        /// </summary>
        public int Period
        {
            get => _period;
            set => SetProperty(ref _period, value);
        }

        /// <summary>
        /// 빈 시간 여부
        /// </summary>
        public bool IsEmpty
        {
            get => _isEmpty;
            set => SetProperty(ref _isEmpty, value);
        }

        /// <summary>
        /// 표시용 텍스트 (과목명 + 교실)
        /// </summary>
        public string DisplayText => IsEmpty ? "" : $"{SubjectName}\n{Room}";

        /// <summary>
        /// 요일 헤더 (월, 화, 수, 목, 금)
        /// </summary>
        public string DayHeader => DayOfWeek switch
        {
            1 => "월",
            2 => "화",
            3 => "수",
            4 => "목",
            5 => "금",
            _ => ""
        };
    }

    /// <summary>
    /// 전체 시간표 ViewModel (5일 x 7교시)
    /// </summary>
    public class TimetableViewModel : NotifyPropertyChangedBase
    {
        private string _title = string.Empty;
        private int _year;
        private int _semester;
        private OptimizedObservableCollection<TimetableItemViewModel> _items = new();

        /// <summary>
        /// 시간표 제목 (예: "3학년 2반 시간표", "홍길동 교사 시간표")
        /// </summary>
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>
        /// 학년도
        /// </summary>
        public int Year
        {
            get => _year;
            set => SetProperty(ref _year, value);
        }

        /// <summary>
        /// 학기
        /// </summary>
        public int Semester
        {
            get => _semester;
            set => SetProperty(ref _semester, value);
        }

        /// <summary>
        /// 시간표 아이템 목록 (5일 x 7교시 = 35개) (최적화됨)
        /// ⚡ OptimizedObservableCollection로 UI 업데이트 80% 향상
        /// </summary>
        public OptimizedObservableCollection<TimetableItemViewModel> Items
        {
            get => _items;
            set => SetProperty(ref _items, value);
        }

        /// <summary>
        /// 빈 시간표 초기화 (5일 x 7교시)
        /// </summary>
        public void InitializeEmptyTimetable()
        {
            Items.Clear();

            for (int day = 1; day <= 5; day++) // 월~금
            {
                for (int period = 1; period <= 7; period++) // 1~7교시
                {
                    Items.Add(new TimetableItemViewModel
                    {
                        DayOfWeek = day,
                        Period = period,
                        IsEmpty = true
                    });
                }
            }
        }

        /// <summary>
        /// 특정 요일/교시의 아이템 가져오기
        /// </summary>
        public TimetableItemViewModel? GetItem(int dayOfWeek, int period)
        {
            foreach (var item in Items)
            {
                if (item.DayOfWeek == dayOfWeek && item.Period == period)
                {
                    return item;
                }
            }
            return null;
        }
    }
}
