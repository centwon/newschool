using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 삭제 축 감사(46차) — <b>지우면 무엇이 따라 사라지는가</b>.
///
/// <para>이 축은 스키마만 봐서는 판정할 수 없다. FK 선언이 <c>DatabaseInitializer</c> 가 아니라
/// <b>각 저장소 파일에 흩어져</b> 있고(감사 중 실제로 셋만 보고 오판할 뻔했다), 논리 삭제
/// (<c>UPDATE IsDeleted=1</c>)를 쓰는 테이블은 CASCADE 가 <b>아예 발동하지 않는다</b>.
/// 그래서 화면 문구가 맞는지는 실제로 지워 보고 세는 수밖에 없다.</para>
/// </summary>
public class DeleteCascadeTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public DeleteCascadeTests(SqliteTestFixture db) => _db = db;

    /// <summary>
    /// <b>단원을 지우면 그 단원의 진도가 함께 사라진다.</b>
    ///
    /// <para>코드 주석은 오랫동안 "개별 삭제 (연관 데이터 보존)" 이라고 적혀 있었고 확인
    /// 문구도 진도 얘기를 하지 않았다 — 정확히 반대였다. 사용자에게 알리는 말이 이 동작을
    /// 따라야 하므로 동작 자체를 못박는다.</para>
    /// </summary>
    [Fact]
    public async Task 단원을_지우면_진도도_사라진다()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var progressRepo = new LessonProgressRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse());
        int sectionNo = await sectionRepo.CreateAsync(TestData.NewSection(courseNo));

        await progressRepo.CreateAsync(new LessonProgress
        {
            CourseSectionId = sectionNo,
            Room = "1-1",
            IsCompleted = true,
        });

        Assert.NotEmpty(await progressRepo.GetByCourseAsync(courseNo));

        Assert.True(await sectionRepo.DeleteAsync(sectionNo));

        // CASCADE 로 진도가 따라 사라져야 한다
        Assert.Empty(await progressRepo.GetByCourseAsync(courseNo));
    }

    /// <summary>
    /// <b>수업을 지우면 단원·진도·시간표 배치가 함께 사라진다.</b>
    /// 수업 삭제 확인 문구가 약속하는 내용이다.
    /// </summary>
    [Fact]
    public async Task 수업을_지우면_단원과_진도가_사라진다()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var progressRepo = new LessonProgressRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse());
        int sectionNo = await sectionRepo.CreateAsync(TestData.NewSection(courseNo));
        await progressRepo.CreateAsync(new LessonProgress
        {
            CourseSectionId = sectionNo,
            Room = "1-1",
            IsCompleted = true,
        });

        Assert.True(await courseRepo.DeleteAsync(courseNo));

        Assert.Empty(await sectionRepo.GetByCourseAsync(courseNo));
        Assert.Empty(await progressRepo.GetByCourseAsync(courseNo));
    }

    /// <summary>
    /// <b>동아리 삭제는 논리 삭제라 부원 배정이 남는다.</b>
    ///
    /// <para><c>ClubEnrollment.ClubNo</c> 에는 <c>ON DELETE CASCADE</c> 가 걸려 있지만
    /// <c>ClubRepository.DeleteAsync</c> 가 행을 지우지 않고 <c>IsDeleted=1</c> 만 세우므로
    /// <b>발동하지 않는다.</b> 확인 문구("부원 배정 기록은 그대로 보관됩니다")가 이것에 기대고
    /// 있으므로, 물리 삭제로 바꾸면 그 문구가 거짓이 된다.</para>
    /// </summary>
    [Fact]
    public async Task 동아리_삭제는_부원_배정을_남긴다()
    {
        using var clubRepo = new ClubRepository(_db.DbPath);
        using var ceRepo = new ClubEnrollmentRepository(_db.DbPath);
        using var enrollRepo = new EnrollmentRepository(_db.DbPath);

        var studentId = await _db.NewStudentInDbAsync("동아리원");
        int enrollNo = await enrollRepo.CreateAsync(TestData.NewEnrollment(studentId, name: "동아리원"));

        int clubNo = await clubRepo.CreateAsync(new Club
        {
            SchoolCode = TestData.SchoolCode,
            TeacherID = TestData.TeacherId,
            Year = TestData.Year,
            ClubName = "천체관측반",
        });

        await ceRepo.CreateAsync(new ClubEnrollment { EnrollmentNo = enrollNo, ClubNo = clubNo });

        Assert.True(await clubRepo.DeleteAsync(clubNo));

        // 목록에서는 사라지지만
        Assert.Null(await clubRepo.GetByIdAsync(clubNo));
        // 배정 기록은 남아 있어야 한다
        Assert.NotEmpty(await ceRepo.GetByClubAsync(clubNo));
    }
}
