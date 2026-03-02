using System;
using System.Text;

namespace NewSchool.Models
{
    /// <summary>
    /// 학생 기록부 (행동 특성 및 종합의견)
    /// IStudentRecord 인터페이스 구현
    /// ⭐ 확장 버전: 구조화된 활동 기록 필드 추가
    /// </summary>
    public class StudentLog : NotifyPropertyChangedBase, IStudentRecord
    {
        #region Fields - 기본 정보

        private int _no = -1;
        private string _studentId = string.Empty;
        private string _teacherId = string.Empty;
        private int _year = DateTime.Today.Year;
        private int _semester = 1;
        private DateTime _date = DateTime.Now;
        private LogCategory _category = LogCategory.전체;
        private int _courseNo = 0;
        private string _subjectName = string.Empty;
        private int _clubNo = 0;
        private string _clubName = string.Empty;
        private string _log = string.Empty;
        private string _tag = string.Empty;
        private bool _isImportant = false;

        #endregion

        #region Fields - 구조화된 활동 기록

        private string _activityName = string.Empty;
        private string _topic = string.Empty;
        private string _description = string.Empty;
        private string _role = string.Empty;
        private string _skillDeveloped = string.Empty;
        private string _strengthShown = string.Empty;
        private string _resultOrOutcome = string.Empty;

        #endregion

        #region Properties - IEntity

        /// <summary>PK (자동 증가)</summary>
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        #endregion

        #region Properties - IStudentRecord

