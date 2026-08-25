namespace NewSchool.Board.Models;

/// <summary>
/// 게시판 뷰 모드. 목록 화면 오른쪽 위 전환 버튼이 Table → Card → Gallery 를 돌린다.
/// (뷰 모드를 저장하는 설정은 없다 — 화면을 다시 열면 <see cref="Table"/> 로 돌아온다.)
/// </summary>
public enum BoardViewMode
{
    /// <summary>지정 안 함. 호출부가 값을 넣지 않았을 때의 상태이며 <see cref="Table"/> 로 열린다.</summary>
    Default = 0,

    /// <summary>테이블형 - 전통적인 게시판 목록</summary>
    Table = 1,

    /// <summary>카드형 - 미리보기 포함 카드 레이아웃</summary>
    Card = 2,

    /// <summary>갤러리형 - 이미지 중심 그리드</summary>
    Gallery = 3
}
