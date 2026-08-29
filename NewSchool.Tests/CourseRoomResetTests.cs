using System;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 강의실 초기화 — <b>정한 규칙(2026-08-29): 강의실은 정하고 나면 바뀌지 않는 것이 기본</b>이고,
/// 바꾸면 그것은 이름 바꾸기가 아니라 초기화다.
///
/// <para><c>Course.Rooms</c> 는 자유 텍스트인데 그 조각 하나가 네 표의 키 노릇을 한다.
/// DB 에 그 넷을 잇는 제약이 없어, 예전에는 이름을 고치면 아무 경고 없이 갈라졌다 —
/// <c>UNIQUE</c> 키에 <c>Room</c> 이 든 두 표는 UPDATE 가 아니라 <b>새 행</b>을 만들어
/// 두 세대가 쌓였다. 이제 지우고 다시 시작한다.</para>
///
/// <para>여기서 못박는 것은 <b>초기화의 범위</b>다 — 무엇이 지워지고 무엇이 살아남는가.
/// 이게 흔들리면 학생 명단이 통째로 날아가거나, 고아 기록이 다시 생긴다.</para>
/// </summary>
public class CourseRoomResetTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public CourseRoomResetTests(SqliteTestFixture db) => _db = db;

    /// <summary>수업 하나에 배치·시수·진도·배정을 한 벌씩 깔아 둔다.</summary>
    private async Task<(int CourseNo, int EnrollmentNo)> SeedAsync(int year, string room = "1-1")
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var lessonRepo = new LessonRepository(_db.DbPath);
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var progressRepo = new LessonProgressRepository(_db.DbPath);
        using var hoursRepo = new CourseWeeklyHoursRepository(_db.DbPath);
        using var enrollRepo = new EnrollmentRepository(_db.DbPath);
        using var ceRepo = new CourseEnrollmentRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(year: year, rooms: room));

        await lessonRepo.CreateAsync(new Lesson
        {
            Course = courseNo,
            Teacher = TestData.TeacherId,
            Year = year,
            Semester = 1,
            DayOfWeek = 1,
            Period = 3,
            Grade = 1,
            Room = room,
        });

        int sectionNo = await sectionRepo.CreateAsync(TestData.NewSection(courseNo));
        await progressRepo.CreateAsync(new LessonProgress
        {
            CourseSectionId = sectionNo,
            Room = room,
            IsCompleted = true,
        });

        await hoursRepo.UpsertAsync(new CourseWeeklyHours
        {
            CourseNo = courseNo,
            Room = room,
            Week = 1,
            WeekStart = new DateTime(year, 3, 2),
            PlannedHours = 2,
        });

        var sid = await _db.NewStudentInDbAsync("수강생");
        int enrollmentNo = await enrollRepo.CreateAsync(TestData.NewEnrollment(sid, year: year));
        await ceRepo.CreateAsync(new CourseEnrollment
        {
            EnrollmentNo = enrollmentNo,
            CourseNo = courseNo,
            Room = room,
        });

        return (courseNo, enrollmentNo);
    }

    [Fact]
    public async Task 세기는_아무것도_지우지_않는다()
    {
        int year = TestData.Year + 60;
        var (courseNo, _) = await SeedAsync(year);

        var first = await CourseRoomReset.MeasureAsync(_db.DbPath, courseNo);
        var second = await CourseRoomReset.MeasureAsync(_db.DbPath, courseNo);

        Assert.Equal(1, first.Lessons);
        Assert.Equal(1, first.WeeklyHours);
        Assert.Equal(1, first.Progress);
        Assert.Equal(1, first.Enrollments);
        Assert.True(first.HasAny);

        // 두 번 세어도 같아야 한다 — 세는 김에 지우면 확인 다이얼로그가 파괴적이 된다.
        Assert.Equal(first, second);
    }

    /// <summary>
    /// <b>초기화 범위.</b> 배치·시수·진도는 지우고, <b>학생 배정은 살린다</b>.
    ///
    /// <para>앞의 셋은 강의실이 정해져야 뜻이 생기는 파생 기록이다. 특히 <c>Lesson</c> 은
    /// <c>Room</c> 이 "그 칸이 어느 학급 수업인지" 를 말하는 유일한 값이라, 이름이 뜻을 잃으면
    /// 칸도 뜻을 잃는다. 반면 수강 배정은 <b>학생 명단</b>이고 <c>Room</c> 은
    /// <c>UNIQUE(EnrollmentNo, CourseNo)</c> 에 없는 속성이다 — 강의실 이름이 바뀌었다고
    /// 수강생을 명단에서 뺄 이유가 없다.</para>
    /// </summary>
    [Fact]
    public async Task 초기화는_배치_시수_진도를_지우고_학생_배정은_살린다()
    {
        int year = TestData.Year + 61;
        var (courseNo, _) = await SeedAsync(year);

        await CourseRoomReset.ExecuteAsync(_db.DbPath, courseNo);

        using var lessonRepo = new LessonRepository(_db.DbPath);
        using var progressRepo = new LessonProgressRepository(_db.DbPath);
        using var hoursRepo = new CourseWeeklyHoursRepository(_db.DbPath);
        using var ceRepo = new CourseEnrollmentRepository(_db.DbPath);

        Assert.Empty(await lessonRepo.GetByCourseAsync(courseNo));
        Assert.Empty(await progressRepo.GetByCourseAsync(courseNo));
        Assert.Empty(await hoursRepo.GetByCourseAsync(courseNo));

        // 배정은 남고, 강의실 지정만 비워진다.
        var enrollments = await ceRepo.GetByCourseAsync(courseNo);
        var one = Assert.Single(enrollments);
        Assert.Equal(string.Empty, one.Room ?? string.Empty);
    }

    /// <summary>초기화는 <b>그 수업만</b> 건드린다. 옆 수업까지 지우면 재앙이다.</summary>
    [Fact]
    public async Task 초기화는_다른_수업을_건드리지_않는다()
    {
        int year = TestData.Year + 62;
        var (target, _) = await SeedAsync(year);
        var (bystander, _) = await SeedAsync(year, room: "2-1");

        await CourseRoomReset.ExecuteAsync(_db.DbPath, target);

        using var lessonRepo = new LessonRepository(_db.DbPath);
        using var progressRepo = new LessonProgressRepository(_db.DbPath);

        Assert.Empty(await lessonRepo.GetByCourseAsync(target));
        Assert.Single(await lessonRepo.GetByCourseAsync(bystander));
        Assert.Single(await progressRepo.GetByCourseAsync(bystander));
    }

    /// <summary>
    /// 순서만 다르거나 공백만 다른 목록은 <b>같은 목록</b>이다 — 지울 이유가 없다.
    /// 이걸 다르다고 보면 교사가 칸을 스치기만 해도 기록이 날아간다.
    /// </summary>
    [Theory]
    [InlineData("1-1,1-2", "1-1,1-2", true)]
    [InlineData("1-1,1-2", "1-2, 1-1", true)]
    [InlineData("1-1, 1-2 ", " 1-1,1-2", true)]
    [InlineData("1-1,1-2", "1-1", false)]
    [InlineData("1-1", "1학년 1반", false)]
    [InlineData("1-1", "", false)]
    public void 같은_강의실_목록인지_가린다(string a, string b, bool expected)
    {
        Assert.Equal(expected, CourseRoomReset.SameRooms(a, b));
    }

    [Fact]
    public async Task 딸린_기록이_없으면_잠글_이유가_없다()
    {
        int year = TestData.Year + 63;
        using var courseRepo = new CourseRepository(_db.DbPath);
        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(year: year));

        var impact = await CourseRoomReset.MeasureAsync(_db.DbPath, courseNo);

        Assert.False(impact.HasAny);
        Assert.Equal(0, impact.Deleted);
    }
}
