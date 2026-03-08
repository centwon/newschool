using System;

namespace NewSchool.Models
{
    /// <summary>
    /// 동아리 배정 정보
    /// 학생이 특정 동아리에 배정되는 정보
    /// </summary>
    public class ClubEnrollment : NotifyPropertyChangedBase, IEntity
    {
        #region Fields

        private int _no = -1;
        private string _studentId = string.Empty;
        private int _clubNo = -1;
        private string _status = ClubEnrollmentStatus.Active;
        private string _remark = string.Empty;
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

        /// <summary>학생 ID (FK: Student.StudentID)</summary>
        public string StudentID
        {
            get => _studentId;
            set => SetProperty(ref _studentId, value);
        }

        /// <summary>동아리 번호 (FK: Club.No)</summary>
        public int ClubNo
        {
            get => _clubNo;
            set => SetProperty(ref _clubNo, value);
        }

        #endregion

        #region Properties - 배정 정보

        /// <summary>
        /// 활동 상태
        /// 활동중/탈퇴
        /// </summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
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

        #endregion

        #region Methods

        public override string ToString()
        {
            return $"Student={StudentID}, Club={ClubNo}";
        }

        #endregion
    }
}
