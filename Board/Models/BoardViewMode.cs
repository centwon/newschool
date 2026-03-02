namespace NewSchool.Board.Models;

/// <summary>
/// 게시판 뷰 모드
/// </summary>
public enum BoardViewMode
{
    /// <summary>자동 선택 (저장된 설정 또는 추천 값 사용)</summary>
    Default = 0,

    /// <summary>테이블형 - 전통적인 게시판 목록</summary>
    Table = 1,

    /// <summary>카드형 - 미리보기 포함 카드 레이아웃</summary>
    Card = 2,

    /// <summary>갤러리형 - 이미지 중심 그리드 (선택사항, 추후 구현)</summary>
    Gallery = 3,

    /// <summary>메모장형 - 빠른 편집 가능한 메모 형식</summary>
    Memo = 4
}
