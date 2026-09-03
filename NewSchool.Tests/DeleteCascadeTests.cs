using System;
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
    /// <b>지운 학사일정은 다시 받아도 되살아나지 않는다.</b>
    ///
    /// <para>학사일정이 행을 남기는 이유가 바로 이 묘비다 — NEIS 에서 다시 받을 때
    /// 사용자가 지운 일정이 돌아오면 안 된다. 그런데 중복 판정이 <c>IsDeleted = 0</c> 인
    /// 행만 세어 <b>지운 일정을 "없는 것" 으로 보고 매번 되살렸다.</b> 논리 삭제를 쓰면서
    /// 정작 그 목적을 이루지 못하고 있었다(46차).</para>
    ///
    /// <para>동기화가 타는 길은 <c>CreateBulkAsync</c> 다 — 한 건짜리 <c>CreateAsync</c> 는
    /// 중복을 보지 않고 그대로 넣는다. 그래서 실제 경로로 확인한다.</para>
    /// </summary>
    [Fact]
    public async Task 지운_학사일정은_다시_받아도_되살아나지_않는다()
    {
        using var repo = new SchoolScheduleRepository(_db.DbPath);

        // ⚠ 픽스처(DB)를 클래스 안에서 공유하므로 테스트마다 다른 날짜를 쓴다 —
        //   같은 키를 쓰면 앞 테스트가 넣어 둔 행 때문에 결과가 뒤바뀐다.
        var date = new DateTime(TestData.Year, 5, 15);

        int no = await repo.CreateAsync(TestData.NewSchedule(date, eventName: "지운행사"));
        Assert.True(no > 0);

        // 사용자가 지운다(행은 남고 표시만 선다)
        Assert.True(await repo.MarkRemovedAsync(no));

        // NEIS 에서 같은 일정을 다시 받아 넣으려 한다 — 중복으로 걸러져야 한다.
        int added = await repo.CreateBulkAsync([TestData.NewSchedule(date, eventName: "지운행사")]);

        Assert.Equal(0, added);
    }

    /// <summary>지우지 않은 일정은 당연히 중복으로 걸러진다 — 위 테스트의 대조군.</summary>
    [Fact]
    public async Task 살아있는_학사일정도_중복으로_걸러진다()
    {
        using var repo = new SchoolScheduleRepository(_db.DbPath);

        var date = new DateTime(TestData.Year, 5, 16);   // 위 테스트와 다른 날짜(픽스처 공유)

        Assert.True(await repo.CreateAsync(TestData.NewSchedule(date, eventName: "남은행사")) > 0);
        Assert.Equal(0, await repo.CreateBulkAsync([TestData.NewSchedule(date, eventName: "남은행사")]));
    }

    /// <summary>
    /// <b>동아리 감추기는 부원 배정을 남긴다.</b>
    ///
    /// <para><c>ClubEnrollment.ClubNo</c> 에는 <c>ON DELETE CASCADE</c> 가 걸려 있지만
    /// <c>ClubRepository.HideAsync</c> 가 행을 지우지 않고 <c>IsDeleted=1</c> 만 세우므로
    /// <b>발동하지 않는다.</b> 확인 문구("부원 배정 기록은 그대로 보관됩니다")가 이것에 기대고
    /// 있으므로, 진짜 삭제로 바꾸면 그 문구가 거짓이 된다 — 그때 이 테스트가 먼저 깨진다.</para>
    ///
    /// <para>이름을 <c>Delete</c> 에서 <c>Hide</c> 로 바꾼 것이 46차의 결론이다:
    /// 한 이름이 "지운다" 와 "감춘다" 두 가지를 가리키는 동안에는, 스키마를 읽는 사람과
    /// 코드를 읽는 사람이 서로 다른 결론에 이른다.</para>
    /// </summary>
    [Fact]
    public async Task 동아리_감추기는_부원_배정을_남긴다()
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

        Assert.True(await clubRepo.HideAsync(clubNo));

        // 목록에서는 사라지지만
        Assert.Null(await clubRepo.GetByIdAsync(clubNo));
        // 배정 기록은 남아 있어야 한다
        Assert.NotEmpty(await ceRepo.GetByClubAsync(clubNo));
    }
}