        /// <summary>학생 ID (FK: Student.StudentID)</summary>
        public string StudentID
        {
            get => _studentId;
            set => SetProperty(ref _studentId, value);
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

        /// <summary>작성일 (yyyy-MM-dd)</summary>
        public DateTime Date
        {
            get => _date;
            set => SetProperty(ref _date, value);
        }

        /// <summary>
        /// 카테고리 (LogCategory enum)
        /// 교과활동/개인별세특/자율활동/동아리활동/봉사활동/진로활동/종합의견/상담기록/기타
        /// </summary>
        public LogCategory Category
        {
            get => _category;
            set => SetProperty(ref _category, value);
        }

        /// <summary>수업 번호 (FK: Course.No, NULL 허용)</summary>
        public int CourseNo
        {
            get => _courseNo;
            set => SetProperty(ref _courseNo, value);
        }

        /// <summary>과목명 (예: "국어", "수학")</summary>
        public string SubjectName
        {
            get => _subjectName;
            set => SetProperty(ref _subjectName, value);
        }

        /// <summary>동아리 번호 (FK: Club.No, NULL 허용)</summary>
        public int ClubNo
        {
            get => _clubNo;
            set => SetProperty(ref _clubNo, value);
        }

        /// <summary>동아리명</summary>
        public string ClubName
        {
            get => _clubName;
            set => SetProperty(ref _clubName, value);
        }

        /// <summary>기록 내용 (단순 기록용 또는 전체 내용)</summary>
        public string Log
        {
            get => _log;
            set => SetProperty(ref _log, value);
        }

        #endregion

        #region Properties - 구조화된 활동 기록

        /// <summary>활동명 (예: "수행평가", "프로젝트 학습", "환경보호 캠페인")</summary>
        public string ActivityName
        {
            get => _activityName;
            set => SetProperty(ref _activityName, value);
        }

        /// <summary>활동 주제 (예: "조선 후기 실학사상", "기후변화 대응")</summary>
        public string Topic
        {
            get => _topic;
            set => SetProperty(ref _topic, value);
        }

        /// <summary>구체적 활동 내용</summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        /// <summary>활동에서 맡은 역할 (예: "팀장", "발표 담당", "자료 조사")</summary>
        public string Role
        {
            get => _role;
            set => SetProperty(ref _role, value);
        }

        /// <summary>기른 능력 (예: "협업 능력", "논리적 사고력")</summary>
        public string SkillDeveloped
        {
            get => _skillDeveloped;
            set => SetProperty(ref _skillDeveloped, value);
        }

        /// <summary>드러난 장점 (예: "주도성", "성실성", "창의성")</summary>
        public string StrengthShown
        {
            get => _strengthShown;
            set => SetProperty(ref _strengthShown, value);
        }

        /// <summary>성취 및 결과 (예: "발표 성공", "우수 평가 획득")</summary>
        public string ResultOrOutcome
        {
            get => _resultOrOutcome;
            set => SetProperty(ref _resultOrOutcome, value);
        }

        #endregion

        #region Properties - 추가 정보

        /// <summary>태그 (검색용, 쉼표로 구분)</summary>
        public string Tag
        {
            get => _tag;
            set => SetProperty(ref _tag, value);
        }

        /// <summary>중요 표시</summary>
        public bool IsImportant
        {
            get => _isImportant;
            set => SetProperty(ref _isImportant, value);
        }

        #endregion

        #region Computed Properties - 자동 요약

        /// <summary>
        /// 검토용 요약 (항목명 포함)
        /// 구조화된 필드가 있으면 항목별로 나열, 없으면 Log 필드 반환
        /// </summary>
        public string Summary
        {
            get
            {
                // 구조화된 필드가 하나라도 있는지 확인
                bool hasStructuredData = !string.IsNullOrWhiteSpace(ActivityName) ||
                                        !string.IsNullOrWhiteSpace(Topic) ||
                                        !string.IsNullOrWhiteSpace(Description);

                if (!hasStructuredData)
                {
                    // 구조화된 데이터가 없으면 기존 Log 필드 반환
                    return Log ?? string.Empty;
                }

                // 구조화된 데이터로 요약 생성
                var sb = new StringBuilder();

                if (!string.IsNullOrWhiteSpace(ActivityName))
                    sb.AppendLine($"활동명: {ActivityName}");

                if (!string.IsNullOrWhiteSpace(Topic))
                    sb.AppendLine($"주제: {Topic}");

                if (!string.IsNullOrWhiteSpace(Description))
                    sb.AppendLine($"활동 내용: {Description}");

                if (!string.IsNullOrWhiteSpace(Role))
                    sb.AppendLine($"역할: {Role}");

                if (!string.IsNullOrWhiteSpace(SkillDeveloped))
                    sb.AppendLine($"기른 능력: {SkillDeveloped}");

                if (!string.IsNullOrWhiteSpace(StrengthShown))
                    sb.AppendLine($"장점: {StrengthShown}");

                if (!string.IsNullOrWhiteSpace(ResultOrOutcome))
                    sb.AppendLine($"성취: {ResultOrOutcome}");

                return sb.ToString().Trim();
            }
        }

        /// <summary>
        /// 학생부 기록용 초안 (자연스러운 문장)
        /// 구조화된 필드가 있으면 문장으로 변환, 없으면 Log 필드 반환
        /// </summary>
        public string DraftSummary
        {
            get
            {
                // 구조화된 필드가 하나라도 있는지 확인
                bool hasStructuredData = !string.IsNullOrWhiteSpace(ActivityName) ||
                                        !string.IsNullOrWhiteSpace(Topic) ||
                                        !string.IsNullOrWhiteSpace(Description);

                if (!hasStructuredData)
                {
                    // 구조화된 데이터가 없으면 기존 Log 필드 반환
                    return Log ?? string.Empty;
                }

                // 카테고리명 변환
                string categoryName = Category switch
                {
                    LogCategory.교과활동 => "교과활동",
                    LogCategory.개인별세특 => "개인별 세부능력",
                    LogCategory.자율활동 => "자율활동",
                    LogCategory.동아리활동 => "동아리활동",
                    LogCategory.봉사활동 => "봉사활동",
                    LogCategory.진로활동 => "진로활동",
                    LogCategory.종합의견 => "행동특성",
                    LogCategory.상담기록 => "상담",
                    _ => "활동"
                };

                // 과목 정보 (교과활동인 경우)
                string subjectInfo = (Category == LogCategory.교과활동 || Category == LogCategory.개인별세특) 
                                     && !string.IsNullOrWhiteSpace(SubjectName)
                    ? $"({SubjectName}) "
                    : "";

                // 자연스러운 문장 생성
                var sb = new StringBuilder();

                // 기본 구조: [카테고리] (과목) [활동명]에서 '[주제]'을/를 주제로 [활동내용]
                if (!string.IsNullOrWhiteSpace(ActivityName))
                {
                    sb.Append($"{categoryName} {subjectInfo}{ActivityName}");
                }
                else
                {
                    sb.Append($"{categoryName} {subjectInfo}");
                }

                if (!string.IsNullOrWhiteSpace(Topic))
                {
                    sb.Append($"에서 '{Topic}'을(를) 주제로");
                }

                if (!string.IsNullOrWhiteSpace(Description))
                {
                    sb.Append($" {Description}");
                }

                // 역할 추가
                if (!string.IsNullOrWhiteSpace(Role))
                {
                    sb.Append($", {Role}");
                }

                // 기른 능력 추가
                if (!string.IsNullOrWhiteSpace(SkillDeveloped))
                {
                    sb.Append($", 이를 통해 {SkillDeveloped}을(를) 기름");
                }

                // 드러난 장점 추가
                if (!string.IsNullOrWhiteSpace(StrengthShown))
                {
                    sb.Append($", {StrengthShown}을(를) 발휘");
                }

                // 성취 및 결과 추가
                if (!string.IsNullOrWhiteSpace(ResultOrOutcome))
                {
                    sb.Append($", {ResultOrOutcome}");
                }

                sb.Append(".");

                return sb.ToString();
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// 구조화된 데이터가 있는지 확인
        /// </summary>
        public bool HasStructuredData()
        {
            return !string.IsNullOrWhiteSpace(ActivityName) ||
                   !string.IsNullOrWhiteSpace(Topic) ||
                   !string.IsNullOrWhiteSpace(Description) ||
                   !string.IsNullOrWhiteSpace(Role) ||
                   !string.IsNullOrWhiteSpace(SkillDeveloped) ||
                   !string.IsNullOrWhiteSpace(StrengthShown) ||
                   !string.IsNullOrWhiteSpace(ResultOrOutcome);
        }

        public override string ToString()
        {
            if (HasStructuredData())
            {
                return $"[{Category}] {ActivityName ?? Topic ?? Description?.Substring(0, Math.Min(20, Description.Length))}";
            }
            return $"[{Category}] {Log?.Substring(0, Math.Min(20, Log?.Length ?? 0))}...";
        }

        #endregion
    }
}
