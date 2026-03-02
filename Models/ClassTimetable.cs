using System;

namespace NewSchool.Models
{
    /// <summary>
    /// 학급 시간표
    /// 학급 관점의 시간표 정보 관리 (학생/학급용)
    /// </summary>
    public class ClassTimetable : NotifyPropertyChangedBase, IEntity, IYearSemesterEntity
    {
        #region Fields

        private int _no = -1;
        private string _schoolCode = string.Empty;
        private int _year = DateTime.Today.Year;
        private int _semester = 1;
        private int _grade;
        private int _class;
        private int _dayOfWeek;
        private int _period;
        private string _subjectName = string.Empty;
        private string _teacherName = string.Empty;
        private string _room = string.Empty;

        #endregion

        #region Properties - 기본 정보

        /// <summary>PK (자동 증가)</summary>
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        /// <summary>학교 코드 (FK: School.SchoolCode)</summary>
        public string SchoolCode
        {
            get => _schoolCode;
            set => SetProperty(ref _schoolCode, value);
        }

        /// <summary>학년도</summary>
        public int Year
        {
            get => _year;
            set => SetProperty(ref _year, value);
        }

        /// <summary>학기 (1 or 2)</summary>
        public int Semester
        {
            get => _semester;
            set => SetProperty(ref _semester, value);
        }

        #endregion

        #region Properties - 학급 정보

        /// <summary>학년</summary>
        public int Grade
        {
            get => _grade;
            set => SetProperty(ref _grade, value);
        }

        /// <summary>반</summary>
        public int Class
        {
            get => _class;
            set => SetProperty(ref _class, value);
        }

        #endregion

        #region Properties - 시간표 정보

        /// <summary>요일 (1=월, 2=화, 3=수, 4=목, 5=금)</summary>
        public int DayOfWeek
        {
            get => _dayOfWeek;
            set => SetProperty(ref _dayOfWeek, value);
        }

        /// <summary>교시 (1~7)</summary>
        public int Period
        {
            get => _period;
            set => SetProperty(ref _period, value);
        }

        /// <summary>과목명</summary>
        public string SubjectName
        {
            get => _subjectName;
            set => SetProperty(ref _subjectName, value);
        }

        /// <summary>교사명</summary>
        public string TeacherName
        {
            get => _teacherName;
            set => SetProperty(ref _teacherName, value);
        }

        /// <summary>강의실</summary>
        public string Room
        {
            get => _room;
            set => SetProperty(ref _room, value);
        }

        #endregion

        #region Computed Properties

        /// <summary>요일명</summary>
        public string DayName => DayOfWeek switch
        {
            1 => "월",
            2 => "화",
            3 => "수",
            4 => "목",
            5 => "금",
            _ => ""
        };

        /// <summary>학급 표시</summary>
        public string ClassInfo => $"{Grade}학년 {Class}반";

        #endregion

        #region Methods

        public override string ToString()
        {
            return $"{ClassInfo} - {DayName}요일 {Period}교시: {SubjectName}";
        }

        #endregion
    }
}
