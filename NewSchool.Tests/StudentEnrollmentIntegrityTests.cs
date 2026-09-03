using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 학생 등록 화면(AddStudentsPage)이 기대는 DB 불변식을 고정한다.
/// 화면 코드는 직접 테스트하기 어려우므로, 화면의 사전 검사가 <b>왜</b> 그렇게 돼야 하는지를
/// DB 제약으로 문서화한다.
/// </summary>
public class StudentEnrollmentIntegrityTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public StudentEnrollmentIntegrityTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task 논리삭제된_학생의_StudentID_는_재사용할_수_없다()
    {
        // StudentID 에 UNIQUE 제약이 있고, 학생은 지워도 행이 남는다(묘비).
        // 그래서 화면의 ID 중복 검사는 IsDeleted 를 보면 안 된다 — 보면 "쓸 수 있는 ID"로
        // 판정했다가 저장 단계에서 UNIQUE 위반으로 실패한다.
        // 메서드 이름이 MarkRemovedAsync 인 것도 그 때문이다(46차) — 지우는 것이 아니다.
        using var repo = new StudentRepository(_db.DbPath);

        var student = TestData.NewStudent(name: "삭제될학생");
        await repo.CreateAsync(student);
        Assert.True(await repo.MarkRemovedAsync(student.StudentID));

        var deleted = await repo.GetByIdAsync(student.StudentID);
        Assert.NotNull(deleted);          // 행은 그대로 남아 있다
        Assert.True(deleted!.IsDeleted);

        var reused = TestData.NewStudent(id: student.StudentID, name: "새학생");
        await Assert.ThrowsAsync<SqliteException>(() => repo.CreateAsync(reused));
    }

    [Fact]
    public async Task 같은_학년도_학적은_한_건만_생성된다()
    {
        // UNIQUE(StudentID, SchoolCode, Year) — 학기 컬럼을 없애면서 키가 좁아졌다.
        var sid = await _db.NewStudentInDbAsync("학적중복");
        using var repo = new EnrollmentRepository(_db.DbPath);

        await repo.CreateAsync(TestData.NewEnrollment(sid, number: 1));

        await Assert.ThrowsAsync<SqliteException>(
            () => repo.CreateAsync(TestData.NewEnrollment(sid, number: 2)));
    }

    [Fact]
    public async Task 학년도가_다르면_학적은_따로_생성된다()
    {
        // 학적은 학년도 단위 행이다. 학년이 올라가면 새 행이 생기고, 이력은 그 나열이 된다.
        var sid = await _db.NewStudentInDbAsync("학년도별학적");
        using var repo = new EnrollmentRepository(_db.DbPath);

        await repo.CreateAsync(TestData.NewEnrollment(sid, year: TestData.Year, grade: 1, number: 3));
        await repo.CreateAsync(TestData.NewEnrollment(sid, year: TestData.Year + 1, grade: 2, number: 3));

        var history = await repo.GetHistoryByStudentIdAsync(sid);
        Assert.Equal(2, history.Count);

        // 2학년 행의 기본 변동은 진급이다.
        Assert.Equal(EnrollmentChange.Promoted, history.First(e => e.Grade == 2).ChangeType);
    }
}
