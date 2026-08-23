using System;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 학생 추가는 <b>Student 행 + Enrollment 행</b>이 함께 만들어져야 한다(<c>AddStudentsPage</c>).
///
/// 두 리포지토리가 각자 연결을 열면 트랜잭션이 공유되지 않아 한쪽만 남는다. 그래서 화면은
/// 오랫동안 학적 INSERT 를 손수 SQL 로 적어 연결을 공유했는데, 이제는 학적 리포지토리를
/// 같은 연결로 만들어 쓴다(2026-08-23). 그 왕복과 롤백을 여기서 묶어 둔다.
/// </summary>
public class StudentEnrollmentAtomicityTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public StudentEnrollmentAtomicityTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task 학생과_학적이_한_트랜잭션으로_함께_저장된다()
    {
        var student = TestData.NewStudent(name: "함께저장");

        using (var studentRepo = new StudentRepository(_db.DbPath))
        using (var enrollmentRepo = new EnrollmentRepository(studentRepo.GetConnection()))
        {
            studentRepo.BeginTransaction();
            enrollmentRepo.SetTransaction(studentRepo.GetTransaction());

            await studentRepo.CreateAsync(student);

            // 학적은 Student 를 외래키로 참조한다 — 같은 트랜잭션 안이라 아직 커밋되지 않은
            // 학생 행이 보여야 통과한다(연결이 갈라져 있으면 여기서 실패한다).
            int no = await enrollmentRepo.CreateAsync(
                TestData.NewEnrollment(student.StudentID, "함께저장", classNum: 4, number: 11));
            Assert.True(no > 0);

            studentRepo.Commit();
        }

        using var check = new EnrollmentRepository(_db.DbPath);
        var saved = await check.GetHistoryByStudentIdAsync(student.StudentID);
        Assert.Single(saved);
    }

    /// <summary>
    /// 학적 저장이 실패하면 학생 행도 남지 않아야 한다. 남으면 같은 번호로 다시 추가할 때
    /// StudentID 가 이미 있어 화면이 영영 저장하지 못한다.
    /// </summary>
    [Fact]
    public async Task 학적_저장이_실패하면_학생도_남지_않는다()
    {
        var student = TestData.NewStudent(name: "롤백대상");

        using (var studentRepo = new StudentRepository(_db.DbPath))
        using (var enrollmentRepo = new EnrollmentRepository(studentRepo.GetConnection()))
        {
            studentRepo.BeginTransaction();
            enrollmentRepo.SetTransaction(studentRepo.GetTransaction());

            await studentRepo.CreateAsync(student);

            // 없는 학교 코드 → Enrollment 의 School 외래키 위반
            var bad = TestData.NewEnrollment(student.StudentID, "롤백대상", classNum: 4, number: 12);
            bad.SchoolCode = "존재하지않는학교";

            await Assert.ThrowsAnyAsync<Exception>(() => enrollmentRepo.CreateAsync(bad));

            studentRepo.Rollback();
        }

        using var check = new StudentRepository(_db.DbPath);
        Assert.Null(await check.GetByIdAsync(student.StudentID));
    }
}
