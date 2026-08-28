using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 수업·동아리 배정이 <b>학생이 아니라 학적</b>을 가리킨다는 것의 회귀.
///
/// <para>예전에는 <c>StudentID</c> 로 학생을 직접 가리켜 "어느 학년도의 배정인가" 가 관계에
/// 담기지 않았다. 그래서 <b>전출한 학생이 그 해 수업·동아리 명단에 계속 남았고</b>, 막을
/// 장치가 조회 필터밖에 없었다. 이제 FK 가 구조로 막는다.</para>
///
/// <para>설계 근거는 <c>docs/enrollment-redesign.md</c> 6장.</para>
/// </summary>
public sealed class AssignmentFollowsEnrollmentTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public AssignmentFollowsEnrollmentTests(SqliteTestFixture db) => _db = db;

    private async Task<int> NewClubInDbAsync(int year)
    {
        using var con = new SqliteConnection($"Data Source={_db.DbPath}");
        await con.OpenAsync();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Club (SchoolCode, TeacherID, Year, ClubName, CreatedAt, UpdatedAt, IsDeleted)
            VALUES (@sc, @tid, @y, '테스트동아리', '2026-01-01', '2026-01-01', 0);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@sc", TestData.SchoolCode);
        cmd.Parameters.AddWithValue("@tid", TestData.TeacherId);
        cmd.Parameters.AddWithValue("@y", year);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // ── 수업 ──────────────────────────────────────────────────

    [Fact]
    public async Task 전출하면_수업_명단에서_빠진다()
    {
        int year = TestData.Year + 50;
        using var enrollRepo = new EnrollmentRepository(_db.DbPath);
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var ceRepo = new CourseEnrollmentRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(year: year));

        var staying = await _db.NewStudentInDbAsync("남는학생");
        var leaving = await _db.NewStudentInDbAsync("전출학생");
        int stayNo = await enrollRepo.CreateAsync(TestData.NewEnrollment(staying, "남는학생", year: year, number: 1));
        int leftNo = await enrollRepo.CreateAsync(TestData.NewEnrollment(leaving, "전출학생", year: year, number: 2));

        await ceRepo.CreateAsync(new CourseEnrollment { EnrollmentNo = stayNo, CourseNo = courseNo });
        await ceRepo.CreateAsync(new CourseEnrollment { EnrollmentNo = leftNo, CourseNo = courseNo });

        Assert.Equal(2, (await ceRepo.GetByCourseAsync(courseNo)).Count);

        // 전출시킨다 — 수업 배정을 손대지 않는데도 명단에서 빠져야 한다.
        await enrollRepo.ApplyChangeAsync(leftNo, EnrollmentChange.TransferredOut, new DateTime(year, 5, 10));

        var roster = await ceRepo.GetByCourseAsync(courseNo);
        Assert.Single(roster);
        Assert.Equal(staying, roster[0].StudentID);

        // 그래도 배정 자체는 남아 있다 — 그 해에 실제로 들었던 수업이다.
        Assert.Equal(2, (await ceRepo.GetByCourseAsync(courseNo, includeInactive: true)).Count);
    }

    [Fact]
    public async Task 수업_배정은_학생이_아니라_학적을_가리킨다()
    {
        int year = TestData.Year + 51;
        using var enrollRepo = new EnrollmentRepository(_db.DbPath);
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var ceRepo = new CourseEnrollmentRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(year: year));
        var sid = await _db.NewStudentInDbAsync("이름확인");
        int no = await enrollRepo.CreateAsync(TestData.NewEnrollment(sid, "엉뚱한이름", year: year, number: 3));

        await ceRepo.CreateAsync(new CourseEnrollment { EnrollmentNo = no, CourseNo = courseNo });

        var loaded = (await ceRepo.GetByCourseAsync(courseNo))[0];

        Assert.Equal(no, loaded.EnrollmentNo);
        Assert.Equal(sid, loaded.StudentID);      // 학적을 거쳐 온 값
        Assert.Equal("이름확인", loaded.Name);     // Student 가 정본
    }

    [Fact]
    public async Task 학적을_지우면_수업_배정도_함께_사라진다()
    {
        // ON DELETE CASCADE. 예전에는 학생을 지워도 배정이 남아 고아가 됐다.
        int year = TestData.Year + 52;
        using var enrollRepo = new EnrollmentRepository(_db.DbPath);
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var ceRepo = new CourseEnrollmentRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(year: year));
        var sid = await _db.NewStudentInDbAsync("삭제될학생");
        int no = await enrollRepo.CreateAsync(TestData.NewEnrollment(sid, "삭제될학생", year: year, number: 4));
        await ceRepo.CreateAsync(new CourseEnrollment { EnrollmentNo = no, CourseNo = courseNo });

        Assert.Single(await ceRepo.GetByCourseAsync(courseNo));

        await enrollRepo.DeleteAsync(no);

        Assert.Empty(await ceRepo.GetByCourseAsync(courseNo, includeInactive: true));
    }

    [Fact]
    public async Task 같은_학적을_같은_수업에_두_번_넣을_수_없다()
    {
        int year = TestData.Year + 53;
        using var enrollRepo = new EnrollmentRepository(_db.DbPath);
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var ceRepo = new CourseEnrollmentRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(year: year));
        var sid = await _db.NewStudentInDbAsync("중복");
        int no = await enrollRepo.CreateAsync(TestData.NewEnrollment(sid, "중복", year: year, number: 5));

        await ceRepo.CreateAsync(new CourseEnrollment { EnrollmentNo = no, CourseNo = courseNo });

        await Assert.ThrowsAsync<SqliteException>(
            () => ceRepo.CreateAsync(new CourseEnrollment { EnrollmentNo = no, CourseNo = courseNo }));
    }

    // ── 동아리 ────────────────────────────────────────────────

    [Fact]
    public async Task 전출하면_동아리_명단에서도_빠진다()
    {
        int year = TestData.Year + 54;
        using var enrollRepo = new EnrollmentRepository(_db.DbPath);
        using var ceRepo = new ClubEnrollmentRepository(_db.DbPath);

        int clubNo = await NewClubInDbAsync(year);

        var staying = await _db.NewStudentInDbAsync("동아리남음");
        var leaving = await _db.NewStudentInDbAsync("동아리전출");
        int stayNo = await enrollRepo.CreateAsync(TestData.NewEnrollment(staying, "동아리남음", year: year, number: 6));
        int leftNo = await enrollRepo.CreateAsync(TestData.NewEnrollment(leaving, "동아리전출", year: year, number: 7));

        await ceRepo.CreateAsync(new ClubEnrollment { EnrollmentNo = stayNo, ClubNo = clubNo });
        await ceRepo.CreateAsync(new ClubEnrollment { EnrollmentNo = leftNo, ClubNo = clubNo });

        Assert.Equal(2, (await ceRepo.GetByClubAsync(clubNo)).Count);

        await enrollRepo.ApplyChangeAsync(leftNo, EnrollmentChange.TransferredOut, new DateTime(year, 5, 10));

        var members = await ceRepo.GetByClubAsync(clubNo);
        Assert.Single(members);
        Assert.Equal(staying, members[0].StudentID);
    }

    [Fact]
    public async Task 졸업해도_명단에서_빠진다()
    {
        // 전출만이 아니다 — 명단에 들어가는지는 IsActive 하나가 정한다.
        int year = TestData.Year + 55;
        using var enrollRepo = new EnrollmentRepository(_db.DbPath);
        using var ceRepo = new ClubEnrollmentRepository(_db.DbPath);

        int clubNo = await NewClubInDbAsync(year);
        var sid = await _db.NewStudentInDbAsync("졸업생");
        int no = await enrollRepo.CreateAsync(TestData.NewEnrollment(sid, "졸업생", year: year, number: 8));
        await ceRepo.CreateAsync(new ClubEnrollment { EnrollmentNo = no, ClubNo = clubNo });

        await enrollRepo.ApplyChangeAsync(no, EnrollmentChange.Graduated, new DateTime(year + 1, 2, 28));

        Assert.Empty(await ceRepo.GetByClubAsync(clubNo));
        Assert.Single(await ceRepo.GetByClubAsync(clubNo, includeInactive: true));
    }
}
