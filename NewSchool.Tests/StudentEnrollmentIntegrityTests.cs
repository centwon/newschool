using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
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
        // StudentID 에 UNIQUE 제약이 있고 삭제는 논리 삭제(행이 남는다)다.
        // 그래서 화면의 ID 중복 검사는 IsDeleted 를 보면 안 된다 — 보면 "쓸 수 있는 ID"로
        // 판정했다가 저장 단계에서 UNIQUE 위반으로 실패한다.
        using var repo = new StudentRepository(_db.DbPath);

        var student = TestData.NewStudent(name: "삭제될학생");
        await repo.CreateAsync(student);
        Assert.True(await repo.DeleteAsync(student.StudentID));   // 논리 삭제

        var deleted = await repo.GetByIdAsync(student.StudentID);
        Assert.NotNull(deleted);          // 행은 그대로 남아 있다
        Assert.True(deleted!.IsDeleted);

        var reused = TestData.NewStudent(id: student.StudentID, name: "새학생");
        await Assert.ThrowsAsync<SqliteException>(() => repo.CreateAsync(reused));
    }

    [Fact]
    public async Task 같은_학년도_학기_학적은_한_건만_생성된다()
    {
        // UNIQUE(StudentID, SchoolCode, Year, Semester)
        var sid = await _db.NewStudentInDbAsync("학적중복");
        using var repo = new EnrollmentRepository(_db.DbPath);

        await repo.CreateAsync(TestData.NewEnrollment(sid, semester: 1, number: 1));

        await Assert.ThrowsAsync<SqliteException>(
            () => repo.CreateAsync(TestData.NewEnrollment(sid, semester: 1, number: 2)));
    }

    [Fact]
    public async Task 같은_학생이라도_학기가_다르면_학적은_따로_생성된다()
    {
        // 학적은 (학년도, 학기) 단위 행이다. 그래서 등록 화면이 학기를 정확히 지정해야 하고,
        // 번호 중복 검사도 학기별로 해야 한다(예전에는 학기를 안 봐서 1학기 번호 때문에
        // 같은 학년도 2학기 등록이 막혔다).
        var sid = await _db.NewStudentInDbAsync("학기별학적");
        using var repo = new EnrollmentRepository(_db.DbPath);

        await repo.CreateAsync(TestData.NewEnrollment(sid, semester: 1, number: 3));
        await repo.CreateAsync(TestData.NewEnrollment(sid, semester: 2, number: 3));

        var history = await repo.GetHistoryByStudentIdAsync(sid);
        Assert.Equal(2, history.Count);
    }
}
