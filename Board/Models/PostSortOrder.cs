namespace NewSchool.Board.Models;

/// <summary>
/// 게시글 목록 정렬 기준
/// </summary>
public enum PostSortOrder
{
    /// <summary>최신순 (기본값)</summary>
    NewestFirst = 0,

    /// <summary>오래된순</summary>
    OldestFirst = 1,

    /// <summary>제목순 (가나다)</summary>
    TitleAsc = 2,

    /// <summary>조회수 많은순</summary>
    ReadCountDesc = 3,

    /// <summary>작성자순 (가나다)</summary>
    UserAsc = 4
}
