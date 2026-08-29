using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>EnrollmentService 서비스 로직 회귀 테스트 (TEST_PLAN 2단계).</summary>
public class EnrollmentServiceTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public EnrollmentServiceTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task 한_학년도에_학적은_한_줄뿐이다()
    {
        // 학기 컬럼을 없애면서 UNIQUE(StudentID, SchoolCode, Year) 가 이것을 강제한다.
        // 예전에는 1·2학기 행이 둘 다 있는 학생이 명부에 두 번 나왔다(2026-07-15).
        int year = TestData.Year + 12;
        using var repo = new EnrollmentRepository(_db.DbPath);
        var sid = await _db.NewStudentInDbAsync();
        await repo.CreateAsync(TestData.NewEnrollment(sid, year: year, grade: 2, classNum: 3, number: 7));

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await repo.CreateAsync(TestData.NewEnrollment(sid, year: year, grade: 2, classNum: 3, number: 7)));

        using var svc = new EnrollmentService(_db.DbPath);
        var roster = await svc.GetClassRosterAsync(TestData.SchoolCode, year, 2, 3);

        Assert.Single(roster, e => e.StudentID == sid);
    }

    [Fact]
    public async Task GetEnrollmentsAsync_이름은_Student_에서_가져온다()
    {
        // 이름은 Enrollment 의 컬럼이 아니라 EnrollmentFull 뷰가 JOIN 으로 읽는 값이다.
        // 예전에는 학적 쪽에 사본이 있어, 학생 이름을 고치면 두 곳이 어긋날 수 있었다.
        int year = TestData.Year + 13;
        using var repo = new EnrollmentRepository(_db.DbPath);

        var sid = await _db.NewStudentInDbAsync("이름확인");
        await repo.CreateAsync(TestData.NewEnrollment(sid, "엉뚱한이름", year: year, grade: 2, classNum: 3, number: 7));

        using var svc = new EnrollmentService(_db.DbPath);
        var roster = await svc.GetEnrollmentsAsync(TestData.SchoolCode, year, grade: 2, classNum: 3);

        // 학적에 넣으려 한 "엉뚱한이름" 은 저장되지 않고, Student 의 이름이 나온다.
        Assert.Single(roster);
        Assert.Equal("이름확인", roster[0].Name);
    }

    [Fact]
    public async Task GetEnrollmentsAsync_학년도가_다르면_각각_남는다()
    {
        // 중복 제거는 (학생, 학년도) 단위여야 한다 — 학생 ID 로만 묶으면 과거 학년도 학적이 사라진다.
        int year = TestData.Year + 14;
        using var repo = new EnrollmentRepository(_db.DbPath);
        var sid = await _db.NewStudentInDbAsync("두학년도");
        await repo.CreateAsync(TestData.NewEnrollment(sid, year: year, grade: 1, classNum: 1, number: 4));
        await repo.CreateAsync(TestData.NewEnrollment(sid, year: year + 1, grade: 2, classNum: 1, number: 4));

        using var svc = new EnrollmentService(_db.DbPath);
        var all = await svc.GetEnrollmentsAsync(TestData.SchoolCode);

        Assert.Equal(2, all.Count(e => e.StudentID == sid));
    }

    /// <summary>
    /// 학년·반을 넘기지 않으면 <b>그 해 재적자가 전부</b> 온다.
    ///
    /// <para>동아리 배정 다이얼로그가 후보를 <c>1~3학년 × 1~15반</c> 이중 루프로 모으다가
    /// 이 조회 한 번으로 바뀌었다. 두 배정 화면이 이제 이 계약에 기대므로 못박아 둔다 —
    /// 여기에 학년이나 반 조건이 슬쩍 들어가면 <b>후보 목록에서 학생이 조용히 사라지고</b>,
    /// 그건 "그 학생은 원래 명단에 없나 보다" 로 보여 알아채기 어렵다.</para>
    ///
    /// <para>학년은 중·고등학교라 1~3이면 맞았지만, 반은 학교마다 다르다. 15반을 넘겨
    /// 세우는 것은 <b>범위를 코드에 적어 두지 않았다</b>는 뜻이다.</para>
    /// </summary>
    [Fact]
    public async Task GetEnrollmentsAsync_학년도만_주면_전_학년_전_학급이_온다()
    {
        int year = TestData.Year + 15;
        using var repo = new EnrollmentRepository(_db.DbPath);

        // 1~3학년 × 1반, 그리고 옛 루프의 상한(15반) 밖에 있는 16반 하나
        var expected = new List<(string Id, string Label)>();
        for (int grade = 1; grade <= 3; grade++)
        {
            string id = await _db.NewStudentInDbAsync($"{grade}학년생");
            await repo.CreateAsync(TestData.NewEnrollment(
                id, year: year, grade: grade, classNum: 1, number: grade));
            expected.Add((id, $"{grade}학년 1반"));
        }

        string farClass = await _db.NewStudentInDbAsync("십육반생");
        await repo.CreateAsync(TestData.NewEnrollment(
            farClass, year: year, grade: 1, classNum: 16, number: 1));
        expected.Add((farClass, "1학년 16반"));

        using var svc = new EnrollmentService(_db.DbPath);
        var all = await svc.GetEnrollmentsAsync(TestData.SchoolCode, year);

        foreach (var (id, label) in expected)
        {
            Assert.True(all.Any(e => e.StudentID == id),
                $"{label} 학생이 빠졌다 — 학년·반 범위를 코드에 적어 두면 이렇게 된다");
        }
    }
}