using System;

namespace NewSchool.Models;

/// <summary>
/// 교과 단원 구조
/// 대단원 > 중단원 > 소단원 계층 구조 (단일 테이블)
/// </summary>
public class CourseSection : NotifyPropertyChangedBase, IEntity
{
    #region Fields

    private int _no = -1;
    private int _course;
    private int _unitNo;
    private string _unitName = string.Empty;
    private int _chapterNo;
    private string _chapterName = string.Empty;
    private int _sectionNo;
    private string _sectionName = string.Empty;
    private int _startPage;
    private int _endPage;
    private int _estimatedHours = 1;
    private int _sortOrder;
    private string _lessonPlan = string.Empty;

    // 신규 필드 (v2)
    private string _sectionType = "Normal";
    private bool _isPinned;
    private DateTime? _pinnedDate;
    private string _learningObjective = string.Empty;
    private string _materialPath = string.Empty;
    private string _materialUrl = string.Empty;
    private string _memo = string.Empty;

    #endregion

    #region Properties - 기본 정보

    /// <summary>PK (자동 증가)</summary>
    public int No
    {
        get => _no;
        set => SetProperty(ref _no, value);
    }

    /// <summary>과목 번호 (FK: Course.No)</summary>
    public int Course
    {
        get => _course;
        set => SetProperty(ref _course, value);
    }

    #endregion

    #region Properties - 대단원

    /// <summary>대단원 번호 (1, 2, 3...)</summary>
    public int UnitNo
    {
        get => _unitNo;
        set => SetProperty(ref _unitNo, value);
    }

    /// <summary>대단원명</summary>
    public string UnitName
    {
        get => _unitName;
        set => SetProperty(ref _unitName, value);
    }

    #endregion

    #region Properties - 중단원

    /// <summary>중단원 번호 (1, 2, 3...)</summary>
    public int ChapterNo
    {
        get => _chapterNo;
        set => SetProperty(ref _chapterNo, value);
    }

    /// <summary>중단원명</summary>
    public string ChapterName
    {
        get => _chapterName;
        set => SetProperty(ref _chapterName, value);
    }

    #endregion

    #region Properties - 소단원 (핵심)

    /// <summary>소단원 번호 (1, 2, 3...)</summary>
    public int SectionNo
    {
        get => _sectionNo;
        set => SetProperty(ref _sectionNo, value);
    }

    /// <summary>소단원명 (핵심 단위)</summary>
    public string SectionName
    {
        get => _sectionName;
        set => SetProperty(ref _sectionName, value);
    }

    /// <summary>교과서 시작 페이지</summary>
    public int StartPage
    {
        get => _startPage;
        set => SetProperty(ref _startPage, value);
    }

    /// <summary>교과서 끝 페이지</summary>
    public int EndPage
    {
        get => _endPage;
        set => SetProperty(ref _endPage, value);
    }

    /// <summary>예상 소요 차시</summary>
    public int EstimatedHours
    {
        get => _estimatedHours;
        set => SetProperty(ref _estimatedHours, value);
    }

    /// <summary>수업 계획 메모 (교수학습 활동, 평가 방법 등)</summary>
    public string LessonPlan
    {
        get => _lessonPlan;
        set => SetProperty(ref _lessonPlan, value);
    }

    #endregion

    #region Properties - 정렬

