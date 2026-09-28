using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using NewSchool.Board;
using NewSchool.Controls;

namespace NewSchool.Dialogs;

/// <summary>
/// 시간표 한 칸이 알려 주는 수업. 수업 일지의 시작값이 된다.
/// </summary>
/// <param name="Date">수업 날짜(보고 있는 날짜 · 그 주의 그 요일)</param>
/// <param name="Period">교시</param>
/// <param name="CourseNo">교과 번호. 0 이면 <paramref name="Subject"/> 로 맞춘다.</param>
/// <param name="Subject">과목명</param>
/// <param name="Room">강의실</param>
public sealed record LessonSlotSeed(
    DateTime Date,
    int Period,
    int CourseNo,
    string Subject,
    string Room);

/// <summary>
/// 수업 일지 제목 규칙.
///
/// <code>
///   8/21 3교시 영어 1-1
///   └날짜 └교시  └과목 └강의실
/// </code>
///
/// 만드는 쪽(작성 창)과 되읽는 쪽(시간표 칸의 공책 표시, 편집 시 머리 정보 복원)이
/// <b>같은 규칙</b>을 봐야 하므로 한자리에 둔다. 게시글에는 날짜·교시를 담을 칸이 없어서
/// (<c>Post</c> 는 Category·Subject·Title·Content 뿐) 제목이 유일한 단서다.
/// </summary>
public static class LessonJournalTitle
{
    /// <summary>"8/21 3교시 영어 1-1" — 빈 조각은 건너뛴다.</summary>
    /// <param name="date">null 이면 날짜를 붙이지 않는다</param>
    /// <param name="period">0 이하면 교시를 붙이지 않는다</param>
    public static string Build(DateTime? date, int period, string? subject, string? room)
    {
        var parts = new System.Collections.Generic.List<string>();

        if (date is { } d) parts.Add($"{d.Month}/{d.Day}");
        if (period > 0) parts.Add($"{period}교시");
        if (!string.IsNullOrWhiteSpace(subject)) parts.Add(subject.Trim());
        if (!string.IsNullOrWhiteSpace(room)) parts.Add(room.Trim());

        return string.Join(" ", parts);
    }

    /// <summary>
    /// 제목 머리에서 월·일·교시와 그 뒤에 남은 꼬리(과목 + 강의실)를 되읽는다.
    /// 규칙을 벗어난 제목이면 null — 사용자가 제목을 새로 쓴 것이다.
    /// </summary>
    public static (int Month, int Day, int Period, string Tail)? Head(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var m = TitleHead.Match(title);
        if (!m.Success) return null;

        return (
            int.Parse(m.Groups[1].Value),
            int.Parse(m.Groups[2].Value),
            int.Parse(m.Groups[3].Value),
            title[m.Length..].Trim());
    }

    // 제목에서 그 날의 교시만 되읽던 PeriodOf 는 해를 가리지 못해(DateOf 주석) 지웠다(2026-09-29) —
    // 그 날 찾기는 Head + DateOf 로 한다.

    /// <summary>
    /// 제목의 월·일을 실제 날짜로 — 제목에는 해가 없으므로 <b>글을 쓴 때에 가장 가까운 해</b>로 잡는다.
    ///
    /// <para>예전에는 쓴 해를 그대로 붙였다. 12/31 수업의 일지를 1/2 에 쓰면 이듬해 12/31 로 읽혀
    /// 그 칸에 공책 표시가 뜨지 않았고, [수업 일지 쓰기] 가 같은 수업의 일지를 한 벌 더 만들었다.</para>
    /// </summary>
    /// <returns>그런 날이 없으면(2/30 등) null</returns>
    public static DateTime? DateOf(int month, int day, DateTime writtenAt)
    {
        if (month is < 1 or > 12 || day < 1) return null;

        DateTime? best = null;
        for (int year = writtenAt.Year - 1; year <= writtenAt.Year + 1; year++)
        {
            if (day > DateTime.DaysInMonth(year, month)) continue;

            var candidate = new DateTime(year, month, day);
            if (best == null || Math.Abs((candidate - writtenAt.Date).Days) < Math.Abs((best.Value - writtenAt.Date).Days))
                best = candidate;
        }
        return best;
    }

    private static readonly Regex TitleHead =
        new(@"^\s*(\d{1,2})/(\d{1,2})\s+(\d{1,2})교시", RegexOptions.Compiled);
}

/// <summary>
/// 수업 일지 쓰기·열기 진입점.
///
/// 수업 일지는 게시판 글 하나다. 어디서 시작하든 흐름이 같아야 하므로
/// — 수업 홈의 내 시간표·오늘의 수업, 오늘 화면의 내 수업, 수업 일지 게시판의 새 글 버튼,
/// 최근 수업 일지 카드 — 전부 이 한 곳을 거쳐 <see cref="LessonJournalWindow"/> 를 연다.
/// </summary>
public static class LessonJournalComposer
{
    /// <summary>수업 일지가 사는 게시판</summary>
    public const string Category = "수업";

    /// <summary>수업 일지 게시판의 주제(글머리)</summary>
    public const string Subject = "수업일지";

    /// <summary>
    /// 새 수업 일지를 쓴다.
    /// </summary>
    /// <param name="seed">시간표 칸에서 넘어온 시작값. null 이면 오늘·첫 교과로 연다.</param>
    /// <returns>저장했으면 true</returns>
    public static async Task<bool> ComposeAsync(LessonSlotSeed? seed = null)
    {
        try
        {
            return await new LessonJournalWindow(seed).ShowDialogAsync(App.MainWindow);
        }
        catch (Exception ex)
        {
            await UserErrorReporter.ReportAsync("수업 일지 쓰기", ex);
            return false;
        }
    }

