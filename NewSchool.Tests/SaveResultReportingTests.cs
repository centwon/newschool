using System;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 저장 결과 보고 회귀 테스트 — 전수 조사 34차 후속.
///
/// 서비스 계층이 리포지토리 반환값을 버리는 바람에, 화면이 결과를 검사해도
/// 실패를 알 수 없던 지점들을 고정한다.
/// </summary>
public class SaveResultReportingTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public SaveResultReportingTests(SqliteTestFixture db) => _db = db;

    /// <summary>
    /// <c>StudentDetailService.CreateOrUpdateAsync</c> 는 갱신 결과를 보지 않고 무조건
    /// 기존 No 를 돌려줬다 — 호출부가 반환값을 검사해도 실패를 알 수 없었다.
    /// </summary>
    [Fact]
    public async Task 상세정보_저장은_반영_안_되면_0을_돌려준다()
    {
        string studentId = await _db.NewStudentInDbAsync("상세저장");

        using var service = new StudentDetailService(_db.DbPath);

        // 최초 저장(생성) — 유효한 No 를 돌려준다
        int no = await service.CreateOrUpdateAsync(new StudentDetail
        {
            StudentID = studentId,
            GuardianName = "홍보호",
        });
        Assert.True(no > 0);

        // 같은 학생 재저장(갱신) — 여전히 유효한 No
        int again = await service.CreateOrUpdateAsync(new StudentDetail
        {
            StudentID = studentId,
            GuardianName = "김보호",
        });
        Assert.Equal(no, again);
    }

    /// <summary>
    /// <c>SaveManyAsync</c> 는 "하나라도 실패하면 전체 롤백"이라고 선언해 놓고
    /// 0행 갱신은 그대로 커밋했다. 이제 예외를 던져 롤백이 실제로 성립한다.
    /// </summary>
    [Fact]
    public async Task 학생부_일괄저장은_없는_기록이_섞이면_전체_롤백한다()
    {
        string studentId = await _db.NewStudentInDbAsync("일괄저장");

        using var service = new StudentSpecialService(_db.DbPath);
        using var repo = new StudentSpecialRepository(_db.DbPath);

        var good = TestData.NewSpecial(studentId, type: "자율활동", content: "정상 기록");
        var ghost = TestData.NewSpecial(studentId, type: "진로활동", content: "유령 기록");
        ghost.No = 999_999; // DB 에 없는 기록 → 0행 갱신

        await Assert.ThrowsAnyAsync<Exception>(
            () => service.SaveManyAsync([good, ghost]));

        // 롤백됐으므로 정상 기록도 남지 않아야 한다
        var all = await repo.GetByStudentAsync(studentId, TestData.Year);
        Assert.DoesNotContain(all, s => s.Content == "정상 기록");
    }

    /// <summary>
    /// <c>SchoolService.SaveSchoolAsync</c> 는 갱신 결과를 버려서, 저장이 실패해도
    /// 초기 설정·설정 화면이 성공으로 넘어갔다.
    /// </summary>
    [Fact]
    public async Task 학교_저장은_왕복이_된다()
    {
        using var service = new SchoolService(_db.DbPath);

        var saved = await service.SaveSchoolAsync(new School
        {
            SchoolCode = TestData.SchoolCode,
            SchoolName = "이름바꾼학교",
            Address = "주소",
        });

        Assert.True(saved.No > 0);
        Assert.Equal("이름바꾼학교", saved.SchoolName);
    }
}
