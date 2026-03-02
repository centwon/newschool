using System;
using System.Collections.Generic;
using System.Linq;

namespace NewSchool.Models
{
    /// <summary>
    /// 학급 일지 모델
    /// 출결, 메모, 알림장, 학생 생활 기록 관리
    /// </summary>
    public class ClassDiary : NotifyPropertyChangedBase, IDailyRecord
    {
        #region Fields - 기본 정보

        private int _no = -1;
        private string _schoolCode = string.Empty;
        private string _teacherId = string.Empty;
        private int _year = DateTime.Today.Year;
        private int _semester = 1;
        private DateTime _date = DateTime.Today;
        private int _grade;
        private int _class;

        #endregion

        #region Fields - 출결 정보

        private string _absent = string.Empty;
        private string _late = string.Empty;
        private string _leaveEarly = string.Empty;

        #endregion

        #region Fields - 기록 내용

        private string _memo = string.Empty;
        private string _notice = string.Empty;
        private string _life = string.Empty;

        #endregion

        #region Fields - 메타 정보

        private DateTime _createdAt = DateTime.Now;
        private DateTime _updatedAt = DateTime.Now;

        #endregion

        #region Properties - IEntity

        /// <summary>PK (자동 증가)</summary>
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        #endregion

        #region Properties - 기본 정보

        /// <summary>학교 코드 (FK: School.SchoolCode)</summary>
        public string SchoolCode
        {
            get => _schoolCode;
            set => SetProperty(ref _schoolCode, value);
        }

        /// <summary>작성 교사 ID (FK: Teacher.TeacherID)</summary>
        public string TeacherID
        {
            get => _teacherId;
            set => SetProperty(ref _teacherId, value);
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

        /// <summary>날짜</summary>
        public DateTime Date
        {
            get => _date;
            set => SetProperty(ref _date, value.Date); // 시간 부분 제거, 날짜만 저장
        }

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

        #region Properties - 출결 정보

        /// <summary>결석 학생 (쉼표 구분)</summary>
        public string Absent
        {
            get => _absent;
            set => SetProperty(ref _absent, value ?? string.Empty);
        }

        /// <summary>지각 학생 (쉼표 구분)</summary>
        public string Late
        {
            get => _late;
            set => SetProperty(ref _late, value ?? string.Empty);
        }

        /// <summary>조퇴 학생 (쉼표 구분)</summary>
        public string LeaveEarly
        {
            get => _leaveEarly;
            set => SetProperty(ref _leaveEarly, value ?? string.Empty);
        }

        #endregion

        #region Properties - 기록 내용

        /// <summary>메모</summary>
        public string Memo
        {
            get => _memo;
            set => SetProperty(ref _memo, value ?? string.Empty);
        }

        /// <summary>알림장 (HTML 또는 텍스트)</summary>
        public string Notice
        {
            get => _notice;
            set => SetProperty(ref _notice, value ?? string.Empty);
        }

        /// <summary>학생 생활 기록 (StudentLog에서 자동 생성)</summary>
        public string Life
        {
            get => _life;
            set => SetProperty(ref _life, value ?? string.Empty);
        }

        #endregion

        #region Computed Properties

        /// <summary>
        /// 요일
        /// </summary>
        public DayOfWeek DayOfWeek => Date.DayOfWeek;

        /// <summary>
        /// 요일 (한글)
        /// </summary>
        public string DayOfWeekKorean => Date.ToString("ddd");

        /// <summary>
        /// 날짜 표시 (yyyy년 M월 d일 (요일))
        /// </summary>
        public string DateDisplay => $"{Date:yyyy년 M월 d일} ({DayOfWeekKorean})";

        /// <summary>
        /// 출결 문제가 있는지 확인
        /// </summary>
        public bool HasAttendanceIssues =>
            !string.IsNullOrWhiteSpace(Absent) ||
            !string.IsNullOrWhiteSpace(Late) ||
            !string.IsNullOrWhiteSpace(LeaveEarly);

        /// <summary>
        /// 출결 요약
        /// </summary>
        public string AttendanceSummary
        {
            get
            {
                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(Absent))
                    parts.Add($"결석: {Absent}");

                if (!string.IsNullOrWhiteSpace(Late))
                    parts.Add($"지각: {Late}");

                if (!string.IsNullOrWhiteSpace(LeaveEarly))
                    parts.Add($"조퇴: {LeaveEarly}");

                return parts.Count > 0 ? string.Join(", ", parts) : "출결 이상 없음";
            }
        }

