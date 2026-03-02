namespace NewSchool.Models
{
    /// <summary>
    /// 주차별 수업 시수
    /// 자동 계산 + 사용자 수정 가능
    /// </summary>
    public class WeeklyLessonHours : NotifyPropertyChangedBase, IEntity
    {
        #region Fields

        private int _no = -1;
        private int _yearPlanNo;
        private int _week;
        private string _weekStartDate = string.Empty;
        private string _weekEndDate = string.Empty;
        private int _autoCalculatedHours;
        private int _plannedHours;
        private int _actualHours;
        private int _isModified;
        private string _notes = string.Empty;

        #endregion

        #region Properties - 기본 정보

        /// <summary>PK (자동 증가)</summary>
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        /// <summary>연간계획 FK (SubjectYearPlan.No)</summary>
        public int YearPlanNo
        {
            get => _yearPlanNo;
            set => SetProperty(ref _yearPlanNo, value);
        }

        #endregion

        #region Properties - 주차 정보

        /// <summary>주차 (1, 2, 3, ...)</summary>
        public int Week
        {
            get => _week;
            set => SetProperty(ref _week, value);
        }

        /// <summary>주 시작일 (yyyy-MM-dd)</summary>
        public string WeekStartDate
        {
            get => _weekStartDate;
            set => SetProperty(ref _weekStartDate, value);
        }

        /// <summary>주 종료일 (yyyy-MM-dd)</summary>
        public string WeekEndDate
        {
            get => _weekEndDate;
            set => SetProperty(ref _weekEndDate, value);
        }

        #endregion

        #region Properties - 시수 정보

        /// <summary>자동 계산된 시수 (참고용, 변경 불가)</summary>
        public int AutoCalculatedHours
        {
            get => _autoCalculatedHours;
            set => SetProperty(ref _autoCalculatedHours, value);
        }

        /// <summary>계획 시수 (사용자 수정 가능)</summary>
        public int PlannedHours
        {
            get => _plannedHours;
            set => SetProperty(ref _plannedHours, value);
        }

        /// <summary>실제 진행 시수 (LessonLog 집계)</summary>
        public int ActualHours
        {
            get => _actualHours;
            set => SetProperty(ref _actualHours, value);
        }

        #endregion

        #region Properties - 수정 추적

        /// <summary>사용자 수정 여부 (0=자동, 1=수동)</summary>
        public int IsModified
        {
            get => _isModified;
            set => SetProperty(ref _isModified, value);
        }

        /// <summary>메모 (추가 수업 사유 등)</summary>
        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        #endregion

        #region Computed Properties

        /// <summary>주차 표시</summary>
        public string WeekDisplay => $"{Week}주차";

        /// <summary>기간 표시</summary>
        public string DateRangeDisplay => $"{WeekStartDate} ~ {WeekEndDate}";

        /// <summary>시수 차이 (계획 - 자동계산)</summary>
        public int HoursDiff => PlannedHours - AutoCalculatedHours;

        /// <summary>사용자 수정 여부 (bool)</summary>
        public bool IsUserModified => IsModified == 1;

        /// <summary>진행률 (%)</summary>
        public double ProgressRate => PlannedHours > 0
            ? (double)ActualHours / PlannedHours * 100
            : 0;

        /// <summary>완료 여부</summary>
        public bool IsCompleted => ActualHours >= PlannedHours && PlannedHours > 0;

        /// <summary>지연 여부</summary>
        public bool IsDelayed => ActualHours < PlannedHours && PlannedHours > 0;

        #endregion

        #region Methods

        public override string ToString()
        {
            return $"{WeekDisplay}: 계획 {PlannedHours}시간 / 실제 {ActualHours}시간";
        }

        #endregion
    }
}
