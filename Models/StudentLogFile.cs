using System;

namespace NewSchool.Models;

/// <summary>
/// 누가기록에 딸린 첨부파일 한 건.
///
/// <para>실물은 <c>{데이터폴더}\StudentLogFiles\{학년도}\{학생ID}\{저장명}</c> 에 산다.
/// 경로를 만드는 것은 <see cref="Services.StudentLogAttachments"/> 한 곳뿐이다.</para>
///
/// <para>⚠ <b>폴더를 영역(Category)으로 나누지 않은 것은 의도한 것이다.</b> 게시판 첨부는
/// <c>Files\{카테고리}\</c> 에 사는데, 글의 카테고리가 바뀌면 실물을 따라 옮겨야 하고 그
/// 뒤처리가 130줄짜리 코드가 됐다(<c>PostAttachments.MoveAllToCategoryAsync</c>). 옮기지
/// 못하면 첨부가 끊기는 정도가 아니라 <b>같은 이름의 남의 파일이 열리고 지워진다</b>.
/// 누가기록의 영역도 편집 창에서 바뀌므로 같은 함정에 빠질 자리였다 — 그래서 <b>바뀌지 않는
/// 것</b>(학년도·학생)으로 나눈다. 그러면 옮기는 코드 자체가 필요 없다.
/// 사진(<c>PhotoService</c>)이 이미 <c>Photos\{연도}\{학생ID}</c> 로 같은 규칙을 쓴다.</para>
/// </summary>
public class StudentLogFile
{
    /// <summary>PK (자동 증가)</summary>
    public int No { get; set; } = -1;

    /// <summary>어느 누가기록의 첨부인가 (FK: StudentLog.No, ON DELETE CASCADE)</summary>
    public int LogNo { get; set; }

    /// <summary>
    /// 폴더를 정하는 두 값. 기록의 학년도·학생이 바뀌지 않으므로 첨부도 자리를 옮기지 않는다.
    /// 기록에서 유도하지 않고 직접 들고 있는 이유는 <see cref="StudentSpecial.Semester"/> 와
    /// 같다 — 기록이 지워진 뒤 남은 실물을 치우려면 경로를 스스로 알아야 한다.
    /// </summary>
    public int Year { get; set; }

    /// <inheritdoc cref="Year"/>
    public string StudentID { get; set; } = string.Empty;

    /// <summary>
    /// 폴더에 실제로 저장된 이름. 원본 이름과 다를 수 있다 —
    /// 같은 이름이 이미 있으면 OS 가 겹치지 않는 이름을 만들고, <b>그 결과를</b> 여기 넣는다.
    /// (게시판이 얻은 교훈: 희망 이름을 그대로 DB 에 넣으면 DB 엔 2건인데 실물은 1개가 된다.)
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>바이트 단위 크기. 목록에 보여 주기만 한다.</summary>
    public long FileSize { get; set; }

    /// <summary>붙인 시각</summary>
    public DateTime DateTime { get; set; } = DateTime.Now;
}
