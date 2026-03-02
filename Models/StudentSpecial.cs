using System;

namespace NewSchool.Models
{
    /// <summary>
    /// 학교생활기록부 특기사항
    /// NEIS 입력용 최종 기록 (교과세특, 자율/동아리/진로활동, 종합의견)
    /// StudentLog(활동 근거자료)를 바탕으로 작성하며, 바이트 제한이 적용됨
    /// </summary>
    public class StudentSpecial : NotifyPropertyChangedBase, IEntity
    {
        #region Fields

        private int _no = -1;
        private string _studentId = string.Empty;
        private int _year = DateTime.Today.Year;
        private string _type = string.Empty;
        private string _title = string.Empty;
        private string _content = string.Empty;
        private string _date = DateTime.Today.ToString("yyyy-MM-dd");
        private string _teacherId = string.Empty;
        private int _courseNo = 0;
        private string _subjectName = string.Empty;
        private bool _isFinalized = false;
        private string _tag = string.Empty;

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

        /// <summary>학년도</summary>
        public int Year
        {
            get => _year;
            set => SetProperty(ref _year, value);
        }

        #endregion

        #region Properties - 특이사항 정보

        /// <summary>
        /// 학생부 영역
        /// 교과활동/개인별세특/자율활동/동아리활동/진로활동/종합의견
        /// </summary>
        public string Type
        {
            get => _type;
            set => SetProperty(ref _type, value);
        }

        /// <summary>제목</summary>
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>NEIS 입력용 특기사항 본문 (바이트 제한 적용)</summary>
        public string Content
        {
            get => _content;
            set => SetProperty(ref _content, value);
        }

        /// <summary>작성일 (yyyy-MM-dd)</summary>
        public string Date
        {
            get => _date;
            set => SetProperty(ref _date, value);
        }

        /// <summary>작성 교사 ID (FK: Teacher.TeacherID)</summary>
        public string TeacherID
        {
            get => _teacherId;
            set => SetProperty(ref _teacherId, value);
        }

        #endregion

        #region Properties - 수업 연계 정보

        /// <summary>수업 번호 (FK: Course.No, NULL 허용)</summary>
        public int CourseNo
        {
            get => _courseNo;
            set => SetProperty(ref _courseNo, value);
        }

        /// <summary>과목명 (NULL 허용)</summary>
        public string SubjectName
        {
            get => _subjectName;
            set => SetProperty(ref _subjectName, value);
        }

        #endregion

        #region Properties - 관리 정보

        /// <summary>
        /// 마감(확정) 여부
        /// false: 작성 중 (수정 가능), true: 마감됨 (수정 불가)
        /// </summary>
        public bool IsFinalized
        {
            get => _isFinalized;
            set => SetProperty(ref _isFinalized, value);
        }

        /// <summary>태그 (검색용, 쉼표로 구분)</summary>
        public string Tag
        {
            get => _tag;
            set => SetProperty(ref _tag, value);
        }

        #endregion

        #region Methods

        public override string ToString()
        {
            return $"[{Type}] {Title}";
        }

        #endregion
    }
}
