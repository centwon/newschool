using Microsoft.Data.Sqlite;
using NewSchool.Helpers;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 자료 파일이 지금 판과 안 맞을 때 <b>사용자에게 무엇을 말하는가</b> — 51차(스키마 세대 축).
///
/// <para>이 프로젝트는 ALTER 마이그레이션을 두지 않기로 했으므로, 새 판이 칸을 하나 더 쓰기
/// 시작하면 옛 자료 파일에서는 그 화면이 통째로 실패한다. 그때 보이던 것은
/// <c>SQLite Error 1: 'no such column: IsPinned'</c> 였다 — 자기 자료가 무사한지조차
/// 알 수 없는 문장이다. 그 자리에 무슨 말을 세울지를 여기서 못박는다.</para>
/// </summary>
public class DbErrorTextTests
{
    private static SqliteException Sqlite(string message, int code) => new(message, code);

    /// <summary>
    /// 가장 중요한 경우 — <b>자료가 지워진 것이 아니라는 말</b>이 반드시 있어야 한다.
    /// 그 말이 없으면 교사는 백업 복원부터 시도한다.
    /// </summary>
    [Fact]
    public void 없는_칸이면_옛_파일임을_말하고_자료는_무사하다고_말한다()
    {
        var text = DbErrorText.Explain(Sqlite("SQLite Error 1: 'no such column: IsPinned'.", 1));

        Assert.NotNull(text);
        Assert.Contains("IsPinned", text);
        Assert.Contains("오래된 자료 파일", text);
        Assert.Contains("지워진 것은 아닙니다", text);
    }

    /// <summary>표 이름은 <c>Post.IsPinned</c> 처럼 붙어 오기도 한다 — 사용자에게는 칸 이름이면 된다.</summary>
    [Fact]
    public void 표_이름이_붙어_와도_칸_이름만_보여_준다()
    {
        var text = DbErrorText.Explain(Sqlite("SQLite Error 1: 'no such column: Post.IsPinned'.", 1));

        Assert.NotNull(text);
        Assert.Contains("'IsPinned'", text);
        Assert.DoesNotContain("Post.IsPinned", text);
    }

    /// <summary>
    /// <b>같은 상황이 두 길로 온다</b> — 쿼리가 칸 이름을 직접 대면 SQLite 가 막고,
    /// <c>SELECT *</c> 로 읽으면 매핑에서 걸린다. 두 길이 같은 말을 해야 한다(49차의 규칙).
    /// </summary>
    [Fact]
    public void 없는_칸은_어느_길로_와도_같은_말을_한다()
    {
        var byQuery = DbErrorText.Explain(Sqlite("SQLite Error 1: 'no such column: IsPinned'.", 1));
        var byMapping = DbErrorText.Explain(new MissingColumnException("IsPinned"));

        Assert.NotNull(byMapping);
        Assert.Equal(byQuery, byMapping);
    }

    [Fact]
    public void 없는_표도_같은_말을_한다()
    {
        var text = DbErrorText.Explain(Sqlite("SQLite Error 1: 'no such table: StudentLogFile'.", 1));

        Assert.NotNull(text);
        Assert.Contains("StudentLogFile", text);
        Assert.Contains("지워진 것은 아닙니다", text);
    }

    /// <summary>
    /// 손상은 할 일이 다르다 — 복원 자리를 짚어 준다(48차에서 세운 흐름).
    /// </summary>
    [Theory]
    [InlineData(11)]   // SQLITE_CORRUPT
    [InlineData(26)]   // SQLITE_NOTADB
    public void 손상된_파일이면_백업_복원을_가리킨다(int code)
    {
        var text = DbErrorText.Explain(Sqlite("database disk image is malformed", code));

        Assert.NotNull(text);
        Assert.Contains("백업", text);
        Assert.Contains("복원", text);
    }

    /// <summary>
    /// 잠김은 OneDrive 운영에서 실제로 나는 실패다(사용자가 위험을 알고 고른 구성).
    /// </summary>
    [Theory]
    [InlineData(5)]   // SQLITE_BUSY
    [InlineData(6)]   // SQLITE_LOCKED
    public void 잠긴_파일이면_기다렸다_다시_하라고_말한다(int code)
    {
        var text = DbErrorText.Explain(Sqlite("database is locked", code));

        Assert.NotNull(text);
        Assert.Contains("다시 시도", text);
        Assert.Contains("동기화", text);
    }

    [Fact]
    public void 읽기_전용이면_폴더_권한을_가리킨다()
    {
        var text = DbErrorText.Explain(Sqlite("attempt to write a readonly database", 8));

        Assert.NotNull(text);
        Assert.Contains("읽기 전용", text);
    }

    /// <summary>
    /// 할 말이 없으면 null 이다 — 엉뚱한 안내보다 원문이 낫다(49차와 같은 규칙).
    /// SQL 문법 오류처럼 사용자가 할 수 있는 일이 없는 것도 여기에 든다.
    /// </summary>
    [Fact]
    public void 해_줄_말이_없으면_원문을_쓰게_둔다()
    {
        Assert.Null(DbErrorText.Explain(null));
        Assert.Null(DbErrorText.Explain(new System.InvalidOperationException("아무 예외")));
        Assert.Null(DbErrorText.Explain(Sqlite("SQLite Error 1: 'near \"SELCT\": syntax error'.", 1)));
    }
}