    /// <summary>정렬 순서</summary>
    public int SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
    }

    #endregion

    #region Properties - 유형 및 고정 (v2 신규)

    /// <summary>
    /// 단원 유형
    /// Normal: 일반수업, Exam: 지필고사, Assessment: 수행평가, Event: 행사/기타
    /// </summary>
    public string SectionType
    {
        get => _sectionType;
        set => SetProperty(ref _sectionType, value);
    }

    /// <summary>날짜 고정 여부 (Anchor 배치용)</summary>
    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    /// <summary>고정된 날짜 (지필고사, 수행평가 등)</summary>
    public DateTime? PinnedDate
    {
        get => _pinnedDate;
        set => SetProperty(ref _pinnedDate, value);
    }

    #endregion

    #region Properties - 추가 정보 (v2 신규)

    /// <summary>학습 목표</summary>
    public string LearningObjective
    {
        get => _learningObjective;
        set => SetProperty(ref _learningObjective, value);
    }

    /// <summary>자료 파일 경로 (PPT, PDF 등)</summary>
    public string MaterialPath
    {
        get => _materialPath;
        set => SetProperty(ref _materialPath, value);
    }

    /// <summary>자료 웹 링크</summary>
    public string MaterialUrl
    {
        get => _materialUrl;
        set => SetProperty(ref _materialUrl, value);
    }

    /// <summary>수업 후기/메모 (차년도 참고용)</summary>
    public string Memo
    {
        get => _memo;
        set => SetProperty(ref _memo, value);
    }

    #endregion

    #region Computed Properties - 기존

    /// <summary>대단원 표시 (예: "1. 수와 연산")</summary>
    public string UnitDisplay => $"{UnitNo}. {UnitName}";

    /// <summary>중단원 표시 (예: "1-1. 자연수의 혼합 계산")</summary>
    public string ChapterDisplay => $"{UnitNo}-{ChapterNo}. {ChapterName}";

    /// <summary>소단원 표시 (예: "① 덧셈과 뺄셈")</summary>
    public string SectionDisplay => $"{GetCircledNumber(SectionNo)} {SectionName}";

    /// <summary>단원번호 대-중-소 (예: "1-1-1)</summary>
    public string FullPath => $"{UnitNo}-{ChapterNo}-{SectionNo}";

    /// <summary>대단원 > 중단원 경로 (예: "수와 연산 > 자연수의 혼합 계산")</summary>
    public string UnitChapterPath => $"{UnitName} > {ChapterName}";

    /// <summary>페이지 정보 (예: "p.8")</summary>
    public string PageDisplay => StartPage > 0 ? $"p.{StartPage}" : "";

    /// <summary>차시 정보 (예: "2차시")</summary>
    public string HoursDisplay => $"{EstimatedHours}차시";

    #endregion

    #region Computed Properties - 신규 (v2)

    /// <summary>평가 단원 여부 (지필고사 또는 수행평가)</summary>
    public bool IsEvaluation => SectionType == "Exam" || SectionType == "Assessment";

    /// <summary>일반 수업 여부</summary>
    public bool IsNormal => SectionType == "Normal";

    /// <summary>이동 불가 여부 (고정되었거나 평가 단원)</summary>
    public bool IsFixed => IsPinned || IsEvaluation;

    /// <summary>페이지 범위 표시 (예: "p.8~12")</summary>
    public string PageRangeDisplay
    {
        get
        {
            if (StartPage <= 0) return "";
            if (EndPage > 0 && EndPage > StartPage)
                return $"p.{StartPage}~{EndPage}";
            return $"p.{StartPage}";
        }
    }

    /// <summary>유형 표시 (한글)</summary>
    public string SectionTypeDisplay => SectionType switch
    {
        "Normal" => "일반",
        "Exam" => "지필고사",
        "Assessment" => "수행평가",
        "Event" => "행사",
        _ => SectionType
    };

    /// <summary>유형 아이콘 (Segoe Fluent Icons)</summary>
    public string SectionTypeIcon => SectionType switch
    {
        "Normal" => "\uE7C3",      // 책
        "Exam" => "\uE9D9",        // 체크리스트
        "Assessment" => "\uE82D",  // 사람+체크
        "Event" => "\uE787",       // 달력
        _ => "\uE7C3"
    };

    /// <summary>고정 날짜 표시 (예: "3/15")</summary>
    public string PinnedDateDisplay => PinnedDate?.ToString("M/d") ?? "";

    /// <summary>고정 날짜 전체 표시 (예: "2025-03-15")</summary>
    public string PinnedDateFullDisplay => PinnedDate?.ToString("yyyy-MM-dd") ?? "";

    /// <summary>자료 존재 여부</summary>
    public bool HasMaterial => !string.IsNullOrEmpty(MaterialPath) || !string.IsNullOrEmpty(MaterialUrl);

    /// <summary>자료 표시 (파일명 또는 URL)</summary>
    public string MaterialDisplay
    {
        get
        {
            if (!string.IsNullOrEmpty(MaterialPath))
                return System.IO.Path.GetFileName(MaterialPath);
            if (!string.IsNullOrEmpty(MaterialUrl))
                return "웹 링크";
            return "";
        }
    }

    /// <summary>메모 존재 여부</summary>
    public bool HasMemo => !string.IsNullOrEmpty(Memo);

    /// <summary>학습 목표 존재 여부</summary>
    public bool HasLearningObjective => !string.IsNullOrEmpty(LearningObjective);

    /// <summary>간단한 정보 표시 (목록용)</summary>
    public string ShortInfo => $"{SectionTypeDisplay} | {HoursDisplay}" + 
        (IsFixed ? $" | 📌{PinnedDateDisplay}" : "");

    #endregion

    #region Helper Methods

    /// <summary>
    /// 숫자를 원문자로 변환 (1~20)
    /// </summary>
    private static string GetCircledNumber(int number)
    {
        if (number < 1 || number > 20) return number.ToString();

        // ① ~ ⑳
        char[] circled = { '①', '②', '③', '④', '⑤', '⑥', '⑦', '⑧', '⑨', '⑩',
                          '⑪', '⑫', '⑬', '⑭', '⑮', '⑯', '⑰', '⑱', '⑲', '⑳' };
        return circled[number - 1].ToString();
    }

    /// <summary>
    /// 복제본 생성 (수정 전 백업용)
    /// </summary>
    public CourseSection Clone()
    {
        return new CourseSection
        {
            No = this.No,
            Course = this.Course,
            UnitNo = this.UnitNo,
            UnitName = this.UnitName,
            ChapterNo = this.ChapterNo,
            ChapterName = this.ChapterName,
            SectionNo = this.SectionNo,
            SectionName = this.SectionName,
            StartPage = this.StartPage,
            EndPage = this.EndPage,
            EstimatedHours = this.EstimatedHours,
            SortOrder = this.SortOrder,
            LessonPlan = this.LessonPlan,
            SectionType = this.SectionType,
            IsPinned = this.IsPinned,
            PinnedDate = this.PinnedDate,
            LearningObjective = this.LearningObjective,
            MaterialPath = this.MaterialPath,
            MaterialUrl = this.MaterialUrl,
            Memo = this.Memo
        };
    }

    public override string ToString()
    {
        return $"{FullPath} ({PageRangeDisplay}, {HoursDisplay})";
    }

    #endregion
}
