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

    // 페이지 제목(Title)은 지웠다 — 목록 화면 맨 위에 한 줄을 차지했는데, 왼쪽 메뉴가 이미
    // 같은 말("아카이브"·"업무 게시판"…)을 하고 있어 되풀이였다. 그 높이는 목록에 넘겼다.

    /// <summary>내장 모드 - 카테고리 선택 숨김</summary>
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

    // IsPrivate 는 세팅하는 곳도 읽는 곳도 없어 지웠다(39차) —
    // 개인/공용 구분은 게시판 카테고리로 한다.

    /// <summary>
    /// 새 글을 쓸 때 수업 일지 머리 정보(날짜·교시·교과·강의실·단원) 다이얼로그를 먼저 띄운다.
    /// 결과는 제목과 본문 첫 줄의 <b>기본값</b>으로만 들어가며, 편집기에서 자유롭게 고칠 수 있다.
    /// </summary>
    public bool UseLessonJournalTemplate { get; set; } = false;

    // ========== 기존 필드 유지 ==========

    /// <summary>새 글 작성 시 기본 카테고리</summary>
    public string DefaultCategory => Category;

    /// <summary>새 글 작성 시 기본 Subject</summary>
    public string DefaultSubject => Subject;
}
