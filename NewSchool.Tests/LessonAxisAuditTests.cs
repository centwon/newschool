using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using NewSchool.ViewModels;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 수업·시간표·교과 축 전수 감사(2026-08-31)에서 드러난 결함들의 회귀 테스트.
///
/// <para>여기 걸린 것 중 셋은 <b>화면이 서로 다른 답을 내던</b> 종류다 — 어느 한 곳만
/// 규칙을 안 따라도 사용자에게는 "저장은 됐는데 안 보인다"로 나타나므로, 그 규칙을
/// 코드에 적어 두는 것만으로는 부족하고 <b>어긋나는 순간 빌드가 아니라 테스트가 걸리게</b>
/// 만들어 둔다.</para>
/// </summary>
public class LessonAxisAuditTests
{
    #region 소스 규칙 (파일을 읽어 검사)

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// 주석을 걷어낸 소스. 규칙을 설명하는 주석 안에 그 규칙이 금지하는 이름이 등장하므로
    /// (예: "Settings.WorkYear 를 섞어 쓰지 말 것"), 주석을 지우지 않으면 제 문서에 걸린다.
    /// </summary>
    private static string CodeOnly(string root, string relativePath)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"파일을 찾지 못했습니다: {relativePath}");

        var lines = File.ReadAllLines(path)
            .Where(line =>
            {
                var t = line.TrimStart();
                return !t.StartsWith("//") && !t.StartsWith("<!--") && !t.StartsWith("*");
            });

        return string.Join("\n", lines);
    }

    /// <summary>
    /// <b>학급일지는 화면의 학년도·학기 피커를 따른다.</b>
    ///
    /// <para>예전에는 같은 화면에서 명렬·누가기록만 피커를 따르고 일지와 그 시간표는
    /// <c>Settings.WorkYear</c>·<c>Settings.WorkSemester</c> 를 봤다. 피커로 지난 학년도를
    /// 펼쳐 놓고 쓴 일지가 <b>올해 행에 저장됐고</b>, 그 해의 일지는 열 방법도 없었다.</para>
    /// </summary>
    [Theory]
    [InlineData("Pages/ClassDiaryPage.xaml.cs")]
    [InlineData("Controls/ClassDiaryBox.xaml.cs")]
    [InlineData("ViewModels/ClassDiaryViewModel.cs")]
    public void 학급일지_축은_작업_학년도_설정을_직접_읽지_않는다(string relativePath)
    {
        string code = CodeOnly(FindRepoRoot(), relativePath);

        Assert.DoesNotContain("Settings.WorkYear", code);
        Assert.DoesNotContain("Settings.WorkSemester", code);
    }

    /// <summary>
    /// <b>하루 교시 상한은 <see cref="PeriodCounts.MaxSupported"/> 하나뿐이다.</b>
    ///
    /// <para>예전에는 상한이 두 벌이었다 — 설정 화면은 12 까지 올려 주는데 표시 쪽은 7 로
    /// 박혀 있어서, 8교시에 배치한 수업이 배치판·주별 표·시수에는 나오는데 내 시간표·오늘
    /// 화면·학급일지에서만 <b>조용히 사라졌다.</b> 같은 8교시라도 보강(LessonChange)은
    /// 보였기 때문에 특히 짚기 어려웠다.</para>
    /// </summary>
    [Theory]
    [InlineData("ViewModels/TimetableViewModel.cs", @"period\s*<=\s*7")]
    [InlineData("Controls/TimetableControl.xaml.cs", @"period\s*<=\s*7")]
    [InlineData("Controls/TimetableControl.xaml.cs", @"Range\(\s*1\s*,\s*7\s*\)")]
    [InlineData("Services/TimetableService.cs", @"Period\s*>\s*7")]
    [InlineData("Dialogs/ClassTimetableEditDialog.xaml", @"ComboBoxItem\s+Content=""7""")]
    [InlineData("Pages/SettingsPage.xaml", @"Maximum=""12""")]
    public void 교시_상한을_파일마다_따로_적지_않는다(string relativePath, string forbidden)
    {
        string code = CodeOnly(FindRepoRoot(), relativePath);

        Assert.False(
            Regex.IsMatch(code, forbidden),
            $"{relativePath} 에 교시 상한이 직접 적혀 있습니다(`{forbidden}`). " +
            "PeriodCounts.MaxSupported 를 쓰세요 — 상한이 두 벌이 되면 그 위 교시 수업이 화면에서 사라집니다.");
    }

    /// <summary>
    /// 시간표 격자의 교시 행은 코드가 만든다 — XAML 에 손으로 적어 두면
    /// 상한을 올릴 때 그 파일을 잊는다(헤더 행 하나만 XAML 에 있다).
    /// </summary>
    [Fact]
    public void 시간표_격자의_교시_행은_XAML_에_박혀_있지_않다()
    {
        string xaml = CodeOnly(FindRepoRoot(), "Controls/TimetableControl.xaml");

        int rowDefinitions = Regex.Matches(xaml, "<RowDefinition").Count;

        Assert.True(rowDefinitions == 1,
            $"TimetableControl.xaml 의 RowDefinition 이 {rowDefinitions}개입니다. " +
            "헤더 한 줄만 두고 교시 행은 BuildPeriodRows() 가 만들어야 합니다.");
    }

    /// <summary>
    /// <b>수업 홈의 [오늘의 수업]도 그날 변경을 얹는다.</b>
    ///
    /// <para>예전에는 이 카드만 평소 시간표를 그대로 세워서, 휴강한 수업이 '예정' 으로 남고
    /// "N시간 중 M건" 의 N 까지 부풀었으며 보강은 아예 나오지 않았다. 바로 옆 [내 시간표]
    /// 카드는 얹고 있었으므로 <b>한 화면이 같은 질문에 두 답</b>을 내놓았다.</para>
    /// </summary>
    [Fact]
    public void 오늘의_수업은_그날_변경을_얹는다()
    {
        string code = CodeOnly(FindRepoRoot(), "Pages/LessonHomePage.xaml.cs");

        Assert.Contains("ApplyDayChangesAsync", code);
    }

    /// <summary>
    /// 강의실 초기화는 네 표를 <b>한 트랜잭션</b>으로 지운다.
    ///
    /// <para>예전에는 리포지토리마다 제 연결을 열어 따로 지웠다 — 세 번째에서 실패하면
    /// 진도·시수는 이미 사라졌는데 강의실은 그대로인, 부르는 쪽이 "반쯤 지워진 상태가
    /// 제일 나쁘다" 며 막았다고 적어 둔 바로 그 상태가 됐다.</para>
    /// </summary>
    [Fact]
    public void 강의실_초기화는_한_트랜잭션이다()
    {
        string code = CodeOnly(FindRepoRoot(), "Services/CourseRoomReset.cs");

        Assert.Contains("BeginTransaction", code);
        Assert.Contains("Commit", code);
        Assert.Contains("Rollback", code);
    }

    #endregion

    #region 교시 상한 (순수 함수)

    [Fact]
    public void 상한까지의_교시_설정은_그대로_받는다()
    {
        int max = PeriodCounts.MaxSupported;
        var parsed = PeriodCounts.Parse($"{max},{max},{max},{max},{max}");

        Assert.Equal(new PeriodCounts(max, max, max, max, max), parsed);
    }

    [Fact]
    public void 상한을_넘는_교시_설정은_기본값으로_떨어진다()
    {
        int over = PeriodCounts.MaxSupported + 1;

        Assert.Equal(PeriodCounts.Default, PeriodCounts.Parse($"6,7,6,7,{over}"));
    }

    /// <summary>
    /// 빈 격자는 상한만큼 만들어져야 한다 — 모자라면 그 교시 수업이
    /// <see cref="TimetableViewModel.GetItem"/> 에서 null 로 떨어져 화면에서 사라진다.
    /// </summary>
    [Fact]
    public void 빈_시간표는_상한만큼_칸을_만든다()
    {
        var vm = new TimetableViewModel();
        vm.InitializeEmptyTimetable();

        Assert.Equal(5 * PeriodCounts.MaxSupported, vm.Items.Count);

        // 마지막 교시가 실제로 찾아져야 한다 (여기서 null 이면 그 수업이 사라진다)
        Assert.NotNull(vm.GetItem(1, PeriodCounts.MaxSupported));
        Assert.NotNull(vm.GetItem(5, PeriodCounts.MaxSupported));
    }

    #endregion
}

