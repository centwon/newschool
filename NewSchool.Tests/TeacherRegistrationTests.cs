using System;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 교사 등록은 <b>교사 행 + 근무 이력</b>이 함께 만들어져야 한다(첫 실행 초기 설정이 쓰는 경로).
///
/// 예전에는 두 Repository 가 각자 연결을 열고 한쪽 트랜잭션을 다른 쪽에 넘겨 주는 모양이었다.
/// 그러면 <c>BaseRepository.CreateCommand</c> 의 "이 연결에서 시작된 트랜잭션인가" 검사에 걸려
/// 트랜잭션이 조용히 무시되고, 이력 INSERT 는 아직 커밋 안 된 교사 행을 못 보는 다른 연결에서
/// 돌았다 — 쓰기 락 대기 끝에 실패하거나 외래키 위반이 났다. 그래서 이 왕복 자체가 회귀 검사다.
/// </summary>
public class TeacherRegistrationTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public TeacherRegistrationTests(SqliteTestFixture db) => _db = db;

    private static (Teacher, TeacherSchoolHistory) NewPair(string teacherId)
    {
        var now = DateTime.Now;

        var teacher = new Teacher
        {
            TeacherID = teacherId,
            LoginID = teacherId,
            Name = "새내기교사",
            Status = "재직",
            HireDate = now.ToString("yyyy-MM-dd"),
            CreatedAt = now,
            UpdatedAt = now
        };

        var history = new TeacherSchoolHistory
        {
            TeacherID = teacherId,
            SchoolCode = TestData.SchoolCode,
            StartDate = now.ToString("yyyy-MM-dd"),
            IsCurrent = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        return (teacher, history);
    }

    [Fact]
    public async Task 교사와_근무이력이_한_번에_저장된다()
    {
        using var service = new TeacherService(_db.DbPath);
        var (teacher, history) = NewPair("T_REG_0001");

        var (success, message, teacherId) = await service.RegisterTeacherAsync(teacher, history);

        Assert.True(success, message);
        Assert.Equal("T_REG_0001", teacherId);

        using var teacherRepo = new TeacherRepository(_db.DbPath);
        Assert.NotNull(await teacherRepo.GetByTeacherIdAsync(teacherId));

        using var historyRepo = new TeacherSchoolHistoryRepository(_db.DbPath);
        var saved = await historyRepo.GetByTeacherIdAsync(teacherId);
        Assert.Single(saved);
        Assert.Equal(TestData.SchoolCode, saved[0].SchoolCode);
    }

    /// <summary>
    /// 이력 저장이 실패하면 교사 행도 남지 않아야 한다. 남으면 재시도 때 TeacherID 를 새로
    /// 만들기 때문에 근무 이력 없는 고아 교사 행이 영영 지워지지 않는다.
    /// </summary>
    [Fact]
    public async Task 이력_저장이_실패하면_교사도_남지_않는다()
    {
        using var service = new TeacherService(_db.DbPath);
        var (teacher, history) = NewPair("T_REG_0002");

        // 없는 학교 코드 → TeacherSchoolHistory 의 School 외래키 위반
        history.SchoolCode = "존재하지않는학교";

        var (success, _, _) = await service.RegisterTeacherAsync(teacher, history);
        Assert.False(success);

        using var teacherRepo = new TeacherRepository(_db.DbPath);
        Assert.Null(await teacherRepo.GetByTeacherIdAsync("T_REG_0002"));
    }
}
