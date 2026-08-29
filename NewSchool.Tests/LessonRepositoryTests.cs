using System;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <c>Lesson</c> 은 <b>교사 시간표의 한 칸</b>이다 — 이 선생이 무슨 요일 몇 교시에 어디서.
///
/// <para>2026-08-29 에 이 표에서 여섯 열(<c>Date</c>·<c>Class</c>·<c>Topic</c>·
/// <c>IsRecurring</c>·<c>IsCompleted</c>·<c>IsCancelled</c>)을 걷어냈다. 채우는 코드가 없어
/// 줄곧 기본값이었고, 그 일들은 게시판 일지와 <c>LessonChange</c> 가 이미 하고 있다.</para>
/// </summary>
public class LessonRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public LessonRepositoryTests(SqliteTestFixture db) => _db = db;

    private static Lesson NewLesson(int courseNo, int year, int semester, int dayOfWeek, int period) => new()
    {
        Course = courseNo,
        Teacher = TestData.TeacherId,
        Year = year,
        Semester = semester,
        DayOfWeek = dayOfWeek,
        Period = period,
        Grade = 1,
        Room = "1-1",
    };

    /// <summary>
    /// 날짜별 조회는 <b>학년도·학기로 반드시 거른다</b>.
    ///
    /// <para>예전에는 교사와 요일만 봤다. 그래서 학년도가 바뀌면 <b>작년 같은 요일 수업이
    /// "오늘의 수업" 에 섞였다</b> — 과목 목록은 올해 것만이라 과목명이 빈 유령 행으로 뜨고,
    /// "N시간 중 M건" 의 N 도 함께 부풀려진다. 배포 첫 해라 드러나지 않았을 뿐,
    /// 첫 학년도 롤오버에 바로 나타날 자리였다.</para>
    /// </summary>
    [Fact]
    public async Task 날짜별_조회는_다른_학년도_수업을_섞지_않는다()
    {
        int thisYear = TestData.Year + 30;
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var repo = new LessonRepository(_db.DbPath);

        int thisCourse = await courseRepo.CreateAsync(TestData.NewCourse(subject: "올해수학", year: thisYear));
        int lastCourse = await courseRepo.CreateAsync(TestData.NewCourse(subject: "작년수학", year: thisYear - 1));

        // 같은 요일(월)·같은 교시에 올해와 작년 수업을 하나씩
        await repo.CreateAsync(NewLesson(thisCourse, thisYear, 1, dayOfWeek: 1, period: 3));
        await repo.CreateAsync(NewLesson(lastCourse, thisYear - 1, 1, dayOfWeek: 1, period: 3));

        var monday = NextMonday();
        var found = await repo.GetByDateAsync(TestData.TeacherId, monday, thisYear, semester: 1);

        Assert.All(found, l => Assert.Equal(thisYear, l.Year));
        Assert.Contains(found, l => l.Course == thisCourse);
        Assert.DoesNotContain(found, l => l.Course == lastCourse);
    }

    /// <summary>학기도 함께 걸러야 한다 — 1학기 시간표가 2학기에 남아 보이면 안 된다.</summary>
    [Fact]
    public async Task 날짜별_조회는_다른_학기_수업을_섞지_않는다()
    {
        int year = TestData.Year + 31;
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var repo = new LessonRepository(_db.DbPath);

        int first = await courseRepo.CreateAsync(TestData.NewCourse(subject: "1학기", year: year, semester: 1));
        int second = await courseRepo.CreateAsync(TestData.NewCourse(subject: "2학기", year: year, semester: 2));

        await repo.CreateAsync(NewLesson(first, year, semester: 1, dayOfWeek: 2, period: 2));
        await repo.CreateAsync(NewLesson(second, year, semester: 2, dayOfWeek: 2, period: 2));

        var tuesday = NextMonday().AddDays(1);
        var found = await repo.GetByDateAsync(TestData.TeacherId, tuesday, year, semester: 2);

        Assert.All(found, l => Assert.Equal(2, l.Semester));
        Assert.DoesNotContain(found, l => l.Course == first);
    }

    /// <summary>
    /// 저장·조회 왕복. 걷어낸 여섯 열이 빠진 뒤에도 남은 여덟 칸이 온전히 오간다.
    /// </summary>
    [Fact]
    public async Task 저장한_칸이_그대로_돌아온다()
    {
        int year = TestData.Year + 32;
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var repo = new LessonRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(year: year));
        var lesson = NewLesson(courseNo, year, semester: 1, dayOfWeek: 3, period: 5);
        lesson.Room = "음악실";

        int no = await repo.CreateAsync(lesson);
        Assert.True(no > 0);

        var loaded = await repo.GetByCourseAsync(courseNo);
        var one = Assert.Single(loaded);

        Assert.Equal(courseNo, one.Course);
        Assert.Equal(TestData.TeacherId, one.Teacher);
        Assert.Equal(year, one.Year);
        Assert.Equal(1, one.Semester);
        Assert.Equal(3, one.DayOfWeek);
        Assert.Equal(5, one.Period);
        Assert.Equal("음악실", one.Room);
    }

    /// <summary>다음 월요일 — 요일 계산이 실행 요일에 흔들리지 않게 고정한다.</summary>
    private static DateTime NextMonday()
    {
        var d = DateTime.Today;
        while (d.DayOfWeek != System.DayOfWeek.Monday) d = d.AddDays(1);
        return d;
    }
}
