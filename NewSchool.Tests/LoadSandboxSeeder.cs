using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;
using BoardInitializer = NewSchool.Board.DatabaseInitializer;
using SchedulerInitializer = NewSchool.Scheduler.DatabaseInitializer;

namespace NewSchool.Tests;

/// <summary>
/// <b>축 "많을 때·길 때" 의 모래상자를 만든다.</b>
///
/// <para>이 개발 PC 의 진짜 자료는 <b>한 반 17명</b> 이라, 크기에 따라 달라지는 결함은
/// 여태 눈앞에 온 적이 없다(53차 인쇄도 그 반으로만 실측했다). 그렇다고 사용자의 실제
/// 자료를 불릴 수는 없으므로, <b>디버그 산출물 옆에 별도 데이터 폴더</b>를 만들어 거기에
/// 크게 심는다 — 앱은 실행 파일 옆에 <c>portable.txt</c> 가 있으면 그 폴더를 쓴다
/// (<see cref="NewSchool.Settings.IsPortableLayout"/>).</para>
///
/// <para>⚠ 이 파일은 <b>시험이 아니라 도구</b>다. 환경변수 <c>NEWSCHOOL_SEED_DIR</c> 가
/// 없으면 아무 일도 하지 않는다 — 평소 테스트 실행에 끼어들지 않게 하기 위해서다.
/// 쓸 때: <c>NEWSCHOOL_SEED_DIR=... dotnet test --filter 모래상자</c></para>
///
/// <para>⚠ 앱의 정적 경로(<c>SchoolDatabase.DbPath</c>·<c>Settings.*</c>)는 <b>절대 쓰지 않는다</b>.
/// 그것을 읽는 순간 진짜 사용자 폴더를 가리키기 때문이다. 모든 경로는 인자로만 준다.</para>
/// </summary>
public class LoadSandboxSeeder
{
    private const int StudentCount = 40;
    private const int PostCount = 500;
    private const int LogCount = 300;

