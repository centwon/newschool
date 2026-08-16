using System.Collections.Generic;
using System.Linq;
using NewSchool.Models;
using NewSchool.ViewModels;

namespace NewSchool.Services;

/// <summary>
/// 그날만 걸리는 시간표 변경을 평소 시간표 위에 얹는다.
///
/// DB 를 모르는 순수 함수로 떼어 뒀다 — 이 규칙(휴강을 남길 것인가, 교체인가 보강인가)이
/// 화면 코드 안에 있으면 눈으로만 확인할 수 있다.
/// </summary>
public static class TimetableChangeMerger
{
    /// <summary>
    /// <paramref name="slots"/> 에 <paramref name="changes"/>(교시 → 변경)를 적용한 새 목록을 돌려준다.
    ///
    /// 휴강은 <b>칸을 지우지 않고 표시만 바꾼다</b> — 오늘 화면의 일은 "평소와 무엇이 다른가"를
    /// 알리는 것이라, 조용히 사라지면 원래 없던 교시인지 오늘만 없는 교시인지 구분할 수 없다.
    /// </summary>
    /// <param name="slots">평소(정기) 시간표의 오늘 칸들</param>
    /// <param name="changes">그 날짜의 변경 — 교시 → 변경</param>
    /// <param name="dayOfWeek">보강으로 새로 생기는 칸에 적을 요일 (월=1 … 금=5)</param>
    public static List<TimetableItemViewModel> Apply(
        IReadOnlyList<TimetableItemViewModel> slots,
        IReadOnlyDictionary<int, LessonChange> changes,
        int dayOfWeek)
    {
        if (changes.Count == 0)
            return slots.OrderBy(s => s.Period).ToList();

        var byPeriod = slots.ToDictionary(s => s.Period);

        foreach (var (period, change) in changes)
        {
            byPeriod.TryGetValue(period, out var slot);

            if (change.IsCancellation)
            {
                // 없는 수업의 휴강은 보여 줄 것이 없다. 빈 "휴강" 칸을 만들면
                // 그 교시에 원래 수업이 있었던 것처럼 읽힌다.
                if (slot == null) continue;

                slot.ChangeKind = LessonChangeKind.Cancelled;
                slot.ChangeMemo = change.Memo;
                continue;
            }

            if (slot == null)
            {
                slot = new TimetableItemViewModel
                {
                    DayOfWeek = dayOfWeek,
                    Period = period,
                    IsEmpty = false
                };
                byPeriod[period] = slot;
            }

            // 대강은 내 수업이 아니다 — 원래 그 교시에 내 수업이 있었는지와 무관하게 따로 표시한다.
            slot.ChangeKind = change.IsSubstitute
                ? LessonChangeKind.Substitute
                : slots.Any(s => s.Period == period)
                    ? LessonChangeKind.Replaced
                    : LessonChangeKind.Added;

            slot.CourseNo = change.CourseNo ?? 0;
            slot.SubjectName = change.Subject;
            slot.Room = change.Room;
            slot.ChangeMemo = change.Memo;
        }

        return byPeriod.Values.OrderBy(s => s.Period).ToList();
    }
}
