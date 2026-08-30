using System;
using System.Diagnostics;
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
    /// 이미 써 둔 수업 일지를 같은 창으로 연다.
    /// </summary>
    /// <returns>고쳐서 저장했으면 true</returns>
    public static async Task<bool> OpenPostAsync(int postNo)
    {
        try
        {
            Post? post;
            // ⚠ 캐시 서비스로 바꾸지 말 것 — 돌려받은 Post 를 편집 창이 직접 고치는데,
            //    캐시는 같은 인스턴스를 나눠 주므로 취소해도 고친 값이 캐시에 남는다.
            //    (저장은 LessonJournalWindow 가 캐시 서비스로 한다.)
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
