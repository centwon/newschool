using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewSchool.Models
{
    /// <summary>
    /// 학교 정보 (NEIS 표준)
    /// </summary>
    public class School : NotifyPropertyChangedBase, IEntity
    {
        #region Fields

        private int _no = -1;
        private string _schoolCode = string.Empty;
        private string _atptOfcdcScCode = string.Empty;
        private string _atptOfcdcScName = string.Empty;
        private string _schoolName = string.Empty;
        private string _schoolType = string.Empty;
        private string _foundationDate = string.Empty;
        private string _address = string.Empty;
        private string _phone = string.Empty;
        private string _fax = string.Empty;
        private string _website = string.Empty;
        private string _principalName = string.Empty;
        private string _memo = string.Empty;
        private bool _isActive = true;
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

        /// <summary>표준학교코드 (NEIS 7자리, UNIQUE)</summary>
        public string SchoolCode
        {
            get => _schoolCode;
            set => SetProperty(ref _schoolCode, value);
        }

        /// <summary>시도교육청코드 (NEIS, 예: B10=서울)</summary>
        public string ATPT_OFCDC_SC_CODE
        {
            get => _atptOfcdcScCode;
            set => SetProperty(ref _atptOfcdcScCode, value);
        }

        /// <summary>시도교육청명 (예: 서울특별시교육청)</summary>
        public string ATPT_OFCDC_SC_NAME
        {
            get => _atptOfcdcScName;
            set => SetProperty(ref _atptOfcdcScName, value);
        }

        /// <summary>학교명</summary>
        public string SchoolName
        {
            get => _schoolName;
            set => SetProperty(ref _schoolName, value);
        }

        /// <summary>학교 종류 (초등학교/중학교/고등학교/특수학교)</summary>
        public string SchoolType
        {
            get => _schoolType;
            set => SetProperty(ref _schoolType, value);
        }

        /// <summary>개교일 (yyyy-MM-dd)</summary>
        public string FoundationDate
        {
            get => _foundationDate;
            set => SetProperty(ref _foundationDate, value);
        }

        #endregion

        #region Properties - 연락처

        /// <summary>주소</summary>
        public string Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        /// <summary>전화번호</summary>
        public string Phone
        {
            get => _phone;
            set => SetProperty(ref _phone, value);
        }

        /// <summary>팩스번호</summary>
        public string Fax
        {
            get => _fax;
            set => SetProperty(ref _fax, value);
        }

        /// <summary>홈페이지 URL</summary>
        public string Website
        {
            get => _website;
            set => SetProperty(ref _website, value);
        }

        #endregion

        #region Properties - 학교장

        /// <summary>교장명</summary>
        public string PrincipalName
        {
            get => _principalName;
            set => SetProperty(ref _principalName, value);
        }

        #endregion

        #region Properties - 기타

        /// <summary>메모</summary>
        public string Memo
        {
            get => _memo;
            set => SetProperty(ref _memo, value);
        }

        /// <summary>활성 여부 (폐교 시 false)</summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
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
            return SchoolName;
        }

        /// <summary>
        /// 학교 전체 표시명 (시도교육청 포함)
        /// </summary>
        public string GetFullName()
        {
            return $"{ATPT_OFCDC_SC_NAME} {SchoolName}";
        }

        /// <summary>
        /// 학교 코드 검증 (7자리 숫자)
        /// </summary>
        public bool IsValidSchoolCode()
        {
            return !string.IsNullOrEmpty(SchoolCode)
                && SchoolCode.Length == 7
                && long.TryParse(SchoolCode, out _);
        }

        #endregion
    }
}
