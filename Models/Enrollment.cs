using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Data;

namespace NewSchool.Models
{
    /// <summary>
    /// 학적 정보 (NEIS 표준)
    /// 기존 ClassAssignment 대체 - A안의 핵심 테이블!
    /// 학생의 학교, 학년, 반, 번호 정보 관리
    /// UNIQUE(StudentID, SchoolCode, Year, Semester)
    /// 
    /// ※ 성능 최적화: 명렬표 조회를 위해 Student의 Name, Sex, Photo를 denormalize
    /// WinUI3 x:Bind를 위한 Bindable 특성 추가
    /// </summary>
    [Microsoft.UI.Xaml.Data.Bindable]
    public class Enrollment : NotifyPropertyChangedBase, IEntity
    {
        #region Fields

        private int _no = -1;
        private string _studentId = string.Empty;
        private string _name = string.Empty;
        private string _sex = string.Empty;
        private string _photo = string.Empty;
        private string _schoolCode = string.Empty;
        private int _year = DateTime.Today.Year;
        private int _semester = 1;
        private int _grade = 1;
        private int _class = 1;
        private int _number = 1;
        private string _status = "재학";
        private string _teacherId = string.Empty;
        private string _admissionDate = string.Empty;
        private string _graduationDate = string.Empty;
        private string _transferOutDate = string.Empty;
        private string _transferOutSchool = string.Empty;
        private string _transferInDate = string.Empty;
        private string _transferInSchool = string.Empty;
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

        #endregion

        #region Properties - 학생 기본 정보 (denormalized from Student)

        /// <summary>
        /// 학생명 (denormalized: Student.Name)
        /// 명렬표 조회 성능 최적화를 위해 복제
        /// Student.Name 변경 시 동기화 필요
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// 성별 (denormalized: Student.Sex)
        /// 남녀 분리 명렬표 작성용
        /// 값: "남" 또는 "여"
        /// </summary>
        public string Sex
        {
            get => _sex;
            set => SetProperty(ref _sex, value);
        }

        /// <summary>
        /// 증명사진 경로 (denormalized: Student.Photo)
        /// 사진 명렬표 출력용
        /// </summary>
        public string Photo
        {
            get => _photo;
            set => SetProperty(ref _photo, value);
        }

        #endregion

        #region Properties - 학교 정보

        /// <summary>학교 코드 (FK: School.SchoolCode)</summary>
        public string SchoolCode
        {
            get => _schoolCode;
            set => SetProperty(ref _schoolCode, value);
        }

        /// <summary>학년도 (예: 2024)</summary>
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

        #region Properties - 학급 배정

        /// <summary>학년 (1, 2, 3)</summary>
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

        /// <summary>번호</summary>
        public int Number
        {
            get => _number;
            set => SetProperty(ref _number, value);
        }

        #endregion

        #region Properties - 학적 상태

        /// <summary>
        /// 학적 상태
        /// 재학/휴학/전학(전출)/전학(전입)/졸업/자퇴/퇴학
        /// </summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>담임교사 ID(FK: Teacher.TeacherID)</summary>
        public string TeacherID
        {
            get => _teacherId;
            set => SetProperty(ref _teacherId, value);
        }

        #endregion

        #region Properties - 입학/졸업 정보

        /// <summary>입학일 (yyyy-MM-dd)</summary>
        public string AdmissionDate
        {
            get => _admissionDate;
            set => SetProperty(ref _admissionDate, value);
        }

        /// <summary>졸업일 (yyyy-MM-dd)</summary>
        public string GraduationDate
        {
            get => _graduationDate;
            set => SetProperty(ref _graduationDate, value);
        }

        #endregion

        #region Properties - 전학 정보

        /// <summary>전학(전출)일 (yyyy-MM-dd)</summary>
        public string TransferOutDate
        {
            get => _transferOutDate;
            set => SetProperty(ref _transferOutDate, value);
        }

        /// <summary>전출 학교명</summary>
        public string TransferOutSchool
        {
            get => _transferOutSchool;
            set => SetProperty(ref _transferOutSchool, value);
        }

        /// <summary>전학(전입)일 (yyyy-MM-dd)</summary>
        public string TransferInDate
        {
            get => _transferInDate;
            set => SetProperty(ref _transferInDate, value);
        }

        /// <summary>전입 학교명</summary>
        public string TransferInSchool
        {
            get => _transferInSchool;
            set => SetProperty(ref _transferInSchool, value);
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
            // 이름이 있으면 이름 포함 (가독성 향상)
            if (!string.IsNullOrEmpty(Name))
                return $"{Year}년 {GetClassInfo()} - {Name} ({StudentID})";
            
            return $"{Year}년 {GetClassInfo()} - {StudentID}";
        }

        /// <summary>
        /// 학급 표시 문자열 (학년-반-번호)
        /// </summary>
        public string GetClassInfo()
        {
            return $"{Grade}학년 {Class}반 {Number}번";
        }

        /// <summary>
        /// 명렬표용 전체 정보 문자열
        /// 예: "1학년 1반 1번 홍길동 (남)"
        /// </summary>
        public string GetRosterInfo()
        {
            if (!string.IsNullOrEmpty(Name))
                return $"{GetClassInfo()} {Name} ({Sex})";
            
            return GetClassInfo();
        }

        /// <summary>
        /// 상태 확인 메서드들
        /// </summary>
        public bool IsCurrentlyEnrolled() => Status == EnrollmentStatus.Enrolled;
        public bool IsOnLeave() => Status == EnrollmentStatus.OnLeave;
        public bool IsGraduated() => Status == EnrollmentStatus.Graduated;
        public bool IsTransferred() => Status.Contains(EnrollmentStatus.Transferred);

        /// <summary>
        /// 재학 기간 계산
        /// </summary>
        public int GetEnrollmentDuration()
        {
            if (string.IsNullOrEmpty(AdmissionDate))
                return 0;

            if (!DateTime.TryParse(AdmissionDate, out DateTime admission))
                return 0;

            DateTime endDate;
            if (!string.IsNullOrEmpty(GraduationDate) && DateTime.TryParse(GraduationDate, out DateTime grad))
            {
                endDate = grad;
            }
            else if (!string.IsNullOrEmpty(TransferOutDate) && DateTime.TryParse(TransferOutDate, out DateTime transfer))
            {
                endDate = transfer;
            }
            else
            {
                endDate = DateTime.Today;
            }

            return (int)((endDate - admission).TotalDays / 365.25);
        }

        #endregion
    }
}
