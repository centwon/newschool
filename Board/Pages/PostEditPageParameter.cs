namespace NewSchool.Board.Pages;

/// <summary>
/// PostEditPage 네비게이션 파라미터
/// </summary>
public class PostEditPageParameter
{
    /// <summary>편집할 Post 번호 (0이면 새 글 작성)</summary>
    public int PostNo { get; set; } = 0;

    /// <summary>기본 카테고리 (카테고리 고정 시 사용)</summary>
    public string? DefaultCategory { get; set; }

    /// <summary>기본 Subject (Subject 고정 시 사용)</summary>
    public string? DefaultSubject { get; set; }

    /// <summary>카테고리 변경 허용 여부</summary>
    public bool AllowCategoryChange { get; set; } = true;
}