/// <summary>
/// 주차별 시수 조정은 <b>주 시작일</b>로 갈린다.
///
/// <para>예전 키는 (수업, 학급, 주차번호)였다. 주차 번호는 학기 구간에서 다시 세어지는
/// 값이라, 2학기를 관례값(9/1 시작)으로 보다가 학사일정을 내려받으면 시작이 여름방학
/// 다음 첫 수업일로 당겨지면서 번호가 통째로 밀린다 — 9월 셋째 주에 손으로 고친 시수가
/// 8월 마지막 주 칸에 가서 붙었다.</para>
/// </summary>
public class CourseWeeklyHoursKeyTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public CourseWeeklyHoursKeyTests(SqliteTestFixture db) => _db = db;

    private async Task<int> NewCourseAsync()
    {
        using var repo = new CourseRepository(_db.DbPath);
        return await repo.CreateAsync(TestData.NewCourse(year: TestData.Year, rooms: "1-1"));
    }

    private static CourseWeeklyHours Row(int courseNo, DateTime weekStart, int week, int hours) => new()
    {
        CourseNo = courseNo,
        Room = "1-1",
        Week = week,
        WeekStart = weekStart,
        PlannedHours = hours,
    };

    [Fact]
    public async Task 주차_번호가_밀려도_같은_주면_한_줄로_덮어쓴다()
    {
        int courseNo = await NewCourseAsync();
        var monday = new DateTime(TestData.Year, 9, 15);

        using var repo = new CourseWeeklyHoursRepository(_db.DbPath);

        // 관례값 기준으로 3주차라고 적어 두었다가…
        await repo.UpsertAsync(Row(courseNo, monday, week: 3, hours: 2));
        // …학사일정을 받아 같은 주가 5주차로 다시 세어졌다.
        await repo.UpsertAsync(Row(courseNo, monday, week: 5, hours: 4));

        var map = await repo.GetByCourseAsync(courseNo);

        Assert.Single(map);
        Assert.Equal(4, map[("1-1", monday)].PlannedHours);
        Assert.Equal(5, map[("1-1", monday)].Week);
    }

    [Fact]
    public async Task 주가_다르면_따로_남는다()
    {
        int courseNo = await NewCourseAsync();
        var first = new DateTime(TestData.Year, 9, 15);
        var second = new DateTime(TestData.Year, 9, 22);

        using var repo = new CourseWeeklyHoursRepository(_db.DbPath);
        await repo.UpsertAsync(Row(courseNo, first, week: 3, hours: 2));
        await repo.UpsertAsync(Row(courseNo, second, week: 3, hours: 5));   // 번호가 같아도 다른 주다

        var map = await repo.GetByCourseAsync(courseNo);

        Assert.Equal(2, map.Count);
        Assert.Equal(2, map[("1-1", first)].PlannedHours);
        Assert.Equal(5, map[("1-1", second)].PlannedHours);
    }

    [Fact]
    public async Task 되돌리기는_주_시작일로_지운다()
    {
        int courseNo = await NewCourseAsync();
        var monday = new DateTime(TestData.Year, 10, 6);

        using var repo = new CourseWeeklyHoursRepository(_db.DbPath);
        await repo.UpsertAsync(Row(courseNo, monday, week: 7, hours: 3));

        Assert.True(await repo.DeleteAsync(courseNo, "1-1", monday));
        Assert.Empty(await repo.GetByCourseAsync(courseNo));
    }

    /// <summary>
    /// 옛 키로 쌓인 DB 에는 같은 주 시작일이 두 줄일 수 있다(번호가 밀린 뒤 다시 고친 경우).
    /// 마이그레이션 3 이 <b>나중에 적은 것</b>만 남기고 새 UNIQUE 인덱스를 세운다.
    /// </summary>
    [Fact]
    public async Task 옛_키로_겹친_줄은_마이그레이션이_정리한다()
    {
        int courseNo = await NewCourseAsync();
        var monday = new DateTime(TestData.Year, 11, 3);

        // 새 인덱스를 잠시 걷어내고 옛 모양(같은 주 시작일 두 줄)을 만든다.
        using (var conn = new SqliteConnection($"Data Source={_db.DbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                DROP INDEX IF EXISTS idx_courseweeklyhours_by_weekstart;
                INSERT INTO CourseWeeklyHours (CourseNo, Room, Week, WeekStartDate, PlannedHours)
                VALUES (@c, '1-1', 3, @d, 2), (@c, '1-1', 5, @d, 4);
                PRAGMA user_version = 2;
                """;
            cmd.Parameters.AddWithValue("@c", courseNo);
            cmd.Parameters.AddWithValue("@d", monday.ToString("yyyy-MM-dd"));
            await cmd.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();

        using (var initializer = new NewSchool.Database.DatabaseInitializer(_db.DbPath))
        {
            Assert.True(await initializer.InitializeAsync());
        }

        using var repo = new CourseWeeklyHoursRepository(_db.DbPath);
        var map = await repo.GetByCourseAsync(courseNo);

        var kept = Assert.Single(map);
        Assert.Equal(monday, kept.Key.WeekStart);
        Assert.Equal(4, kept.Value.PlannedHours);   // 나중에 적은 값
    }
}
