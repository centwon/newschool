using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 진도 기록 외래키 회귀 테스트 — 전수 조사 34차.
///
/// <c>LessonProgress</c> 의 두 외래키에 <c>ON DELETE</c> 절이 없어 NO ACTION 이었다.
/// 그래서 진도 기록이 한 건이라도 있으면 수업·단원·시간표 일정 삭제가
/// <c>FOREIGN KEY constraint failed</c> 로 영구 실패했다(진도 매트릭스를 한 번이라도
/// 쓴 수업은 지울 수 없었다). 이제 단원은 CASCADE, 일정은 SET NULL 이다.
/// </summary>
public class LessonProgressCascadeTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public LessonProgressCascadeTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task 진도_기록이_있어도_수업이_삭제된다()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var progressRepo = new LessonProgressRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(subject: "수업삭제", rooms: "1-1"));
        int sectionNo = await sectionRepo.CreateAsync(TestData.NewSection(courseNo));
        Assert.True(await progressRepo.MarkAsCompletedAsync(sectionNo, "1-1"));

        Assert.True(await courseRepo.DeleteAsync(courseNo));
        Assert.Null(await courseRepo.GetByIdAsync(courseNo));

        // 단원과 함께 진도도 사라져야 한다(CASCADE)
        Assert.Null(await progressRepo.GetBySectionAndRoomAsync(sectionNo, "1-1"));
    }

    [Fact]
    public async Task 진도_기록이_있어도_단원이_삭제된다()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var progressRepo = new LessonProgressRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(subject: "단원삭제", rooms: "1-1"));
        int sectionNo = await sectionRepo.CreateAsync(TestData.NewSection(courseNo));
        Assert.True(await progressRepo.MarkAsCompletedAsync(sectionNo, "1-1"));

        Assert.True(await sectionRepo.DeleteAsync(sectionNo));
        Assert.Null(await progressRepo.GetBySectionAndRoomAsync(sectionNo, "1-1"));
    }

    [Fact]
    public async Task 일정을_지워도_진도는_남고_참조만_풀린다()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var progressRepo = new LessonProgressRepository(_db.DbPath);
        using var scheduleRepo = new ScheduleRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(subject: "일정삭제", rooms: "1-1"));
        int sectionNo = await sectionRepo.CreateAsync(TestData.NewSection(courseNo));

        int scheduleNo = await scheduleRepo.CreateAsync(new Schedule
        {
            CourseId = courseNo,
            Room = "1-1",
            Date = DateTime.Today,
            Period = 1,
        });
        Assert.True(scheduleNo > 0);
        Assert.True(await progressRepo.MarkAsCompletedAsync(sectionNo, "1-1", DateTime.Today, scheduleNo));

        Assert.True(await scheduleRepo.DeleteAsync(scheduleNo));

        // 진도 자체는 살아 있어야 한다 — 일정은 부가 정보일 뿐이다
        var progress = await progressRepo.GetBySectionAndRoomAsync(sectionNo, "1-1");
        Assert.NotNull(progress);
        Assert.True(progress!.IsCompleted);
        Assert.Null(progress.ScheduleId);
    }

    /// <summary>
    /// 구 스키마(ON DELETE 절 없음)로 만든 DB 를 초기화기가 재작성해 고치는지.
    /// 가짜 스키마를 손으로 쓰면 초기화기와 어긋나므로, 실제 초기화기로 만든 뒤
    /// 해당 표만 옛 정의로 되돌려 구버전을 재현한다.
    /// </summary>
    [Fact]
    public async Task 구스키마_DB_는_초기화_시_외래키가_교정된다()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "NewSchoolTests", $"fk_{Guid.NewGuid():N}.db");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        try
        {
            using (var init = new NewSchool.Database.DatabaseInitializer(path))
                Assert.True(await init.InitializeAsync());

            // 구 스키마로 되돌리기 + 데이터 한 건
            using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    DROP TABLE LessonProgress;
                    CREATE TABLE LessonProgress (
                        No INTEGER PRIMARY KEY AUTOINCREMENT,
                        CourseSectionId INTEGER NOT NULL,
                        Room TEXT NOT NULL,
                        IsCompleted INTEGER NOT NULL DEFAULT 0,
                        CompletedDate TEXT,
                        ProgressType INTEGER NOT NULL DEFAULT 0,
                        ScheduleId INTEGER,
                        Memo TEXT,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT,
                        FOREIGN KEY (CourseSectionId) REFERENCES CourseSection(No),
                        FOREIGN KEY (ScheduleId) REFERENCES Schedule(No)
                    );
                    """;
                await cmd.ExecuteNonQueryAsync();
            }

            using (var courseRepo = new CourseRepository(path))
            using (var sectionRepo = new CourseSectionRepository(path))
            using (var progressRepo = new LessonProgressRepository(path))
            {
                // FK 부모 시드
                using (var conn = new SqliteConnection($"Data Source={path}"))
                {
                    await conn.OpenAsync();
                    using var seed = conn.CreateCommand();
                    seed.CommandText = """
                        INSERT INTO School (SchoolCode, SchoolName, CreatedAt, UpdatedAt)
                        VALUES (@sc, '학교', @now, @now);
                        INSERT INTO Teacher (TeacherID, LoginID, Name, CreatedAt, UpdatedAt)
                        VALUES (@tid, @tid, '교사', @now, @now);
                        """;
                    seed.Parameters.AddWithValue("@sc", TestData.SchoolCode);
                    seed.Parameters.AddWithValue("@tid", TestData.TeacherId);
                    seed.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    await seed.ExecuteNonQueryAsync();
                }

                int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(rooms: "1-1"));
                int sectionNo = await sectionRepo.CreateAsync(TestData.NewSection(courseNo));
                await progressRepo.MarkAsCompletedAsync(sectionNo, "1-1");

                // 구 스키마에서는 여기서 FK 오류가 났다
                await Assert.ThrowsAsync<SqliteException>(() => courseRepo.DeleteAsync(courseNo));
            }

            // 앱 재시작 = 초기화기 재실행 → 재작성
            using (var init = new NewSchool.Database.DatabaseInitializer(path))
                Assert.True(await init.InitializeAsync());

            using (var courseRepo = new CourseRepository(path))
            using (var progressRepo = new LessonProgressRepository(path))
            {
                var courses = await courseRepo.GetByTeacherAsync(
                    TestData.TeacherId, TestData.Year, 1);
                Assert.Single(courses);

                // 재작성 후에는 기존 데이터를 유지한 채 삭제가 된다
                Assert.True(await courseRepo.DeleteAsync(courses[0].No));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { System.IO.File.Delete(path); } catch { /* 임시 파일 — OS 가 정리 */ }
        }
    }
}
