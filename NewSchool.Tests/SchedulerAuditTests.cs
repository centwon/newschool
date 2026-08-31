using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NewSchool.Scheduler;
using NewSchool.Scheduler.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 스케줄러 전수 감사에서 고친 것들의 회귀 테스트 (2026-08-31).
/// </summary>
public class SchedulerAuditTests : IClassFixture<SchedulerTestFixture>
{
    private readonly SchedulerTestFixture _db;

    public SchedulerAuditTests(SchedulerTestFixture db) => _db = db;

    private static KEvent NewTask(string title, DateTime start, int calendarId = 1) => new()
    {
        ItemType = "task",
        CalendarId = calendarId,
        Title = title,
        Start = start,
        End = start,
        IsAllday = false,
        Status = "confirmed",
        User = "테스트교사",
        Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
    };

    // ── A2/A3: 0행 갱신을 성공으로 보지 않는다 ─────────────────────────

    /// <summary>
    /// 이미 지워진 항목을 고치면 <b>false</b> 가 나와야 한다 — 예외가 아니다.
    /// 화면들이 이 값을 보지 않으면 "화면은 완료, DB 는 미완료" 로 갈라진다.
    /// </summary>
    [Fact]
    public async Task 없는_할일을_고치면_false_다()
    {
        // ⚠ Scheduler.CreateService() 는 실제 사용자 DB 를 가리킨다 — 테스트는 픽스처 경로만 쓴다.
        using var svc = new SchedulerService(_db.DbPath);

        var ghost = NewTask("이미 지워진 할 일", DateTime.Today.AddHours(9));
        ghost.No = 999999;

        Assert.False(await svc.UpdateTaskAsync(ghost));
        Assert.False(await svc.UpdateEventAsync(ghost));
    }

    [Fact]
    public async Task 있는_할일을_고치면_true_이고_값이_남는다()
    {
        using var svc = new SchedulerService(_db.DbPath);

        var task = NewTask("완료 토글 대상", DateTime.Today.AddHours(9));
        task.No = await svc.CreateTaskAsync(task);
        Assert.True(task.No > 0);

        task.IsDone = true;
        Assert.True(await svc.UpdateTaskAsync(task));

        var reloaded = (await svc.GetAllTasksAsync()).First(t => t.No == task.No);
        Assert.True(reloaded.IsDone);
    }

    /// <summary>
    /// 화면 셋이 모두 갱신 결과를 확인해야 한다. 컴파일러가 못 잡는 자리라 소스로 고정한다 —
    /// 실제로 달력 셀만 확인을 빠뜨린 채 "고쳤다" 는 주석이 달려 있었다.
    /// </summary>
    [Theory]
    [InlineData("Scheduler/DayCell.xaml.cs", "UpdateTaskAsync")]
    [InlineData("Scheduler/KAgendaControl.xaml.cs", "UpdateTaskAsync")]
    [InlineData("Scheduler/UnifiedItemDialog.xaml.cs", "UpdateTaskAsync")]
    [InlineData("Scheduler/UnifiedItemDialog.xaml.cs", "UpdateEventAsync")]
    public void 갱신_결과를_보지_않고_넘어가는_곳이_없다(string relativePath, string method)
    {
        var source = ReadSource(relativePath);

        // "await ...Method(...);" 로 끝나면 결과를 버린 것이다.
        // 결과를 쓰는 형태는 if(!await ...) / = await ... / return await ... 처럼 앞에 무언가 온다.
        var discarded = Regex.Matches(source, @"^\s*await\s+[\w\.]*" + Regex.Escape(method) + @"\([^;]*\);",
            RegexOptions.Multiline);

        Assert.True(discarded.Count == 0,
            $"{relativePath} 에서 {method} 의 결과를 버린다 — 0행 갱신(이미 지워진 항목)이 " +
            $"저장 성공으로 보인다: {string.Join(" | ", discarded.Select(m => m.Value.Trim()))}");
    }

    // ── B1: 트랜잭션을 겹쳐 열면 던진다 ────────────────────────────────

    [Fact]
    public void 트랜잭션을_겹쳐_열면_조용히_삼키지_않고_던진다()
    {
        using var uow = new UnitOfWork(_db.DbPath);
        uow.BeginTransaction();

        var ex = Assert.Throws<InvalidOperationException>(() => uow.BeginTransaction());
        Assert.Contains("조용히 롤백", ex.Message);

        uow.Rollback();
    }

