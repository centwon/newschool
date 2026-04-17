using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewSchool.Models
{
    /// <summary>
    /// 출결 정보 (NEIS 표준)
    /// 학생의 일별 출결 상태 관리
    /// </summary>
    public class Attendance : NotifyPropertyChangedBase, IEntity, IYearSemesterEntity
    {
        #region Fields

        private int _no = -1;
        private string _studentId = string.Empty;
        private string _schoolCode = string.Empty;
        private int _year = DateTime.Today.Year;
        private int _semester = 1;
        private string _date = string.Empty;
        private int _period = 0;
        private string _status = AttendanceStatus.Present;
        private string _reason = string.Empty;
        private string _teacherId = string.Empty;
        private string _memo = string.Empty;
        private DateTime _createdAt = DateTime.Now;
        private DateTime _updatedAt = DateTime.Now;
        private bool _isDeleted = false;

        #endregion

        #region Properties - 기본 정보

        /// <summary>PK (자동 증가)</summary>
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        /// <summary>학생 ID (FK: Student.StudentID)</summary>
        public string StudentID
        {
            get => _studentId;
            set => SetProperty(ref _studentId, value);
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

        /// <summary>출결 날짜 (yyyy-MM-dd)</summary>
        public string Date
        {
            get => _date;
            set => SetProperty(ref _date, value);
        }

        /// <summary>교시 (0=종일, 1~9=해당 교시)</summary>
        public int Period
        {
            get => _period;
            set => SetProperty(ref _period, value);
        }

        #endregion

        #region Properties - 출결 상태

        /// <summary>
        /// 출결 상태
        /// 출석/지각/조퇴/결석/결과/질병/기타
        /// </summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>사유 (간단한 설명)</summary>
        public string Reason
        {
            get => _reason;
            set => SetProperty(ref _reason, value);
        }

        /// <summary>기록 교사 ID (FK: Teacher.TeacherID)</summary>
        public string TeacherID
        {
            get => _teacherId;
            set => SetProperty(ref _teacherId, value);
        }

        /// <summary>상세 메모</summary>
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

        /// <summary>논리 삭제 플래그</summary>
        public bool IsDeleted
        {
            get => _isDeleted;
            set => SetProperty(ref _isDeleted, value);
        }

        #endregion

        #region Methods

        public override string ToString()
        {
            return $"{Date} {(Period == 0 ? "종일" : $"{Period}교시")} - {Status}";
        }

        /// <summary>
        /// 결석 여부 확인
        /// </summary>
        public bool IsAbsent()
        {
            return Status == AttendanceStatus.Absent || Status == AttendanceStatus.Excused || Status == AttendanceStatus.Illness;
        }

        /// <summary>
        /// 지각/조퇴 여부 확인
        /// </summary>
        public bool IsTardy()
        {
            return Status == AttendanceStatus.Tardy || Status == AttendanceStatus.EarlyLeave;
        }

        /// <summary>
        /// 정상 출석 여부 확인
        /// </summary>
        public bool IsPresent()
        {
            return Status == AttendanceStatus.Present;
        }

        /// <summary>
        /// 날짜 파싱 헬퍼
        /// </summary>
        public DateTime? GetDateTime()
        {
            if (DateTime.TryParse(Date, out var result))
            {
                return result;
            }
            return null;
        }

        #endregion
    }
}
