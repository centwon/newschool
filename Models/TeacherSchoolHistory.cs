using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewSchool.Models
{
    /// <summary>
    /// 교사 근무 이력 (학교별)
    /// 교사가 여러 학교를 옮겨다니는 경우 이력 관리
    /// </summary>
    public class TeacherSchoolHistory : NotifyPropertyChangedBase, IEntity
    {
        #region Fields

        private int _no = -1;
        private string _teacherId = string.Empty;
        private string _schoolCode = string.Empty;
        private string _startDate = string.Empty;
        private string _endDate = string.Empty;
        private bool _isCurrent = false;
        private string _position = string.Empty;
        private string _role = string.Empty;
        private string _memo = string.Empty;
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

        /// <summary>교사 ID (FK: Teacher.TeacherID)</summary>
        public string TeacherID
        {
            get => _teacherId;
            set => SetProperty(ref _teacherId, value);
        }

        /// <summary>학교 코드 (FK: School.SchoolCode)</summary>
        public string SchoolCode
        {
            get => _schoolCode;
            set => SetProperty(ref _schoolCode, value);
        }

        /// <summary>근무 시작일 (yyyy-MM-dd)</summary>
        public string StartDate
        {
            get => _startDate;
            set => SetProperty(ref _startDate, value);
        }

        /// <summary>근무 종료일 (yyyy-MM-dd, 현직이면 비어있음)</summary>
        public string EndDate
        {
            get => _endDate;
            set => SetProperty(ref _endDate, value);
        }

        /// <summary>현재 근무 중인지 여부</summary>
        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetProperty(ref _isCurrent, value);
        }

        #endregion

        #region Properties - 직위 정보

        /// <summary>해당 학교에서의 직위 (교사/부장교사/교감/교장)</summary>
        public string Position
        {
            get => _position;
            set => SetProperty(ref _position, value);
        }

        /// <summary>역할/담당 업무 (예: 1학년 담임, 과학부장 등)</summary>
        public string Role
        {
            get => _role;
            set => SetProperty(ref _role, value);
        }

        #endregion

        #region Properties - 기타

        /// <summary>메모</summary>
        public string Memo
        {
            get => _memo;
            set => SetProperty(ref _memo, value);
        }

        #endregion

        #region Properties - 메타 정보

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

        #region Methods

        public override string ToString()
        {
            return $"{SchoolCode} ({StartDate} ~ {(IsCurrent ? "현재" : EndDate)})";
        }

        /// <summary>
        /// 근무 기간 계산 (년)
        /// </summary>
        public int GetYearsOfService()
        {
            if (!DateTime.TryParse(StartDate, out DateTime start))
                return 0;

            DateTime end = IsCurrent || string.IsNullOrEmpty(EndDate)
                ? DateTime.Today
                : DateTime.TryParse(EndDate, out DateTime parsedEnd)
                    ? parsedEnd
                    : DateTime.Today;

            return (int)((end - start).TotalDays / 365.25);
        }

        /// <summary>
        /// 현재 근무 중인지 확인
        /// </summary>
        public bool IsActiveNow()
        {
            return IsCurrent && string.IsNullOrEmpty(EndDate);
        }

        #endregion
    }
}
