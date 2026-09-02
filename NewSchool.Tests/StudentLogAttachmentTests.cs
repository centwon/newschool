using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Helpers;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 누가기록 첨부 — 규칙 고정.
///
/// <para>여기 담은 것들은 게시판 첨부가 <b>실제로 데었던</b> 자리다. 같은 함정을 되풀이하지
/// 않으려고 새 축을 만들 때부터 못박는다.</para>
/// </summary>
public class StudentLogAttachmentTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public StudentLogAttachmentTests(SqliteTestFixture db) => _db = db;

    private async Task<int> NewLogAsync(string studentId)
    {
        using var logRepo = new StudentLogRepository(_db.DbPath);
        return await logRepo.CreateAsync(TestData.NewStudentLog(studentId));
    }

    [Fact]
    public async Task 첨부는_기록에_붙어_왕복한다()
    {
        var studentId = await _db.NewStudentInDbAsync("첨부학생");
        int logNo = await NewLogAsync(studentId);

        using var repo = new StudentLogFileRepository(_db.DbPath);

        var file = new StudentLogFile
        {
            LogNo = logNo,
            Year = TestData.Year,
            StudentID = studentId,
            FileName = "수행평가 보고서.pdf",
            FileSize = 12345,
            DateTime = new DateTime(TestData.Year, 4, 15, 9, 30, 0),
        };

        Assert.True(await repo.CreateAsync(file) > 0);

        var found = await repo.GetByLogAsync(logNo);
        var one = Assert.Single(found);
        Assert.Equal("수행평가 보고서.pdf", one.FileName);
        Assert.Equal(12345, one.FileSize);
        Assert.Equal(studentId, one.StudentID);

        // ⚠ 시각까지 왕복해야 한다. 날짜만 보는 파서로 읽으면 조용히 0001-01-01 이 된다.
        Assert.Equal(new DateTime(TestData.Year, 4, 15, 9, 30, 0), one.DateTime);
    }

    /// <summary>
    /// <b>규칙: 기록이 사라지면 첨부 행도 사라진다.</b>
    /// (실물 파일은 따로 치운다 — CASCADE 는 DB 행만 지운다.)
    /// </summary>
    [Fact]
    public async Task 기록을_지우면_첨부_행도_따라_사라진다()
    {
        var studentId = await _db.NewStudentInDbAsync("첨부캐스케이드");
        int logNo = await NewLogAsync(studentId);

        using var repo = new StudentLogFileRepository(_db.DbPath);
        await repo.CreateAsync(new StudentLogFile
        {
            LogNo = logNo,
            Year = TestData.Year,
            StudentID = studentId,
            FileName = "사진.jpg",
        });

        using var logRepo = new StudentLogRepository(_db.DbPath);
        Assert.True(await logRepo.DeleteAsync(logNo));

        Assert.Empty(await repo.GetByLogAsync(logNo));
    }

    [Fact]
    public async Task 일괄_조회는_요청한_기록마다_키를_준다()
    {
        var studentId = await _db.NewStudentInDbAsync("첨부일괄");
        int withFile = await NewLogAsync(studentId);
        int without = await NewLogAsync(studentId);

        using var repo = new StudentLogFileRepository(_db.DbPath);
        await repo.CreateAsync(new StudentLogFile
        {
            LogNo = withFile,
            Year = TestData.Year,
            StudentID = studentId,
            FileName = "자료.hwp",
        });

        var map = await repo.GetByLogsAsync([withFile, without]);

        // 첨부가 없는 기록도 키가 있어야 부르는 쪽이 "없으면" 을 따로 다루지 않는다.
        Assert.Single(map[withFile]);
        Assert.Empty(map[without]);
    }

    /// <summary>
    /// <b>규칙: 폴더는 바뀌지 않는 것(학년도·학생)으로만 나눈다.</b>
    ///
    /// <para>게시판은 분류로 나눠서, 글의 분류가 바뀔 때마다 실물을 따라 옮겨야 했고
    /// 옮기지 못하면 같은 이름의 남의 파일이 열리고 지워졌다. 기록의 영역(Category)이
    /// 바뀌어도 첨부 경로는 그대로여야 한다 — 그래야 옮기는 코드가 아예 필요 없다.</para>
    /// </summary>
    [Fact]
    public void 경로는_학년도와_학생으로만_갈린다()
    {
        var a = new StudentLogFile
        {
            Year = 2026, StudentID = "S001", FileName = "x.pdf",
        };
        var b = new StudentLogFile
        {
            Year = 2026, StudentID = "S001", FileName = "x.pdf",
        };

        Assert.Equal(
            Services.StudentLogAttachments.GetFilePath(a),
            Services.StudentLogAttachments.GetFilePath(b));

        // 학년도나 학생이 다르면 갈린다
        var other = new StudentLogFile { Year = 2027, StudentID = "S001", FileName = "x.pdf" };
        Assert.NotEqual(
            Services.StudentLogAttachments.GetFilePath(a),
            Services.StudentLogAttachments.GetFilePath(other));
    }

    /// <summary>
    /// 학생 ID 는 폴더 이름이 된다. 경로 구분자나 <c>..</c> 가 섞이면 폴더를 빠져나가
    /// 엉뚱한 곳에 쓰게 되므로 다듬어야 한다 — 첨부 저장은 파일을 <b>만드는</b> 일이다.
    /// </summary>
    [Theory]
    [InlineData("../../etc")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    public void 학생ID_가_폴더를_빠져나가지_못한다(string studentId)
    {
        var path = Services.StudentLogAttachments.GetFolderPath(2026, studentId);
        var root = Path.Combine(Settings.UserDataPath, "StudentLogFiles");

        Assert.StartsWith(
            Path.GetFullPath(root),
            Path.GetFullPath(path),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <b>규칙: 차단 확장자 목록은 한 벌뿐이다.</b>
    ///
    /// <para>첨부는 <c>Process.Start(UseShellExecute = true)</c> 로 열리므로 이 목록이
    /// 유일한 방어선이다. 게시판과 누가기록이 각자 목록을 들면 한쪽만 늘어나 조용히
    /// 어긋난다 — 실제로 예전에 실행형 13종만 막아 <c>.lnk</c>·<c>.url</c>·<c>.hta</c> 가
    /// 통과했다.</para>
    /// </summary>
    [Theory]
    [InlineData("보고서.exe")]
    [InlineData("설정.bat")]
    [InlineData("바로가기.lnk")]
    [InlineData("링크.url")]
    [InlineData("스크립트.hta")]
    [InlineData("매크로.js")]
    [InlineData("대문자.EXE")]
    public void 실행_유발_확장자는_막는다(string fileName)
        => Assert.True(AttachmentPolicy.IsBlocked(fileName));

    [Theory]
    [InlineData("보고서.pdf")]
    [InlineData("사진.jpg")]
    [InlineData("자료.hwp")]
    [InlineData("묶음.zip")]
    [InlineData("확장자없음")]
    public void 문서_이미지_압축은_허용한다(string fileName)
        => Assert.False(AttachmentPolicy.IsBlocked(fileName));

    /// <summary>게시판 쪽 판정도 같은 한 벌을 지나가야 한다.</summary>
    [Fact]
    public void 게시판과_누가기록이_같은_목록을_쓴다()
    {
        foreach (var name in new[] { "a.exe", "a.lnk", "a.pdf", "a.zip" })
            Assert.Equal(AttachmentPolicy.IsBlocked(name), Board.Board.IsBlockedAttachment(name));
    }
}
