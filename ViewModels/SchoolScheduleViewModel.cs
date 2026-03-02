using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NewSchool.Models;

namespace NewSchool.ViewModels
{
    /// <summary>
    /// 학사일정 ViewModel (UI 바인딩용)
    /// ListView에서 사용
    /// </summary>
    public class SchoolScheduleViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isModified;
        private bool _isInitializing = true; // 초기화 중인지 표시
        
        // 속성별 독립적인 호출 카운터 (Dictionary 사용)
        private readonly Dictionary<string, int> _propertyCallCounts = new Dictionary<string, int>();
        
        // 초기 값 저장 (비교용)
        private readonly string _originalEventNm;
        private readonly string _originalEventCntnt;
        private readonly string _originalSbtrDdScNm;
        private readonly bool _originalOneGrade;
        private readonly bool _originalTwGrade;
        private readonly bool _originalThreeGrade;
        private readonly bool _originalFrGrade;
        private readonly bool _originalFivGrade;
        private readonly bool _originalSixGrade;

        public SchoolScheduleViewModel(SchoolSchedule schedule)
        {
            Schedule = schedule;
            
            // 초기 값 저장
            _originalEventNm = schedule.EVENT_NM;
            _originalEventCntnt = schedule.EVENT_CNTNT;
            _originalSbtrDdScNm = schedule.SBTR_DD_SC_NM;
            _originalOneGrade = schedule.ONE_GRADE_EVENT_YN;
            _originalTwGrade = schedule.TW_GRADE_EVENT_YN;
            _originalThreeGrade = schedule.THREE_GRADE_EVENT_YN;
            _originalFrGrade = schedule.FR_GRADE_EVENT_YN;
            _originalFivGrade = schedule.FIV_GRADE_EVENT_YN;
            _originalSixGrade = schedule.SIX_GRADE_EVENT_YN;
        }
        
        /// <summary>
        /// 초기화 완료 후 호출 (UI 바인딩 완료 후)
        /// </summary>
        public void CompleteInitialization()
        {
            _isInitializing = false;
            System.Diagnostics.Debug.WriteLine($"[CompleteInitialization] {Schedule.EVENT_NM} - 변경 감지 활성화");
        }

        /// <summary>
        /// 원본 SchoolSchedule 모델
        /// </summary>
        public SchoolSchedule Schedule { get; }

        // ==========================================
        // UI 전용 속성
        // ==========================================