    [Fact]
    public async Task 반복_할일은_한_트랜잭션으로_들어간다()
    {
        using var uow = new UnitOfWork(_db.DbPath);
        var seriesId = Guid.NewGuid().ToString("N");

        await uow.ExecuteInTransactionAsync(async () =>
        {
            for (int i = 0; i < 5; i++)
            {
                var t = NewTask($"반복 {i}", DateTime.Today.AddDays(i).AddHours(9));
                t.SeriesId = seriesId;
                await uow.KEvents.CreateAsync(t);
            }
        });

        using var repo = new KEventRepository(_db.DbPath);
        var members = await repo.GetBySeriesIdFromAsync(seriesId, DateTime.Today);
        Assert.Equal(5, members.Count);
    }

    // ── A1: 기본 캘린더는 일정만 지워지면 안 된다 ──────────────────────

    /// <summary>
    /// 기본 캘린더를 지우려 하면 <b>아무 것도</b> 지워지지 않아야 한다.
    /// 예전에는 소속 일정을 먼저 지우고 커밋한 뒤 false 를 돌려줘서,
    /// 부르는 쪽은 "삭제 실패" 로 보는데 일정은 이미 사라져 있었다.
    /// </summary>
    [Fact]
    public async Task 기본_캘린더는_지워지지_않고_소속_일정도_남는다()
    {
        using var repo = new KCalendarListRepository(_db.DbPath);
        var calendars = await repo.GetAllAsync();
        var defaultCal = calendars.First(c => c.IsDefault);

        using var svc = new SchedulerService(_db.DbPath);
        var task = NewTask("기본 캘린더의 할 일", DateTime.Today.AddHours(10), defaultCal.No);
        task.No = await svc.CreateTaskAsync(task);

        Assert.False(await repo.DeleteAsync(defaultCal.No));

        // 캘린더도 일정도 그대로여야 한다
        Assert.Contains(await repo.GetAllAsync(), c => c.No == defaultCal.No);
        Assert.Contains(await svc.GetAllTasksAsync(), t => t.No == task.No);
    }

    [Fact]
    public async Task 기본이_아닌_캘린더는_소속_일정과_함께_지워진다()
    {
        using var repo = new KCalendarListRepository(_db.DbPath);
        int calId = await repo.GetOrCreateAsync($"지울달력_{Guid.NewGuid():N}", "#123456");

        using var svc = new SchedulerService(_db.DbPath);
        var task = NewTask("함께 지워질 할 일", DateTime.Today.AddHours(11), calId);
        task.No = await svc.CreateTaskAsync(task);

        Assert.True(await repo.DeleteAsync(calId));

        Assert.DoesNotContain(await repo.GetAllAsync(), c => c.No == calId);
        Assert.DoesNotContain(await svc.GetAllTasksAsync(), t => t.No == task.No);
    }

    [Fact]
    public async Task 없는_캘린더를_지우면_false_다()
    {
        using var repo = new KCalendarListRepository(_db.DbPath);
        Assert.False(await repo.DeleteAsync(999999));
    }

    // ── C2 / D2: 작성자 한 벌, 다크 테마 지뢰 제거 ─────────────────────

    /// <summary>
    /// 작성자는 앱 전체가 <c>Settings.AuthorName</c> 한 벌을 쓴다.
    /// 스케줄러는 Windows 계정 이름(Environment.UserName)을, 구글 동기화는 교사 ID 를 써서
    /// 한 앱 안에서 작성자가 세 갈래였다.
    /// </summary>
    [Theory]
    [InlineData("Scheduler/UnifiedItemDialog.xaml.cs")]
    [InlineData("Google/GoogleSyncService.cs")]
    public void 작성자에_Windows_계정_이름을_쓰지_않는다(string relativePath)
    {
        var source = StripComments(ReadSource(relativePath));

        Assert.DoesNotContain("Environment.UserName", source);
        Assert.Contains("Settings.AuthorName", source);
    }

    /// <summary>
    /// 평일 날짜에 검정을 박던 컨버터가 되살아나면 안 된다 — 다크 테마에서 어두운 배경 위
    /// 검정 글씨가 되어 평일 날짜가 통째로 보이지 않는다(DayCell.UpdateColorDisplay 주석 참고).
    /// </summary>
    [Fact]
    public void 평일에_검정을_박는_컨버터가_없다()
    {
        var source = StripComments(ReadSource("Scheduler/DayCell.xaml.cs"));
        Assert.DoesNotContain("class DayOfWeekToColorConverter", source);
    }

    // ── 통합 대화상자: 안 보이는 값이 저장되지 않게 ──────────────────────

