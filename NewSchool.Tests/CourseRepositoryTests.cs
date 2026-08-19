using System.Threading.Tasks;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>Course + CourseSection 리포지토리 테스트 (TEST_PLAN 1단계).</summary>
public class CourseRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public CourseRepositoryTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task Course_CRUD_왕복()
    {
        using var repo = new CourseRepository(_db.DbPath);

        int no = await repo.CreateAsync(TestData.NewCourse(subject: "물리", grade: 2, rooms: "2-1,2-3"));
        Assert.True(no > 0);

        var loaded = await repo.GetByIdAsync(no);
        Assert.Equal("물리", loaded!.Subject);
        Assert.Equal("2-1,2-3", loaded.Rooms);
        Assert.Equal(TestData.TeacherId, loaded.TeacherID);

        loaded.Subject = "화학";
        Assert.True(await repo.UpdateAsync(loaded));
        Assert.Equal("화학", (await repo.GetByIdAsync(no))!.Subject);

        Assert.True(await repo.DeleteAsync(no));
        Assert.Null(await repo.GetByIdAsync(no));
    }

    [Fact]
    public async Task GetByTeacher_연도학기_필터()
    {
        using var repo = new CourseRepository(_db.DbPath);
        int year = TestData.Year + 3; // 격리용 별도 연도
        await repo.CreateAsync(TestData.NewCourse(subject: "국어A", year: year, semester: 1));
        await repo.CreateAsync(TestData.NewCourse(subject: "국어B", year: year, semester: 2));

        var s1 = await repo.GetByTeacherAsync(TestData.TeacherId, year, 1);
        Assert.Single(s1);
        Assert.Equal("국어A", s1[0].Subject);
    }

    [Fact]
    public async Task GetByIds_배치조회_빈목록과_다건()
    {
        using var repo = new CourseRepository(_db.DbPath);
        int no1 = await repo.CreateAsync(TestData.NewCourse(subject: "배치A"));
        int no2 = await repo.CreateAsync(TestData.NewCourse(subject: "배치B"));

        var empty = await repo.GetByIdsAsync([]);
        Assert.Empty(empty);

        var two = await repo.GetByIdsAsync([no1, no2, 999_999]);
        Assert.Equal(2, two.Count);
    }

    [Fact]
    public async Task Section_수정_왕복()
    {
        // 회귀 방지: UPDATE 문 마지막 필드 뒤에 후행 쉼표가 있어 SQL 문법 오류로
        //   단원 수정이 100% 실패했다(예외가 그대로 올라와 다이얼로그가 저장 실패로 끝남).
        using var courseRepo = new CourseRepository(_db.DbPath);
        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(subject: "단원수정과목"));

        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        int no = await sectionRepo.CreateAsync(
            TestData.NewSection(courseNo, unitNo: 1, sectionNo: 1, sectionName: "원래 절", hours: 2));
        Assert.True(no > 0);

        var section = await sectionRepo.GetByIdAsync(no);
        Assert.NotNull(section);
        section!.SectionName = "고친 절";
        section.EstimatedHours = 5;
        section.SortOrder = 3;

        Assert.True(await sectionRepo.UpdateAsync(section));

        var updated = await sectionRepo.GetByIdAsync(no);
        Assert.Equal("고친 절", updated!.SectionName);
        Assert.Equal(5, updated.EstimatedHours);
        Assert.Equal(3, updated.SortOrder);
    }

    [Fact]
    public async Task Section_일괄생성_조회_시수합계()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(subject: "단원과목"));

        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        int created = await sectionRepo.BulkCreateAsync(courseNo,
        [
            TestData.NewSection(courseNo, unitNo: 1, sectionNo: 1, sectionName: "1-1절", hours: 2),
            TestData.NewSection(courseNo, unitNo: 1, sectionNo: 2, sectionName: "1-2절", hours: 3),
        ]);
        Assert.Equal(2, created);

        var sections = await sectionRepo.GetByCourseAsync(courseNo);
        Assert.Equal(2, sections.Count);

        int totalHours = await sectionRepo.GetTotalEstimatedHoursAsync(courseNo);
        Assert.Equal(5, totalHours);
    }

}