    [Fact]
    public async Task 모래상자에_많고_긴_자료를_심는다()
    {
        string? dir = Environment.GetEnvironmentVariable("NEWSCHOOL_SEED_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;   // 평소에는 하는 일이 없다

        Directory.CreateDirectory(dir);
        string school = Path.Combine(dir, "school.db");
        string board = Path.Combine(dir, "board.db");
        string scheduler = Path.Combine(dir, "scheduler.db");
        string settings = Path.Combine(dir, "Settings.db");

        SQLitePCL.Batteries_V2.Init();

        using (var init = new NewSchool.Database.DatabaseInitializer(school))
            Assert.True(await init.InitializeAsync(), "school.db 초기화 실패");
        using (var init = new BoardInitializer(board))
            Assert.True(await init.InitializeAsync(), "board.db 초기화 실패");
        using (var init = new SchedulerInitializer(scheduler))
            Assert.True(await init.InitializeAsync(), "scheduler.db 초기화 실패");

        await SeedSettingsAsync(settings);
        await SeedSchoolAndTeacherAsync(school);
        var studentIds = await SeedStudentsAsync(school);
        await SeedLongRecordsAsync(school, studentIds[0]);
        await SeedTimetableAndCourseAsync(school);
        await SeedPostsAsync(board);

        // 2차(같은 날 이어서): 남은 덩어리들
        await SeedSpecsAsync(school, studentIds);
        await SeedSchoolSchedulesAsync(school);
        await SeedClassDiariesAsync(school);
        await SeedCalendarEventsAsync(scheduler);

        SqliteConnection.ClearAllPools();
    }

    /// <summary>초기 설정 창을 건너뛰려면 <c>SchoolCode</c> 만 있으면 된다(App.OnLaunched).</summary>
    private static async Task SeedSettingsAsync(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();

        using (var create = conn.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY, Value TEXT NOT NULL, Type TEXT NOT NULL,
                    Description TEXT, Updated TEXT NOT NULL)
                """;
            await create.ExecuteNonQueryAsync();
        }

        var values = new (string Key, string Value)[]
        {
            ("SchoolCode", TestData.SchoolCode),
            ("SchoolName", "많을때길때중학교"),
            ("ProvinceCode", "B10"),
            ("WorkYear", TestData.Year.ToString()),
            ("WorkSemester", "2"),
            ("User", TestData.TeacherId),
            ("UserName", "부하시험"),
            ("HomeGrade", "3"),
            ("HomeRoom", "1"),
        };

        foreach (var (key, value) in values)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Settings (Key, Value, Type, Description, Updated)
                VALUES (@k, @v, '', '', @u)
                ON CONFLICT(Key) DO UPDATE SET Value = @v, Updated = @u
                """;
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@v", value);
            cmd.Parameters.AddWithValue("@u", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedSchoolAndTeacherAsync(string school)
    {
        using var conn = new SqliteConnection($"Data Source={school}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO School (SchoolCode, SchoolName, CreatedAt, UpdatedAt)
            VALUES (@sc, '많을때길때중학교', @now, @now);
            INSERT OR REPLACE INTO Teacher (TeacherID, LoginID, Name, CreatedAt, UpdatedAt)
            VALUES (@tid, @tid, '부하시험', @now, @now);
            """;
        cmd.Parameters.AddWithValue("@sc", TestData.SchoolCode);
        cmd.Parameters.AddWithValue("@tid", TestData.TeacherId);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>3학년 1반에 40명. 이름은 길이가 제각각이어야 좁은 칸이 어디서 깨지는지 보인다.</summary>
    private static async Task<List<string>> SeedStudentsAsync(string school)
    {
        string[] surnames = { "김", "이", "박", "최", "정", "강", "조", "윤", "장", "임" };
        string[] givens = { "서준", "하윤", "도윤", "지호", "예린", "시우", "하은", "주원", "지안", "건우" };

        using var students = new StudentRepository(school);
        using var enrollments = new EnrollmentRepository(school);

        var ids = new List<string>();
        for (int i = 1; i <= StudentCount; i++)
        {
            // 20번은 일부러 아주 긴 이름 — 좌석 카드·명렬표의 좁은 칸을 시험한다.
            string name = i == 20
                ? "남궁억숙자예쁜이"
                : surnames[i % surnames.Length] + givens[(i * 3) % givens.Length];

            var student = TestData.NewStudent(name: name, sex: i % 2 == 0 ? "여" : "남");
            await students.CreateAsync(student);
            await enrollments.CreateAsync(TestData.NewEnrollment(
                student.StudentID, name, TestData.Year, grade: 3, classNum: 1, number: i));

            ids.Add(student.StudentID);
        }

        return ids;
    }

    /// <summary>한 학생에게 기록을 많이, 그중 하나는 아주 길게.</summary>
    private static async Task SeedLongRecordsAsync(string school, string studentId)
    {
        using var logs = new StudentLogRepository(school);
        using var specials = new StudentSpecialRepository(school);

        var start = new DateTime(TestData.Year, 3, 2);
        for (int i = 0; i < LogCount; i++)
        {
            string text = i == 0
                ? string.Concat(System.Linq.Enumerable.Repeat(
                    "아주 긴 기록이다. 줄바꿈 없이 이어지는 문장이 목록 미리보기와 인쇄에서 어떻게 잘리는지 본다. ", 60))
                : $"{i + 1}번째 기록 — 수업 태도와 활동 내용을 적는다.";

            await logs.CreateAsync(TestData.NewStudentLog(
                studentId, semester: 2, log: text, date: start.AddDays(i % 180)));
        }

        // 학생부는 바이트 한도가 있는 칸이다 — 한도 언저리까지 채워 둔다.
        await specials.CreateAsync(TestData.NewSpecial(
            studentId,
            title: "한도 언저리 특기사항",
            content: string.Concat(System.Linq.Enumerable.Repeat("성실하게 참여하였음. ", 70))));
    }

    private static async Task SeedTimetableAndCourseAsync(string school)
    {
        using var timetable = new ClassTimetableRepository(school);
        using var courses = new CourseRepository(school);

        string[] subjects = { "국어", "수학", "영어", "과학", "사회", "체육", "음악" };
        for (int day = 1; day <= 5; day++)
            for (int period = 1; period <= 7; period++)
                await timetable.CreateAsync(TestData.NewTimetableSlot(
                    grade: 3, classNum: 1, dayOfWeek: day, period: period,
                    subject: subjects[(day + period) % subjects.Length], semester: 2));

        await courses.CreateAsync(TestData.NewCourse(grade: 3, semester: 2, rooms: "3-1"));
    }

    /// <summary>게시판 500건. 목록이 정말 가상화되는지 보려면 이 정도는 있어야 한다.</summary>
    private static async Task SeedPostsAsync(string board)
    {
        string[] categories = { "수업", "학급", "업무", "개인" };

        using var posts = new NewSchool.Board.Repositories.PostRepository(board);
        for (int i = 1; i <= PostCount; i++)
        {
            var post = TestData.NewPost(
                category: categories[i % categories.Length],
                subject: "메모",
                title: i % 50 == 0
                    ? $"{i}번 글 — 제목이 아주 길어서 목록 한 줄을 넘어가는 경우를 함께 본다 " + new string('길', 40)
                    : $"{i}번 글");
            post.DateTime = DateTime.Now.AddDays(-i);
            post.PlainText = i % 50 == 0
                ? string.Concat(System.Linq.Enumerable.Repeat("본문이 아주 긴 글이다. ", 200))
                : $"{i}번 글 본문";
            await posts.CreateAsync(post);
        }
    }

    /// <summary>
    /// 학생부 특기사항 — 40명 × 영역 3종. 한 명(20번, 이름이 긴 학생)은 <b>바이트 한도
    /// 언저리까지</b> 채운다. 인쇄물이 RowSpan 표라 쪽이 넘어갈 때가 관심사다.
    /// </summary>
    private static async Task SeedSpecsAsync(string school, List<string> studentIds)
    {
        string[] types = { "자율활동", "동아리활동", "진로활동" };

        using var specials = new StudentSpecialRepository(school);
        for (int i = 0; i < studentIds.Count; i++)
        {
            foreach (var type in types)
            {
                string content = i == 19   // 20번
                    ? string.Concat(System.Linq.Enumerable.Repeat(
                        "맡은 일을 끝까지 해내고 친구를 살뜰히 돕는 모습이 자주 관찰됨. ", 25))
                    : $"{type}에 성실히 참여하고 자기 몫을 다함.";

                await specials.CreateAsync(TestData.NewSpecial(
                    studentIds[i], type: type, title: $"{type} 기록", content: content));
            }
        }
    }

    /// <summary>학사일정 200일치 — 달력·오늘 화면이 하루에 여럿을 어떻게 보여 주는지 본다.</summary>
    private static async Task SeedSchoolSchedulesAsync(string school)
    {
        string[] names = { "학력평가", "체육대회", "현장학습", "학부모 상담주간", "방과후 발표회" };

        using var schedules = new SchoolScheduleRepository(school);
        var day = new DateTime(TestData.Year, 3, 2);
        for (int i = 0; i < 200; i++)
        {
            // 열흘에 한 번은 같은 날에 셋을 겹쳐 둔다.
            int sameDay = i % 10 == 0 ? 3 : 1;
            for (int k = 0; k < sameDay; k++)
            {
                await schedules.CreateAsync(TestData.NewSchedule(
                    day.AddDays(i), eventName: $"{names[(i + k) % names.Length]} {i + 1}"));
            }
        }
    }

    /// <summary>학급일지 120일치 — 목록 창의 기간 조회가 관심사다.</summary>
    private static async Task SeedClassDiariesAsync(string school)
    {
        using var diaries = new ClassDiaryRepository(school);
        var day = new DateTime(TestData.Year, 3, 2);
        for (int i = 0; i < 120; i++)
        {
            var date = day.AddDays(i);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

            await diaries.CreateAsync(new ClassDiary(
                TestData.SchoolCode, TestData.Year, 2, 3, 1, date, TestData.TeacherId)
            {
                Absent = i % 7 == 0 ? "김서준, 이하윤" : string.Empty,
                Memo = i % 11 == 0
                    ? string.Concat(System.Linq.Enumerable.Repeat("오늘 있었던 일을 길게 적어 둔다. ", 40))
                    : $"{date:M월 d일} 특이사항 없음",
                Notice = i % 5 == 0 ? "준비물: 실내화" : string.Empty,
            });
        }
    }

    /// <summary>개인 일정 200건 — 달력 한 칸에 여럿이 겹칠 때를 본다.</summary>
    private static async Task SeedCalendarEventsAsync(string scheduler)
    {
        using var events = new NewSchool.Scheduler.Repositories.KEventRepository(scheduler);
        var day = new DateTime(TestData.Year, 9, 1);

        for (int i = 0; i < 200; i++)
        {
            var start = day.AddDays(i / 4).AddHours(9 + (i % 4) * 2);
            await events.CreateAsync(new NewSchool.Scheduler.KEvent
            {
                CalendarId = 1,
                Title = i % 20 == 0
                    ? "제목이 아주 긴 일정이다 — 달력 한 칸에 들어가지 않을 때 어떻게 되는지 본다"
                    : $"{i + 1}번 일정",
                Start = start,
                End = start.AddHours(1),
                IsAllday = i % 8 == 0,
                User = TestData.TeacherId,
            });
        }
    }
}
