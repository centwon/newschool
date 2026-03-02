using NewSchool.Board.Models;

namespace NewSchool.Board.Pages;

/// <summary>
/// PostListPage 네비게이션 파라미터
/// 다른 페이지에 내장될 때 사용
/// </summary>
public class PostListPageParameter
{
    /// <summary>카테고리 (필수, 고정됨)</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Subject (과목명, 동아리명 등)</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>페이지 제목 (기본: "게시판")</summary>
    public string Title { get; set; } = "게시판";

    /// <summary>내장 모드 - 제목/카테고리 선택 숨김</summary>
    public bool IsEmbedded { get; set; } = false;

    // ========== ViewMode 관련 (신규 추가) ==========

    /// <summary>뷰 모드 (Default=자동 선택)</summary>
    public BoardViewMode ViewMode { get; set; } = BoardViewMode.Default;

    /// <summary>카테고리 변경 허용 여부</summary>
    public bool AllowCategoryChange { get; set; } = true;

    /// <summary>뷰모드 변경 허용 여부</summary>
    public bool AllowViewModeChange { get; set; } = true;

    /// <summary>Subject 필터 표시 여부</summary>
    public bool ShowSubjectFilter { get; set; } = false;

    /// <summary>개인 영역 표시</summary>
    public bool IsPrivate { get; set; } = false;

    // ========== 기존 필드 유지 ==========

    /// <summary>새 글 작성 시 기본 카테고리</summary>
    public string DefaultCategory => Category;

    /// <summary>새 글 작성 시 기본 Subject</summary>
    public string DefaultSubject => Subject;
}
