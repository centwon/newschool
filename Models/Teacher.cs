using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewSchool.Models
{
    /// <summary>
    /// 교사 정보 (NEIS 표준)
    /// </summary>
    public class Teacher : NotifyPropertyChangedBase, IEntity
    {
        #region Fields

        private int _no = -1;
        private string _teacherId = string.Empty;
        private string _loginId = string.Empty;
        private string _name = string.Empty;
        private string _status = "재직";
        private string _position = string.Empty;
        private string _subject = string.Empty;
        private string _phone = string.Empty;
        private string _email = string.Empty;
        private string _birthDate = string.Empty;
        private string _hireDate = string.Empty;
        private string _photo = string.Empty;
        private string _memo = string.Empty;
        private DateTime _createdAt = DateTime.Now;
        private DateTime _updatedAt = DateTime.Now;
        private DateTime? _lastLoginAt = null;
        private bool _isDeleted = false;

        #endregion

        #region Properties - 기본 정보

        /// <summary>PK (자동 증가)</summary>
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        /// <summary>교사 고유 ID (NEIS 교직원번호 등)</summary>
        public string TeacherID
        {
            get => _teacherId;
            set => SetProperty(ref _teacherId, value);
        }

        /// <summary>로그인 ID (앱 접속용)</summary>
        public string LoginID
        {
            get => _loginId;
            set => SetProperty(ref _loginId, value);
        }

        /// <summary>교사명</summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>재직 상태 (재직/휴직/퇴직)</summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>직위 (교사/부장교사/교감/교장)</summary>
        public string Position
        {
            get => _position;
            set => SetProperty(ref _position, value);
        }

        /// <summary>담당 과목</summary>
        public string Subject
        {
            get => _subject;
            set => SetProperty(ref _subject, value);
        }

        #endregion

        #region Properties - 연락처

        /// <summary>전화번호</summary>
        public string Phone
        {
            get => _phone;
            set => SetProperty(ref _phone, value);
        }

        /// <summary>이메일</summary>
        public string Email
        {
            get => _email;
            set => SetProperty(ref _email, value);
        }

        #endregion

        #region Properties - 인사 정보

        /// <summary>생년월일 (yyyy-MM-dd)</summary>
        public string BirthDate
        {
            get => _birthDate;
            set => SetProperty(ref _birthDate, value);
        }

        /// <summary>임용일 (yyyy-MM-dd)</summary>
        public string HireDate
        {
            get => _hireDate;
            set => SetProperty(ref _hireDate, value);
        }

        #endregion

        #region Properties - 기타

        /// <summary>프로필 사진 경로</summary>
        public string Photo
        {
            get => _photo;
            set => SetProperty(ref _photo, value);
        }

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

        /// <summary>마지막 로그인 일시</summary>
        public DateTime? LastLoginAt
        {
            get => _lastLoginAt;
            set => SetProperty(ref _lastLoginAt, value);
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
            return $"{Name} ({Position})";
        }

        /// <summary>
        /// 교사 표시명 (직위 포함)
        /// </summary>
        public string GetDisplayName()
        {
            return string.IsNullOrEmpty(Position)
                ? Name
                : $"{Name} {Position}";
        }

        /// <summary>
        /// 재직 중인지 확인
        /// </summary>
        public bool IsActive()
        {
            return Status == "재직" && !IsDeleted;
        }

        #endregion
    }
}
