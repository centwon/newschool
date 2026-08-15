using System;

namespace NewSchool.Models;

/// <summary>
/// 교과 단원 구조
/// 대단원 > 중단원 > 소단원 계층 구조 (단일 테이블)
/// </summary>
public class CourseSection : NotifyPropertyChangedBase
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

    // 신규 필드 (v2)

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

    /// <summary>정렬 순서</summary>
    public int SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
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

    /// <summary>간단한 정보 표시 (목록용)</summary>
    public string ShortInfo => $"{PageRangeDisplay} | {HoursDisplay}".TrimStart(' ', '|');

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
            SortOrder = this.SortOrder
        };
    }

    public override string ToString()
    {
        return $"{FullPath} ({PageRangeDisplay}, {HoursDisplay})";
    }

    #endregion
}
