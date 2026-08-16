using System.Collections.Generic;
using System.Linq;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.ViewModels;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 오늘 화면의 시간표 변경 병합 규칙.
///
/// 이 규칙이 화면 코드 안에 있으면 눈으로만 확인할 수 있어 순수 함수로 떼어 뒀다.
/// 특히 "휴강을 지우지 말 것" 은 눈에 잘 띄지 않는 결정이라 여기에 못박는다 —
/// 조용히 사라지면 원래 없던 교시인지 오늘만 없는 교시인지 구분할 수 없다.
/// </summary>
public class TimetableChangeMergerTests
{
    private static TimetableItemViewModel Slot(int period, string subject, string room = "1-1") =>
        new() { DayOfWeek = 1, Period = period, SubjectName = subject, Room = room, IsEmpty = false };

    private static LessonChange Cancel(int period, string memo = "") =>
        new() { Period = period, CourseNo = null, Memo = memo };

    /// <summary>내 수업을 넣는다 (교체 또는 보강)</summary>
    private static LessonChange Put(int period, int courseNo, string subject, string room = "1-2") =>
        new() { Period = period, CourseNo = courseNo, CourseSubject = subject, Room = room };

    /// <summary>남의 수업에 대신 들어간다 (대강) — 내 수업이 아니라 과목명만 적는다</summary>
    private static LessonChange Substitute(int period, string subject, string room = "2-1", string memo = "") =>
        new() { Period = period, CourseNo = null, SubjectText = subject, Room = room, Memo = memo };

    private static Dictionary<int, LessonChange> Changes(params LessonChange[] changes) =>
        changes.ToDictionary(c => c.Period);

    [Fact]
    public void 변경이_없으면_평소_시간표_그대로다()
    {
        var slots = new[] { Slot(3, "국어"), Slot(1, "수학") };

        var result = TimetableChangeMerger.Apply(slots, new Dictionary<int, LessonChange>(), 1);

        Assert.Equal([1, 3], result.Select(s => s.Period));
        Assert.All(result, s => Assert.Equal(LessonChangeKind.None, s.ChangeKind));
    }

    [Fact]
    public void 휴강은_칸을_지우지_않고_표시만_바꾼다()
    {
        var slots = new[] { Slot(2, "국어") };

        var result = TimetableChangeMerger.Apply(slots, Changes(Cancel(2, "출장")), 1);

        var slot = Assert.Single(result);
        Assert.Equal(LessonChangeKind.Cancelled, slot.ChangeKind);
        Assert.True(slot.IsCancelled);
        Assert.Equal("휴강", slot.ChangeLabel);
        Assert.Equal("국어", slot.SubjectName);          // 무엇이 빠졌는지 남아 있어야 한다
        Assert.Equal("(휴)국어", slot.SubjectWithPrefix);
        Assert.Equal("휴강 · 출장", slot.ChangeTooltip);
    }

    [Fact]
    public void 평소_수업이_없는_교시의_휴강은_무시한다()
    {
        // 빈 "휴강" 칸을 만들면 그 교시에 원래 수업이 있었던 것처럼 읽힌다
        var slots = new[] { Slot(2, "국어") };

        var result = TimetableChangeMerger.Apply(slots, Changes(Cancel(5)), 1);

        Assert.Single(result);
        Assert.Equal(2, result[0].Period);
    }

    [Fact]
    public void 평소_수업이_있는_교시에_수업을_넣으면_교체다()
    {
        var slots = new[] { Slot(2, "국어", "1-1") };

        var result = TimetableChangeMerger.Apply(slots, Changes(Put(2, 7, "수학", "1-3")), 1);

        var slot = Assert.Single(result);
        Assert.Equal(LessonChangeKind.Replaced, slot.ChangeKind);
        Assert.Equal("교체", slot.ChangeLabel);
        Assert.Equal("수학", slot.SubjectName);
        Assert.Equal("(교)수학", slot.SubjectWithPrefix);   // 칸에는 표식이 붙어 보인다
        Assert.Equal("1-3", slot.Room);
        Assert.Equal(7, slot.CourseNo);
        Assert.False(slot.IsCancelled);
    }

    [Fact]
    public void 평소_수업이_없는_교시에_수업을_넣으면_보강으로_생긴다()
    {
        var slots = new[] { Slot(2, "국어") };

        var result = TimetableChangeMerger.Apply(slots, Changes(Put(6, 7, "수학")), 1);

        Assert.Equal(2, result.Count);

        var added = result.Single(s => s.Period == 6);
        Assert.Equal(LessonChangeKind.Added, added.ChangeKind);
        Assert.Equal("보강", added.ChangeLabel);
        Assert.Equal("(보)수학", added.SubjectWithPrefix);
        Assert.Equal(1, added.DayOfWeek);
        Assert.False(added.IsEmpty);
    }

    [Fact]
    public void 보강이_끼어들어도_교시_순서로_정렬된다()
    {
        var slots = new[] { Slot(5, "국어"), Slot(1, "체육") };

        var result = TimetableChangeMerger.Apply(slots, Changes(Put(3, 9, "수학")), 1);

        Assert.Equal([1, 3, 5], result.Select(s => s.Period));
    }

    [Fact]
    public void 맞교환은_두_줄로_표현된다()
    {
        // 2교시 국어 ↔ 4교시 수학
        var slots = new[] { Slot(2, "국어"), Slot(4, "수학") };

        var result = TimetableChangeMerger.Apply(
            slots,
            Changes(Put(2, 20, "수학"), Put(4, 10, "국어")),
            1);

        Assert.Equal("수학", result.Single(s => s.Period == 2).SubjectName);
        Assert.Equal("국어", result.Single(s => s.Period == 4).SubjectName);
        Assert.All(result, s => Assert.Equal(LessonChangeKind.Replaced, s.ChangeKind));
    }

    [Fact]
    public void 대강은_평소_수업이_있어도_교체가_아니라_대강으로_표시된다()
    {
        // 남의 수업에 대신 들어가는 것(대강)이라 내 수업 목록에 없다 — 과목명만 적힌다
        var slots = new[] { Slot(2, "국어") };

        var result = TimetableChangeMerger.Apply(
            slots, Changes(Substitute(2, "과학", "2-1", "김OO 선생님 출장")), 1);

        var slot = Assert.Single(result);
        Assert.Equal(LessonChangeKind.Substitute, slot.ChangeKind);
        Assert.Equal("대강", slot.ChangeLabel);
        Assert.Equal("과학", slot.SubjectName);
        Assert.Equal("(대)과학", slot.SubjectWithPrefix);
        Assert.Equal("2-1", slot.Room);
        Assert.Equal(0, slot.CourseNo);          // 내 수업이 아니다
        Assert.Equal("대강 · 김OO 선생님 출장", slot.ChangeTooltip);
    }

    [Fact]
    public void 대강은_평소_수업이_없는_교시에도_들어간다()
    {
        var result = TimetableChangeMerger.Apply([], Changes(Substitute(6, "과학")), 3);

        var slot = Assert.Single(result);
        Assert.Equal(LessonChangeKind.Substitute, slot.ChangeKind);
        Assert.Equal(3, slot.DayOfWeek);
    }

    [Fact]
    public void 휴업일이라_평소_칸이_없어도_보강은_보인다()
    {
        var result = TimetableChangeMerger.Apply([], Changes(Put(3, 7, "수학")), 4);

        var slot = Assert.Single(result);
        Assert.Equal(LessonChangeKind.Added, slot.ChangeKind);
        Assert.Equal(4, slot.DayOfWeek);
    }
}
