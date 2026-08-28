using System;
using Microsoft.UI.Xaml.Data;

namespace NewSchool.Models
{
    /// <summary>
    /// 학적 — 어느 학년도에 이 학생이 이 학교 어느 반 몇 번이었는가.
    ///
    /// <para><c>UNIQUE(StudentID, SchoolCode, Year)</c> — 학생 한 명에 학년도당 한 줄이다.
    /// 학기는 두지 않는다: 학급 편성은 한 학년도 내내 유지되고, 학기별 구분이 필요한 것은
    /// 명부가 아니라 기록 쪽이다(<c>StudentLog.Semester</c> 등).</para>
    ///
    /// <para><b>이름·성별·사진은 이 테이블의 컬럼이 아니다.</b> 정본은 <c>Student</c> 이고,
    /// 조회할 때 JOIN 으로 채워 넣는 읽기 전용 값이다 — 아래 해당 속성 주석 참고.</para>
    ///
    /// <para>설계 근거와 이 표가 23열에서 12열로 줄어든 경위는
    /// <c>docs/enrollment-redesign.md</c> 에 있다.</para>
    /// </summary>
    [Microsoft.UI.Xaml.Data.Bindable]
    public class Enrollment : NotifyPropertyChangedBase
    {
        #region Fields

        private int _no = -1;
        private string _studentId = string.Empty;
        private string _schoolCode = string.Empty;
        private int _year = DateTime.Today.Year;
        private int _grade = 1;
        private int _class = 1;
        private int _number = 1;
        private bool _isActive = true;
        private string _changeType = EnrollmentChange.Admitted;
        private string _changeDate = string.Empty;
        private string _memo = string.Empty;
        private string _teacherId = string.Empty;

        // JOIN 으로 채워지는 값 (컬럼 아님)
        private string _name = string.Empty;
        private string _sex = string.Empty;
        private string _photo = string.Empty;

        #endregion

        #region Properties - 키

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

        /// <summary>학교 코드 (FK: School.SchoolCode). 교사가 학교를 옮겨도 작년 학적이 어느 학교 것인지 남는다.</summary>
        public string SchoolCode
        {
            get => _schoolCode;
            set => SetProperty(ref _schoolCode, value);
        }

        public int Year
        {
            get => _year;
            set => SetProperty(ref _year, value);
        }

        #endregion

        #region Properties - 학급 배정

        public int Grade
        {
            get => _grade;
            set => SetProperty(ref _grade, value);
        }

        public int Class
        {
            get => _class;
            set => SetProperty(ref _class, value);
        }

        public int Number
        {
            get => _number;
            set => SetProperty(ref _number, value);
        }

        /// <summary>담임교사 ID (FK: Teacher.TeacherID). 다중 사용자 대비.</summary>
        public string TeacherID
        {
            get => _teacherId;
            set => SetProperty(ref _teacherId, value);
        }

        #endregion

        #region Properties - 학적 변동

        /// <summary>
        /// 이 학생이 <b>명단에 들어가는가</b>. 명렬표·좌석·수업·동아리가 모두 이것으로 거른다.
        ///
        /// <para>파생 가능한 값이지만 컬럼으로 둔다 — SQL 의 <c>WHERE IsActive=1</c> 이
        /// 상태 목록을 나열하는 것보다 안정적이고 인덱스도 타기 때문이다.</para>
        ///
        /// <para>⚠ 손으로 넣지 말 것. <see cref="ApplyChange"/> 로만 바꾼다 — 그래야
        /// <see cref="ChangeType"/> 과 갈라지지 않는다.</para>
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            private set => SetProperty(ref _isActive, value);
        }

        /// <summary>
        /// 학적 변동 — 입학·진급·전입·전출·졸업·휴학·유예·정원외·자퇴·퇴학
        /// (<see cref="EnrollmentChange"/>).
        ///
        /// <para>⚠ 이 속성에 값을 넣으면 <see cref="IsActive"/> 도 함께 맞춰진다.</para>
        /// </summary>
        public string ChangeType
        {
            get => _changeType;
            set
            {
                if (SetProperty(ref _changeType, value))
                    IsActive = EnrollmentChange.IsActive(value);
            }
        }

        /// <summary>
        /// 변동 일자 (yyyy-MM-dd). 입학·전입이면 학적이 시작된 날, 전출·졸업이면 끝난 날이다.
        ///
        /// <para>전출 뒤에 그 학생 기록을 남기려 하면 이 날짜를 기준으로 경고한다.</para>
        /// </summary>
        public string ChangeDate
        {
            get => _changeDate;
            set => SetProperty(ref _changeDate, value);
        }

        /// <summary>메모. 전입·전출 학교명이나 사유를 적는 자리이기도 하다.</summary>
        public string Memo
        {
            get => _memo;
            set => SetProperty(ref _memo, value);
        }

        /// <summary>
        /// 학적 변동을 적용한다 — <see cref="ChangeType"/>·<see cref="ChangeDate"/> 와
        /// <see cref="IsActive"/> 를 한 번에 맞춘다. 이 경로로만 바꿔야 세 값이 어긋나지 않는다.
        /// </summary>
        public void ApplyChange(string changeType, string? changeDate = null)
        {
            ChangeType = changeType;                       // IsActive 가 따라온다
            if (changeDate != null) ChangeDate = changeDate;
        }

        #endregion

        #region Properties - Student 에서 JOIN 으로 채워지는 값

        /// <summary>
        /// 학생 이름. <b>이 테이블의 컬럼이 아니다</b> — 정본은 <c>Student.Name</c> 이고
        /// 조회 시 JOIN 으로 채운다. 여기에 값을 넣어도 저장되지 않는다.
        ///
        /// <para>예전에는 진짜 컬럼이었고(명렬표 성능), 그래서 학생 이름을 고칠 때마다
        /// 학적 쪽을 따라 고쳐야 했다. 173행에 JOIN 은 공짜라 사본을 없앴다.</para>
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>학생 성별. <see cref="Name"/> 과 같이 JOIN 으로 채워지는 값이다.</summary>
        public string Sex
        {
            get => _sex;
            set => SetProperty(ref _sex, value);
        }

        /// <summary>학생 사진 경로. <see cref="Name"/> 과 같이 JOIN 으로 채워지는 값이다.</summary>
        public string Photo
        {
            get => _photo;
            set => SetProperty(ref _photo, value);
        }

        #endregion

        #region Methods

        public override string ToString() =>
            string.IsNullOrEmpty(Name)
                ? $"{Year}년 {GetClassInfo()} - {StudentID}"
                : $"{Year}년 {GetClassInfo()} - {Name} ({StudentID})";

        /// <summary>학급 표시 문자열 (학년-반-번호)</summary>
        public string GetClassInfo() => $"{Grade}학년 {Class}반 {Number}번";

        #endregion
    }
}
