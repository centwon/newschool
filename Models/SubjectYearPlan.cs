using System;

namespace NewSchool.Models
{
    /// <summary>
    /// 연간수업계획 기본정보
    /// Course + 학급별로 하나의 연간계획
    /// </summary>
    public class SubjectYearPlan : NotifyPropertyChangedBase, IEntity
    {
        #region Fields

        private int _no = -1;
        private int _courseNo;
        private int _targetGrade;
        private int? _targetClass;
        private int _year;
        private int _semester;
        private int _weeklyHours;
        private int? _totalPlannedHours;
        private string _status = "DRAFT";
        private DateTime _createdAt = DateTime.Now;
        private DateTime _updatedAt = DateTime.Now;

        #endregion

        #region Properties - 기본 정보

        /// <summary>PK (자동 증가)</summary>
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        /// <summary>Course FK (Course.No)</summary>
        public int CourseNo
        {
            get => _courseNo;
            set => SetProperty(ref _courseNo, value);
        }

        #endregion

        #region Properties - 대상 학급

        /// <summary>대상 학년</summary>
        public int TargetGrade
        {
            get => _targetGrade;
            set => SetProperty(ref _targetGrade, value);
        }

        /// <summary>대상 반 (null이면 학년 전체)</summary>
        public int? TargetClass
        {
            get => _targetClass;
            set => SetProperty(ref _targetClass, value);
        }

        #endregion

        #region Properties - 학년도/학기

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

        #region Properties - 시수 정보

        /// <summary>주당 시수 (Course.Unit 기반)</summary>
        public int WeeklyHours
        {
            get => _weeklyHours;
            set => SetProperty(ref _weeklyHours, value);
        }

        /// <summary>전체 계획 시수 (자동 계산)</summary>
        public int? TotalPlannedHours
        {
            get => _totalPlannedHours;
            set => SetProperty(ref _totalPlannedHours, value);
        }

        #endregion

        #region Properties - 상태 관리

        /// <summary>상태: DRAFT, CONFIRMED, ADJUSTED</summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>생성일시</summary>
        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetProperty(ref _createdAt, value);
        }

        /// <summary>수정일시</summary>
        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set => SetProperty(ref _updatedAt, value);
        }

        #endregion

        #region Computed Properties

        /// <summary>대상 학급 표시</summary>
        public string TargetDisplay => TargetClass.HasValue
            ? $"{TargetGrade}학년 {TargetClass}반"
            : $"{TargetGrade}학년 전체";

        /// <summary>상태 표시</summary>
        public string StatusDisplay => Status switch
        {
            "DRAFT" => "작성중",
            "CONFIRMED" => "확정",
            "ADJUSTED" => "조정됨",
            _ => Status
        };

        /// <summary>확정 여부</summary>
        public bool IsConfirmed => Status == "CONFIRMED";

        #endregion

        #region Methods

        public override string ToString()
        {
            return $"{Year}년 {Semester}학기 - {TargetDisplay} ({StatusDisplay})";
        }

        #endregion
    }
}
