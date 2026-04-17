namespace NewSchool.Board.Pages;

/// <summary>
/// PostDetailPage 네비게이션 파라미터
/// </summary>
public class PostDetailPageParameter
{
    /// <summary>게시글 번호</summary>
    public int PostNo { get; set; }

    /// <summary>원래 게시판의 파라미터 (카테고리 고정 등 컨텍스트 유지)</summary>
    public PostListPageParameter? BoardParameter { get; set; }
}