        /// <summary>
        /// 출결 학생 수 (중복 제거)
        /// </summary>
        public int AttendanceIssueCount
        {
            get
            {
                var students = new HashSet<string>();

                if (!string.IsNullOrWhiteSpace(Absent))
                    students.UnionWith(Absent.Split(',').Select(s => s.Trim()));

                if (!string.IsNullOrWhiteSpace(Late))
                    students.UnionWith(Late.Split(',').Select(s => s.Trim()));

                if (!string.IsNullOrWhiteSpace(LeaveEarly))
                    students.UnionWith(LeaveEarly.Split(',').Select(s => s.Trim()));

                return students.Count;
            }
        }

        /// <summary>
        /// 알림장 내용이 있는지 확인
        /// </summary>
        public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

        /// <summary>
        /// 학생 생활 기록이 있는지 확인
        /// </summary>
        public bool HasLifeRecord => !string.IsNullOrWhiteSpace(Life);

        /// <summary>
        /// 메모가 있는지 확인
        /// </summary>
        public bool HasMemo => !string.IsNullOrWhiteSpace(Memo);

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

        /// <summary>
        /// 기본 생성자
        /// </summary>
        public ClassDiary()
        {
        }

        /// <summary>
        /// 학급과 날짜를 지정하는 생성자
        /// </summary>
        public ClassDiary(string schoolCode, int year, int semester, int grade, int classNum, DateTime date, string teacherId)
        {
            SchoolCode = schoolCode;
            Year = year;
            Semester = semester;
            Grade = grade;
            Class = classNum;
            Date = date;
            TeacherID = teacherId;
        }

        /// <summary>
        /// 출결 정보 초기화
        /// </summary>
        public void ClearAttendance()
        {
            Absent = string.Empty;
            Late = string.Empty;
            LeaveEarly = string.Empty;
        }

        /// <summary>
        /// 모든 기록 초기화
        /// </summary>
        public void ClearAll()
        {
            ClearAttendance();
            Memo = string.Empty;
            Notice = string.Empty;
            Life = string.Empty;
        }

        /// <summary>
        /// 유효성 검사
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(SchoolCode) &&
                   !string.IsNullOrWhiteSpace(TeacherID) &&
                   Year > 0 &&
                   Semester > 0 &&
                   Grade > 0 &&
                   Class > 0 &&
                   Date != default;
        }

        /// <summary>
        /// 특정 학생이 출결 문제가 있는지 확인
        /// </summary>
        public bool HasAttendanceIssue(string studentName)
        {
            if (string.IsNullOrWhiteSpace(studentName))
                return false;

            return Absent?.Contains(studentName) == true ||
                   Late?.Contains(studentName) == true ||
                   LeaveEarly?.Contains(studentName) == true;
        }

        /// <summary>
        /// 학생의 출결 상태 반환
        /// </summary>
        public string GetAttendanceStatus(string studentName)
        {
            if (string.IsNullOrWhiteSpace(studentName))
                return "정상";

            var statuses = new List<string>();

            if (Absent?.Contains(studentName) == true)
                statuses.Add("결석");

            if (Late?.Contains(studentName) == true)
                statuses.Add("지각");

            if (LeaveEarly?.Contains(studentName) == true)
                statuses.Add("조퇴");

            return statuses.Count > 0 ? string.Join(", ", statuses) : "정상";
        }

        /// <summary>
        /// 복사본 생성
        /// </summary>
        public ClassDiary Clone()
        {
            return new ClassDiary
            {
                No = this.No,
                SchoolCode = this.SchoolCode,
                TeacherID = this.TeacherID,
                Year = this.Year,
                Semester = this.Semester,
                Date = this.Date,
                Grade = this.Grade,
                Class = this.Class,
                Absent = this.Absent,
                Late = this.Late,
                LeaveEarly = this.LeaveEarly,
                Memo = this.Memo,
                Notice = this.Notice,
                Life = this.Life,
                CreatedAt = this.CreatedAt,
                UpdatedAt = this.UpdatedAt
            };
        }

        /// <summary>
        /// ToString 오버라이드
        /// </summary>
        public override string ToString()
        {
            return $"{Year}학년도 {Semester}학기 {Grade}학년 {Class}반 - {DateDisplay}";
        }

        #endregion
    }
}
