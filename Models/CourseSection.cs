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
        set { if (SetProperty(ref _unitNo, value)) Notify(nameof(FullPath)); }
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
        set { if (SetProperty(ref _chapterNo, value)) Notify(nameof(FullPath)); }
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
        set { if (SetProperty(ref _sectionNo, value)) Notify(nameof(FullPath)); }
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
        set { if (SetProperty(ref _startPage, value)) Notify(nameof(PageRangeDisplay), nameof(ShortInfo)); }
    }

    /// <summary>교과서 끝 페이지</summary>
    public int EndPage
    {
        get => _endPage;
        set { if (SetProperty(ref _endPage, value)) Notify(nameof(PageRangeDisplay), nameof(ShortInfo)); }
    }

    /// <summary>예상 소요 차시</summary>
    public int EstimatedHours
    {
        get => _estimatedHours;
        set { if (SetProperty(ref _estimatedHours, value)) Notify(nameof(HoursDisplay), nameof(ShortInfo)); }
    }

    /// <summary>정렬 순서</summary>
    public int SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
    }

    #endregion

    #region Computed Properties - 기존

    // 표시용 다섯(UnitDisplay·ChapterDisplay·SectionDisplay·UnitChapterPath·PageDisplay)은
    // 읽는 곳이 없어 지웠다(39차). 화면은 FullPath·HoursDisplay·PageRangeDisplay·ShortInfo 를 쓴다.
    // SectionDisplay 만 쓰던 원문자 변환기 GetCircledNumber 도 함께 사라졌다.

    /// <summary>단원번호 대-중-소 (예: "1-1-1)</summary>
    public string FullPath => $"{UnitNo}-{ChapterNo}-{SectionNo}";

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
