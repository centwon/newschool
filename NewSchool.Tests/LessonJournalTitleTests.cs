using System;
using NewSchool.Dialogs;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 수업 일지 제목 규칙 — 만드는 쪽(머리 정보 다이얼로그)과 되읽는 쪽(오늘의 수업 완료 표시)이
/// 같은 규칙을 봐야 한다. 게시글에는 날짜·교시를 담을 칸이 없어서 제목이 유일한 단서이므로,
/// 왕복이 깨지면 "쓴 일지가 있는데도 예정으로 보이는" 증상이 조용히 생긴다.
/// </summary>
public class LessonJournalTitleTests
{
    private static readonly DateTime Aug21 = new(2026, 8, 21);

    [Fact]
    public void 제목은_날짜_교시_과목_강의실_순서로_붙는다()
    {
        Assert.Equal("8/21 3교시 영어 1-1",
            LessonJournalTitle.Build(Aug21, 3, "영어", "1-1"));
    }

    [Theory]
    [InlineData(null, 3, "영어", "1-1", "3교시 영어 1-1")]
    [InlineData("2026-08-21", 0, "영어", "1-1", "8/21 영어 1-1")]
    [InlineData("2026-08-21", 3, "", "1-1", "8/21 3교시 1-1")]
    [InlineData("2026-08-21", 3, "영어", "  ", "8/21 3교시 영어")]
    public void 빈_조각은_건너뛴다(string? date, int period, string subject, string room, string expected)
    {
        var d = date == null ? (DateTime?)null : DateTime.Parse(date);
        Assert.Equal(expected, LessonJournalTitle.Build(d, period, subject, room));
    }

    [Fact]
    public void 만든_제목에서_교시를_되읽는다()
    {
        var title = LessonJournalTitle.Build(Aug21, 3, "영어", "1-1");
        Assert.Equal(3, LessonJournalTitle.PeriodOf(title, Aug21));
    }

    [Fact]
    public void 두_자리_교시도_되읽는다()
    {
        var title = LessonJournalTitle.Build(Aug21, 10, "영어", "1-1");
        Assert.Equal(10, LessonJournalTitle.PeriodOf(title, Aug21));
    }

    /// <summary>"8/2" 로 검색하면 "8/21" 이 함께 걸린다 — 날짜가 다르면 인정하지 않는다.</summary>
    [Fact]
    public void 날짜가_다르면_0()
    {
        var title = LessonJournalTitle.Build(Aug21, 3, "영어", "1-1");
        Assert.Equal(0, LessonJournalTitle.PeriodOf(title, new DateTime(2026, 8, 2)));
    }

    /// <summary>
    /// 일지를 고칠 때 제목에서 머리 정보를 되살린다. 과목명 뒤에 남는 꼬리가
    /// 강의실이 되므로, 꼬리를 있는 그대로 돌려줘야 한다.
    /// </summary>
    [Fact]
    public void 머리를_읽고_나머지를_꼬리로_돌려준다()
    {
        var head = LessonJournalTitle.Head("8/21 3교시 생활과 윤리 과학실");

        Assert.NotNull(head);
        Assert.Equal((8, 21, 3), (head!.Value.Month, head.Value.Day, head.Value.Period));
        Assert.Equal("생활과 윤리 과학실", head.Value.Tail);
    }

    [Fact]
    public void 강의실이_없으면_꼬리는_과목뿐()
    {
        var head = LessonJournalTitle.Head("8/21 3교시 영어");

        Assert.NotNull(head);
        Assert.Equal("영어", head!.Value.Tail);
    }

    /// <summary>
    /// 강의실은 목록에서 고르지 않고 직접 적을 수도 있어 공백이 든다("과학실 2").
    /// 제목에 그대로 실리고, 되읽을 때도 꼬리에서 통째로 살아나야 한다 —
    /// 창은 과목명 길이만큼만 잘라내고 나머지를 강의실로 되돌린다.
    /// </summary>
    [Fact]
    public void 공백이_든_강의실도_왕복한다()
    {
        const string subject = "생활과 윤리";

        var title = LessonJournalTitle.Build(Aug21, 3, subject, "과학실 2");
        Assert.Equal("8/21 3교시 생활과 윤리 과학실 2", title);

        var head = LessonJournalTitle.Head(title);
        Assert.NotNull(head);
        Assert.Equal("과학실 2", head!.Value.Tail[subject.Length..].Trim());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("오늘 수업 정리")]                 // 사용자가 제목을 새로 썼다
    [InlineData("8/21 영어 1-1")]                  // 교시 없이 저장했다
    [InlineData("보강 8/21 3교시 영어")]           // 앞에 다른 말이 붙었다
    public void 규칙을_벗어난_제목은_0(string? title)
    {
        Assert.Equal(0, LessonJournalTitle.PeriodOf(title, Aug21));
    }
}
