using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewSchool.Models
{
    /// <summary>
    /// 학생 기본 정보 (NEIS 표준)
    /// 순수 인적 정보만 포함 (학적 정보는 Enrollment, 상세 정보는 StudentDetail)
    /// </summary>
    public class Student : NotifyPropertyChangedBase, IEntity
    {
        #region Fields

        private int _no = -1;
        private string _studentId = string.Empty;
        private string _name = string.Empty;
        private string _sex = string.Empty;
        private DateTime? _birthDate = null;
        private string _residentNumber = string.Empty;
        private string _photo = string.Empty;
        private string _phone = string.Empty;
        private string _email = string.Empty;
        private string _address = string.Empty;
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

        /// <summary>
        /// 학생 고유 ID (전국 단위 고유 식별자)
        /// 형식: 학교코드(7) + 입학년도(4) + 일련번호(4) = 15자리
        /// 예: 7001234202400001
        /// UNIQUE, NOT NULL
        /// </summary>
        public string StudentID
        {
            get => _studentId;
            set => SetProperty(ref _studentId, value);
        }

        /// <summary>학생명</summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>성별 (남/여)</summary>
        public string Sex
        {
            get => _sex;
            set => SetProperty(ref _sex, value);
        }

        /// <summary>생년월일 (yyyy-MM-dd)</summary>
        public DateTime? BirthDate
        {
            get => _birthDate;
            set => SetProperty(ref _birthDate, value);
        }

        /// <summary>주민등록번호 (암호화 필요)</summary>
        public string ResidentNumber
        {
            get => _residentNumber;
            set => SetProperty(ref _residentNumber, value);
        }

        /// <summary>증명사진 경로</summary>
        public string Photo
        {
            get => _photo;
            set => SetProperty(ref _photo, value);
        }

        #endregion

        #region Properties - 연락처

        /// <summary>학생 전화번호 (선택)</summary>
        public string Phone
        {
            get => _phone;
            set => SetProperty(ref _phone, value);
        }

        /// <summary>학생 이메일 (선택)</summary>
        public string Email
        {
            get => _email;
            set => SetProperty(ref _email, value);
        }

        /// <summary>주소</summary>
        public string Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        #endregion

        #region Properties - 기타

        /// <summary>메모 (간단한 메모만, 상세는 StudentDetail)</summary>
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
            return $"{Name} ({StudentID})";
        }

        /// <summary>
        /// StudentID 생성 헬퍼
        /// </summary>
        /// <param name="schoolCode">학교 코드 (7자리)</param>
        /// <param name="enrollmentYear">입학 년도 (4자리)</param>
        /// <param name="sequence">일련번호 (4자리)</param>
        /// <returns>15자리 StudentID</returns>
        public static string GenerateStudentID(string schoolCode, int enrollmentYear, int sequence)
        {
            if (schoolCode.Length != 7)
                throw new ArgumentException("학교 코드는 7자리여야 합니다.", nameof(schoolCode));

            if (enrollmentYear < 1900 || enrollmentYear > 2100)
                throw new ArgumentException("입학 년도가 유효하지 않습니다.", nameof(enrollmentYear));

            if (sequence < 1 || sequence > 9999)
                throw new ArgumentException("일련번호는 1~9999 사이여야 합니다.", nameof(sequence));

            return $"{schoolCode}{enrollmentYear:D4}{sequence:D4}";
        }

        /// <summary>
        /// StudentID 파싱
        /// </summary>
        /// <returns>(학교코드, 입학년도, 일련번호)</returns>
        public (string SchoolCode, int EnrollmentYear, int Sequence) ParseStudentID()
        {
            if (string.IsNullOrEmpty(StudentID) || StudentID.Length != 15)
                return (string.Empty, 0, 0);

            string schoolCode = StudentID.Substring(0, 7);
            int enrollmentYear = int.Parse(StudentID.Substring(7, 4));
            int sequence = int.Parse(StudentID.Substring(11, 4));

            return (schoolCode, enrollmentYear, sequence);
        }

        /// <summary>
        /// 나이 계산
        /// </summary>
        public int GetAge()
        {
            var today = DateTime.Today;
            if (BirthDate == null)
            {
                return 0;
            }
            else
            {
                var birthday = BirthDate.Value;
                var age = today.Year - birthday.Year;
                if (birthday.Date > today.AddYears(-age))
                {
                    age--;
                    return age;
                }
            }

            return 0;
        }

        #endregion
    }
}
