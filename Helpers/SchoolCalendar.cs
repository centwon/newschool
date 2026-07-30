using System;
using NewSchool.Models;

namespace NewSchool.Helpers;

/// <summary>
/// 학사일정·요일 판정을 한곳에 모은 헬퍼.
///
/// ⚠ 여기 있는 두 규칙은 원래 자동배치(<c>SchedulingEngine</c>)와 밀기/당기기
/// (<c>ScheduleShiftService</c>)에 <b>복붙</b>돼 있었다. 두 곳의 기준이 어긋나면
/// "최초 배치 땐 뺐던 공휴일로 밀기가 수업을 옮기는" 식으로 조용히 깨지므로
/// (실제로 한 번 어긋나 맞춘 이력이 있다) 반드시 이 메서드만 쓸 것.
/// </summary>
public static class SchoolCalendar
{
    /// <summary>
    /// 수업을 배치하면 안 되는 날인가. NEIS 의 휴업일·공휴일 구분과 행사명을 함께 본다.
    /// (행사명이 비어 있어도 안전하다 — 예전에는 이 자리에서 NullReference 가 나면
    ///  바깥 catch 가 삼켜서 <b>휴일 목록 전체</b>가 조용히 비어버렸다)
    /// </summary>
    public static bool IsNonTeachingDay(SchoolSchedule schedule)
    {
        if (schedule == null) return false;
        if (schedule.IsHoliday) return true;

        string name = schedule.EVENT_NM ?? string.Empty;
        return name.Contains("휴업") || name.Contains("공휴") || name.Contains("방학");
    }

    /// <summary>
    /// 날짜를 <c>Lesson.DayOfWeek</c>·<c>ClassTimetable.DayOfWeek</c> 규약(월=1 … 토=6, 일=7)으로 변환.
    ///
    /// ⚠ .NET 의 <see cref="DayOfWeek"/> 는 <b>일요일이 0</b> 이라 그대로 캐스팅하면 일요일만 어긋난다.
    /// 월~토는 우연히 일치해서 눈에 잘 띄지 않는다.
    /// </summary>
    public static int ToLessonDayOfWeek(DateTime date)
    {
        int dow = (int)date.DayOfWeek;
        return dow == 0 ? 7 : dow;
    }
}
