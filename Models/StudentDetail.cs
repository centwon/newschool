using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewSchool.Models
{
    /// <summary>
    /// 학생 상세 정보
    /// Student와 1:1 관계 (선택적)
    /// 보호자, 가족, 진로, 특기사항 등
    /// </summary>
    public class StudentDetail : NotifyPropertyChangedBase, IEntity
    {
        #region Fields

        private int _no = -1;
        private string _studentId = string.Empty;
        private string _fatherName = string.Empty;
        private string _fatherPhone = string.Empty;
        private string _fatherJob = string.Empty;
        private string _motherName = string.Empty;
        private string _motherPhone = string.Empty;
        private string _motherJob = string.Empty;
        private string _guardianName = string.Empty;
        private string _guardianPhone = string.Empty;
        private string _guardianRelation = string.Empty;
        private string _familyInfo = string.Empty;
        private string _friends = string.Empty;
        private string _interests = string.Empty;
        private string _talents = string.Empty;
        private string _careerGoal = string.Empty;
        private string _healthInfo = string.Empty;
        private string _allergies = string.Empty;
        private string _specialNeeds = string.Empty;
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

        /// <summary>학생 ID (FK: Student.StudentID, UNIQUE)</summary>
        public string StudentID
        {
            get => _studentId;
            set => SetProperty(ref _studentId, value);
        }

        #endregion

        #region Properties - 부모 정보

        /// <summary>아버지 성함</summary>
        public string FatherName
        {
            get => _fatherName;
            set => SetProperty(ref _fatherName, value);
        }

        /// <summary>아버지 전화번호</summary>
        public string FatherPhone
        {
            get => _fatherPhone;
            set => SetProperty(ref _fatherPhone, value);
        }

        /// <summary>아버지 직업</summary>
        public string FatherJob
        {
            get => _fatherJob;
            set => SetProperty(ref _fatherJob, value);
        }

        /// <summary>어머니 성함</summary>
        public string MotherName
        {
            get => _motherName;
            set => SetProperty(ref _motherName, value);
        }

        /// <summary>어머니 전화번호</summary>
        public string MotherPhone
        {
            get => _motherPhone;
            set => SetProperty(ref _motherPhone, value);
        }

        /// <summary>어머니 직업</summary>
        public string MotherJob
        {
            get => _motherJob;
            set => SetProperty(ref _motherJob, value);
        }

        #endregion

        #region Properties - 보호자 정보

        /// <summary>보호자 성함 (부모가 아닌 경우)</summary>
        public string GuardianName
        {
            get => _guardianName;
            set => SetProperty(ref _guardianName, value);
        }

        /// <summary>보호자 전화번호</summary>
        public string GuardianPhone
        {
            get => _guardianPhone;
            set => SetProperty(ref _guardianPhone, value);
        }

        /// <summary>보호자 관계 (조부모, 친척 등)</summary>
        public string GuardianRelation
        {
            get => _guardianRelation;
            set => SetProperty(ref _guardianRelation, value);
        }

        #endregion

        #region Properties - 가정 환경

        /// <summary>가족 구성 및 환경</summary>
        public string FamilyInfo
        {
            get => _familyInfo;
            set => SetProperty(ref _familyInfo, value);
        }

        #endregion

        #region Properties - 교우 관계

        /// <summary>친한 친구들</summary>
        public string Friends
        {
            get => _friends;
            set => SetProperty(ref _friends, value);
        }

        #endregion

        #region Properties - 학생 특성

        /// <summary>관심사 및 취미</summary>
        public string Interests
        {
            get => _interests;
            set => SetProperty(ref _interests, value);
        }

        /// <summary>특기</summary>
        public string Talents
        {
            get => _talents;
            set => SetProperty(ref _talents, value);
        }

        /// <summary>진로 희망</summary>
        public string CareerGoal
        {
            get => _careerGoal;
            set => SetProperty(ref _careerGoal, value);
        }

        #endregion

        #region Properties - 건강 정보

        /// <summary>건강 상태 및 주의사항</summary>
        public string HealthInfo
        {
            get => _healthInfo;
            set => SetProperty(ref _healthInfo, value);
        }

        /// <summary>알레르기 정보</summary>
        public string Allergies
        {
            get => _allergies;
            set => SetProperty(ref _allergies, value);
        }

        /// <summary>특수 교육 대상 여부 및 내용</summary>
        public string SpecialNeeds
        {
            get => _specialNeeds;
            set => SetProperty(ref _specialNeeds, value);
        }

        #endregion

        #region Properties - 기타

        /// <summary>메모 (기타 상세 사항)</summary>
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
            return $"StudentDetail for {StudentID}";
        }

        /// <summary>
        /// 주 연락처 가져오기 (우선순위: 어머니 → 아버지 → 보호자)
        /// </summary>
        public string GetPrimaryContact()
        {
            if (!string.IsNullOrEmpty(MotherPhone)) return MotherPhone;
            if (!string.IsNullOrEmpty(FatherPhone)) return FatherPhone;
            if (!string.IsNullOrEmpty(GuardianPhone)) return GuardianPhone;
            return string.Empty;
        }

        /// <summary>
        /// 주 보호자명 가져오기
        /// </summary>
        public string GetPrimaryGuardianName()
        {
            if (!string.IsNullOrEmpty(MotherName)) return MotherName;
            if (!string.IsNullOrEmpty(FatherName)) return FatherName;
            if (!string.IsNullOrEmpty(GuardianName)) return GuardianName;
            return string.Empty;
        }

        /// <summary>
        /// 특이사항 여부 확인
        /// </summary>
        public bool HasSpecialConsiderations()
        {
            return !string.IsNullOrEmpty(HealthInfo)
                || !string.IsNullOrEmpty(Allergies)
                || !string.IsNullOrEmpty(SpecialNeeds);
        }

        #endregion
    }
}
