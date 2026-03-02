using System;
using System.ComponentModel;

namespace NewSchool.Models
{
    /// <summary>
    /// 동아리 정보
    /// 특정 학년도에 개설되는 동아리 정보 관리
    /// </summary>
    public partial class Club : NotifyPropertyChangedBase, IEntity
    {
        #region Fields

        private int _no = -1;
        private string _schoolCode = string.Empty;
        private string _teacherId = string.Empty;
        private int _year = DateTime.Today.Year;
        private string _clubName = string.Empty;
        private string _activityRoom = string.Empty;
        private string _remark = string.Empty;
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

        /// <summary>학교 코드 (FK: School.SchoolCode)</summary>
        public string SchoolCode
        {
            get => _schoolCode;
            set => SetProperty(ref _schoolCode, value);
        }

        /// <summary>지도 교사 ID (FK: Teacher.TeacherID)</summary>
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

        #endregion

        #region Properties - 동아리 정보

        /// <summary>동아리명</summary>
        public string ClubName
        {
            get => _clubName;
            set => SetProperty(ref _clubName, value);
        }

        /// <summary>활동실</summary>
        public string ActivityRoom
        {
            get => _activityRoom;
            set => SetProperty(ref _activityRoom, value);
        }

        /// <summary>비고</summary>
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
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

        /// <summary>삭제 여부</summary>
        public bool IsDeleted
        {
            get => _isDeleted;
            set => SetProperty(ref _isDeleted, value);
        }

        #endregion

        #region Methods

        public override string ToString()
        {
            return $"{ClubName} ({Year})";
        }

        #endregion
    }
}