    /// <summary>
    /// 대화상자를 열 때 <b>두 서식을 모두</b> 채워야 한다.
    ///
    /// <para>예전에는 지금 보이는 탭만 채웠다. 새 항목은 '할 일'로 열리므로 '일정' 서식은
    /// 한 번도 채워지지 않았고, 탭을 옮겨도 패널만 바뀔 뿐이라 날짜·시간 칸이 비어 있었다.
    /// 그런데 메모리의 _event 는 값을 들고 있어서, 그대로 저장하면 <b>화면에 보이지도 않던
    /// 날짜·시간이 등록됐다.</b></para>
    /// </summary>
    [Fact]
    public void 대화상자는_두_서식을_모두_채운다()
    {
        var source = StripComments(ReadSource("Scheduler/UnifiedItemDialog.xaml.cs"));
        var onLoaded = Regex.Match(source, @"OnLoaded[\s\S]*?_isInitialized = true;");

        Assert.True(onLoaded.Success, "OnLoaded 를 찾지 못했다.");
        Assert.Contains("FillTaskForm();", onLoaded.Value);
        Assert.Contains("FillEventForm();", onLoaded.Value);

        // 한쪽만 채우던 옛 형태(if (_isTaskMode) FillTaskForm(); else FillEventForm();)가 아니어야 한다
        Assert.DoesNotContain("else FillEventForm();", onLoaded.Value);
    }

    /// <summary>
    /// 기존 항목은 종류(할 일 ↔ 일정)를 바꿀 수 없어야 한다.
    ///
    /// <para>두 종류를 서로 다른 KEvent 객체로 들고 있어서, 탭을 옮기면 제목·날짜가 사라져
    /// 보이고 그대로 저장하면 <b>같은 제목의 항목이 하나 더</b> 생겼다(반대쪽 No 가 -1 이라
    /// 새로 만들어지고 원래 항목은 남았다). 잠그는 쪽을 택했다.</para>
    /// </summary>
    [Fact]
    public void 기존_항목은_종류를_바꿀_수_없다()
    {
        var source = StripComments(ReadSource("Scheduler/UnifiedItemDialog.xaml.cs"));

        Assert.Contains("RbTypeTask.IsEnabled  = _isNew;", source);
        Assert.Contains("RbTypeEvent.IsEnabled = _isNew;", source);
    }

    /// <summary>
    /// 새 항목에서 탭을 옮기면 적은 내용은 따라가야 한다(제목이 사라지지 않는다).
    /// 다만 신원(No·GoogleId)은 넘기지 않는다 — 넘기면 '변환' 이 되어 반복 시리즈·구글
    /// 동기화 쪽 가장자리가 늘어난다.
    /// </summary>
    [Fact]
    public void 새_항목의_탭_전환은_적은_내용을_넘기고_신원은_넘기지_않는다()
    {
        var source = StripComments(ReadSource("Scheduler/UnifiedItemDialog.xaml.cs"));
        var carry = Regex.Match(source, @"private void CarryOverToOtherType\([\s\S]*?\n    \}");

        Assert.True(carry.Success, "CarryOverToOtherType 을 찾지 못했다.");
        Assert.Contains("to.Title    = from.Title;", carry.Value);
        Assert.Contains("to.Notes", carry.Value);
        Assert.Contains("to.Start", carry.Value);

        Assert.DoesNotContain("to.No ", carry.Value);
        Assert.DoesNotContain("to.GoogleId", carry.Value);
    }

    /// <summary>
    /// 새 일정은 고른 날짜의 <b>지금 시각</b>에서 연다 — 어느 날을 눌러도 9시로 고정이던 것을 고쳤다.
    /// </summary>
    [Fact]
    public void 새_일정은_고정_시각을_쓰지_않는다()
    {
        var source = StripComments(ReadSource("Scheduler/UnifiedItemDialog.xaml.cs"));
        var newEvent = Regex.Match(source, @"private static KEvent NewEvent\([\s\S]*?\n    \}");

        Assert.True(newEvent.Success, "NewEvent 를 찾지 못했다.");
        Assert.Contains("DateTime.Now.Hour", newEvent.Value);
        Assert.DoesNotContain("AddHours(9)", newEvent.Value);
    }

    /// <summary>
    /// 주석을 걷어낸다. 위 검사들은 "이 코드가 되살아났는가" 를 보는 것이라,
    /// 지운 이유를 적어 둔 주석이 스스로 걸리면 안 된다.
    /// </summary>
    private static string StripComments(string source)
    {
        var withoutBlock = Regex.Replace(source, @"/\*[\s\S]*?\*/", string.Empty);
        return string.Join('\n', withoutBlock
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//")));
    }

    private static string ReadSource(string relativePath)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"소스를 찾지 못했다: {relativePath}");
        return File.ReadAllText(path);
    }
}