        /// <summary>
        /// 체크박스 선택 여부
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 수정 여부 (자동 선택용)
        /// </summary>
        public bool IsModified
        {
            get => _isModified;
            set
            {
                if (_isModified != value)
                {
                    _isModified = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ModifiedIcon));
                    
                    // 수정되면 자동으로 선택
                    if (value)
                    {
                        IsSelected = true;
                    }
                }
            }
        }

        /// <summary>
        /// 수정 표시 (아이콘)
        /// </summary>
        public string ModifiedIcon => IsModified ? "✏️" : "";

        /// <summary>
        /// 실제 수정되었는지 확인 (초기 값과 비교)
        /// </summary>
        private void CheckIfModified(string propertyName)
        {
            // 초기화 중이면 수정 감지 안함
            if (_isInitializing)
            {
                return;
            }
            
            // 문자열 비교는 null 처리 필요
            bool eventNmChanged = !string.Equals(Schedule.EVENT_NM ?? "", _originalEventNm ?? "", StringComparison.Ordinal);
            bool eventCntntChanged = !string.Equals(Schedule.EVENT_CNTNT ?? "", _originalEventCntnt ?? "", StringComparison.Ordinal);
            bool sbtrDdScNmChanged = !string.Equals(Schedule.SBTR_DD_SC_NM ?? "", _originalSbtrDdScNm ?? "", StringComparison.Ordinal);
            
            bool hasChanges = 
                eventNmChanged ||
                eventCntntChanged ||
                sbtrDdScNmChanged ||
                Schedule.ONE_GRADE_EVENT_YN != _originalOneGrade ||
                Schedule.TW_GRADE_EVENT_YN != _originalTwGrade ||
                Schedule.THREE_GRADE_EVENT_YN != _originalThreeGrade ||
                Schedule.FR_GRADE_EVENT_YN != _originalFrGrade ||
                Schedule.FIV_GRADE_EVENT_YN != _originalFivGrade ||
                Schedule.SIX_GRADE_EVENT_YN != _originalSixGrade;

            if (hasChanges && !IsModified)
            {
                System.Diagnostics.Debug.WriteLine($"[학사일정] 수정 감지 ({propertyName}): {Schedule.EVENT_NM}");
                IsModified = true;
            }
        }

        /// <summary>
        /// 표시용 날짜 (yyyy.MM.dd)
        /// </summary>
        public string DisplayDate => Schedule.AA_YMD.ToString("yyyy.MM.dd");

        /// <summary>
        /// 표시용 요일
        /// </summary>
        public string DisplayDayOfWeek
        {
            get
            {
                var dayNames = new[] { "일", "월", "화", "수", "목", "금", "토" };
                return dayNames[(int)Schedule.AA_YMD.DayOfWeek];
            }
        }

        /// <summary>
        /// 날짜 + 요일 (yyyy.MM.dd (월))
        /// </summary>
        public string DisplayDateWithDay => $"{DisplayDate} ({DisplayDayOfWeek})";

        /// <summary>
        /// 대상 학년 텍스트 (1,2,3학년 또는 전체)
        /// </summary>
        public string GradeTargetText => Schedule.GetTargetGradesText();

        /// <summary>
        /// 휴일 여부 (휴업일 또는 공휴일)
        /// </summary>
        public bool IsHoliday => Schedule.IsHoliday;

        /// <summary>
        /// 휴업일 여부
        /// </summary>
        public bool IsClosedDay => Schedule.SBTR_DD_SC_NM == "휴업일";

        /// <summary>
        /// 공휴일 여부
        /// </summary>
        public bool IsPublicHoliday => Schedule.SBTR_DD_SC_NM == "공휴일";

        /// <summary>
        /// 수동 입력 여부 아이콘
        /// </summary>
        public string ManualIcon => Schedule.IsManual ? "✏️" : "";

        /// <summary>
        /// 수업공제일 선택 항목 (ComboBox용)
        /// </summary>
        public string[] SubtractionDayOptions => new[] { "해당없음", "휴업일", "공휴일" };

        // ==========================================
        // Schedule 속성 바로 접근 (간편성)
        // ==========================================

        public int No => Schedule.No;
        public int AY => Schedule.AY;
        public DateTime AA_YMD => Schedule.AA_YMD;
        
        public string EVENT_NM
        {
            get => Schedule.EVENT_NM;
            set
            {
                if (Schedule.EVENT_NM != value)
                {
                    Schedule.EVENT_NM = value;
                    OnPropertyChanged();
                    CheckIfModified(nameof(EVENT_NM)); // 수정 여부 확인
                }
            }
        }
        
        public string EVENT_CNTNT
        {
            get => Schedule.EVENT_CNTNT;
            set
            {
                if (Schedule.EVENT_CNTNT != value)
                {
                    Schedule.EVENT_CNTNT = value;
                    OnPropertyChanged();
                    CheckIfModified(nameof(EVENT_CNTNT)); // 수정 여부 확인
                }
            }
        }
        
        public string SBTR_DD_SC_NM
        {
            get => Schedule.SBTR_DD_SC_NM;
            set
            {
                try
                {
                    // value가 null이면 빈 문자열로 처리
                    string newValue = value ?? string.Empty;
                    
                    // 같은 값이면 조기 반환 (반복 호출 방지)
                    if (Schedule.SBTR_DD_SC_NM == newValue)
                        return;
                    
                    Schedule.SBTR_DD_SC_NM = newValue;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsHoliday));
                    OnPropertyChanged(nameof(IsClosedDay));
                    OnPropertyChanged(nameof(IsPublicHoliday));
                    CheckIfModified(nameof(SBTR_DD_SC_NM)); // 수정 여부 확인
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SBTR_DD_SC_NM setter] 예외 발생: {ex.GetType().Name} - {ex.Message}");
                    throw;
                }
            }
        }

        public bool ONE_GRADE_EVENT_YN
        {
            get => Schedule.ONE_GRADE_EVENT_YN;
            set
            {
                if (Schedule.ONE_GRADE_EVENT_YN != value)
                {
                    Schedule.ONE_GRADE_EVENT_YN = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GradeTargetText));
                    CheckIfModified(nameof(ONE_GRADE_EVENT_YN));
                }
            }
        }

        public bool TW_GRADE_EVENT_YN
        {
            get => Schedule.TW_GRADE_EVENT_YN;
            set
            {
                if (Schedule.TW_GRADE_EVENT_YN != value)
                {
                    Schedule.TW_GRADE_EVENT_YN = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GradeTargetText));
                    CheckIfModified(nameof(TW_GRADE_EVENT_YN));
                }
            }
        }

        public bool THREE_GRADE_EVENT_YN
        {
            get => Schedule.THREE_GRADE_EVENT_YN;
            set
            {
                if (Schedule.THREE_GRADE_EVENT_YN != value)
                {
                    Schedule.THREE_GRADE_EVENT_YN = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GradeTargetText));
                    CheckIfModified(nameof(THREE_GRADE_EVENT_YN));
                }
            }
        }

        public bool FR_GRADE_EVENT_YN
        {
            get => Schedule.FR_GRADE_EVENT_YN;
            set
            {
                if (Schedule.FR_GRADE_EVENT_YN != value)
                {
                    Schedule.FR_GRADE_EVENT_YN = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GradeTargetText));
                    CheckIfModified(nameof(FR_GRADE_EVENT_YN));
                }
            }
        }

        public bool FIV_GRADE_EVENT_YN
        {
            get => Schedule.FIV_GRADE_EVENT_YN;
            set
            {
                if (Schedule.FIV_GRADE_EVENT_YN != value)
                {
                    Schedule.FIV_GRADE_EVENT_YN = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GradeTargetText));
                    CheckIfModified(nameof(FIV_GRADE_EVENT_YN));
                }
            }
        }

        public bool SIX_GRADE_EVENT_YN
        {
            get => Schedule.SIX_GRADE_EVENT_YN;
            set
            {
                if (Schedule.SIX_GRADE_EVENT_YN != value)
                {
                    Schedule.SIX_GRADE_EVENT_YN = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GradeTargetText));
                    CheckIfModified(nameof(SIX_GRADE_EVENT_YN));
                }
            }
        }

        public bool IsManual => Schedule.IsManual;

        // ==========================================
        // INotifyPropertyChanged 구현
        // ==========================================

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // ==========================================
        // Helper Methods
        // ==========================================

        /// <summary>
        /// 수정 상태 초기화
        /// </summary>
        public void ResetModified()
        {
            IsModified = false;
        }

        /// <summary>
        /// 학사일정 복사 (새 행 추가시 템플릿으로 사용)
        /// </summary>
        public SchoolScheduleViewModel Clone()
        {
            var newSchedule = new SchoolSchedule
            {
                No = 0, // 새 항목
                SCHUL_NM = Schedule.SCHUL_NM,
                ATPT_OFCDC_SC_CODE = Schedule.ATPT_OFCDC_SC_CODE,
                ATPT_OFCDC_SC_NM = Schedule.ATPT_OFCDC_SC_NM,
                SD_SCHUL_CODE = Schedule.SD_SCHUL_CODE,
                AY = Schedule.AY,
                AA_YMD = DateTime.Today,
                EVENT_NM = "새 일정",
                EVENT_CNTNT = "",
                SBTR_DD_SC_NM = "해당없음",
                IsManual = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            return new SchoolScheduleViewModel(newSchedule);
        }

        public override string ToString()
        {
            return $"{DisplayDate} {EVENT_NM} ({GradeTargetText})";
        }
    }
}
