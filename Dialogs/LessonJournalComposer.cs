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
/// 만드는 쪽(작성 창)과 되읽는 쪽(오늘의 수업 완료 표시, 편집 시 머리 정보 복원)이
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

    /// <summary>
    /// 제목에서 교시를 되읽는다. <paramref name="date"/> 의 날짜로 시작하는 제목만 인정하고,
    /// 그 밖에는 0 을 낸다(사용자가 제목을 고쳤거나 다른 날 글이다).
    /// </summary>
    public static int PeriodOf(string? title, DateTime date)
    {
        if (Head(title) is not { } head) return 0;

        return head.Month == date.Month && head.Day == date.Day ? head.Period : 0;
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
                // 해가 바뀌면 "8/21" 이 겹치므로 쓴 해까지 본다.
                if (post.DateTime.Year != date.Year) continue;

                int period = LessonJournalTitle.PeriodOf(post.Title, date);
                if (period > 0) byPeriod.TryAdd(period, post);
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

                // 제목에는 해가 없다 — 기간에 걸리는 해(2학기는 해를 넘긴다)로 맞춰 본다.
                foreach (var year in new[] { from.Year, to.Year }.Distinct())
                {
                    if (head.Day > DateTime.DaysInMonth(year, head.Month)) continue;

                    var date = new DateTime(year, head.Month, head.Day);
                    if (date >= from.Date && date <= to.Date)
                    {
                        result.Add((date, head.Period, post));
                        break;
                    }
                }
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