    /// <summary>
    /// 그 칸(날짜·교시)에 써 둔 일지가 있으면 그 글을, 없으면 새 일지를 연다.
    /// 시간표·진도표 칸에서 "수업 일지 쓰기" 를 누르면 같은 수업의 일지가 두 벌 생기지 않게 한다.
    /// </summary>
    /// <returns>저장했으면 true</returns>
    public static async Task<bool> OpenOrComposeAsync(LessonSlotSeed seed)
    {
        var existing = await FindByDateAsync(seed.Date);
        return existing.TryGetValue(seed.Period, out var post)
            ? await OpenPostAsync(post.No)
            : await ComposeAsync(seed);
    }

    /// <summary>
    /// 그 날짜에 써 둔 수업 일지를 교시별로 모은다.
    ///
    /// 게시글에는 날짜·교시를 담을 칸이 없어서 제목 규칙(<see cref="LessonJournalTitle"/>)을
    /// 되읽는다. 제목을 손으로 고친 글은 못 알아본다 — 글이 사라지는 것은 아니고 목록에는 남는다.
    /// 읽지 못하면 빈 결과다(부르는 쪽은 "아직 안 씀" 으로 보인다).
    /// </summary>
    public static async Task<Dictionary<int, Post>> FindByDateAsync(DateTime date)
    {
        var byPeriod = new Dictionary<int, Post>();

        try
        {
            // 제목이 "8/21 " 로 시작하는 글만 추린 뒤 교시를 되읽는다.
            using var service = NewSchool.Board.Board.CreateCachedService();
            var page = await service.GetPostsPagedAsync(
                pageNumber: 1,
                pageSize: 50,
                category: Category,
                subject: Subject,
                searchTitle: true,
                searchText: $"{date.Month}/{date.Day} ");

            foreach (var post in page.Items)
            {
                if (LessonJournalTitle.Head(post.Title) is not { } head || head.Period <= 0) continue;

                // 해가 바뀌면 "8/21" 이 겹치므로 쓴 때로 해를 가린다(DateOf 주석 — 연말 수업을 새해에 쓰는 경우).
                if (LessonJournalTitle.DateOf(head.Month, head.Day, post.DateTime) != date.Date) continue;

                byPeriod.TryAdd(head.Period, post);
            }
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Warning("LessonJournalComposer", $"그 날의 수업 일지를 읽지 못했다: {ex.Message}");
        }

        return byPeriod;
    }

    /// <summary>
    /// 한 학급의 수업 일지 가운데 날짜가 [<paramref name="from"/>, <paramref name="to"/>] 안인 것 — 날짜 순.
    /// 진도표의 "이 단원의 수업 일지" 가 쓴다. 제목 규칙의 꼬리("역사 3-1")로 학급을 가린다.
    /// </summary>
    public static async Task<List<(DateTime Date, int Period, Post Post)>> FindForRoomAsync(
        string subject, string room, DateTime from, DateTime to)
    {
        var result = new List<(DateTime, int, Post)>();
        var tail = LessonJournalTitle.Build(null, 0, subject, room);

        try
        {
            using var service = NewSchool.Board.Board.CreateCachedService();
            var page = await service.GetPostsPagedAsync(
                pageNumber: 1,
                pageSize: 1000,
                category: Category,
                subject: Subject,
                searchTitle: true,
                searchText: tail);

            foreach (var post in page.Items)
            {
                if (LessonJournalTitle.Head(post.Title) is not { } head) continue;
                if (!string.Equals(head.Tail, tail, StringComparison.Ordinal)) continue;

                // 제목에는 해가 없다 — 그 날 찾기(FindByDateAsync)와 같은 규칙으로 쓴 때에서 해를 잡는다.
                if (LessonJournalTitle.DateOf(head.Month, head.Day, post.DateTime) is { } date
                    && date >= from.Date && date <= to.Date)
                    result.Add((date, head.Period, post));
            }
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Warning("LessonJournalComposer", $"학급의 수업 일지를 읽지 못했다: {ex.Message}");
        }

        return result.OrderBy(r => r.Item1).ThenBy(r => r.Item2).ToList();
    }

    /// <summary>
    /// 이미 써 둔 수업 일지를 같은 창으로 연다.
    /// </summary>
    /// <returns>고쳐서 저장했으면 true</returns>
    public static async Task<bool> OpenPostAsync(int postNo)
    {
        try
        {
            Post? post;
            // 편집하려고 읽으므로 비캐시 — 묵은 값을 고쳐 저장하면 그 사이의 변경을 덮어쓴다.
            // (저장은 LessonJournalWindow 가 캐시 서비스로 한다.)
            using (var service = NewSchool.Board.Board.CreateService())
                post = await service.GetPostAsync(postNo, incrementReadCount: false);

            if (post == null)
            {
                await MessageBox.ShowErrorAsync("이 수업 일지를 찾을 수 없습니다. 이미 지워진 글일 수 있습니다.");
                return false;
            }

            return await new LessonJournalWindow(post).ShowDialogAsync(App.MainWindow);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonJournalComposer] 일지 열기 실패: {ex.Message}");
            await UserErrorReporter.ReportAsync("수업 일지 열기", ex);
            return false;
        }
    }
}
