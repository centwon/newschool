using System;
using System.Collections.Generic;
using NewSchool.Models;

namespace NewSchool.Helpers;

/// <summary>
/// 학사일정·요일 판정을 한곳에 모은 헬퍼.
///
/// 규칙을 화면마다 복붙하지 말 것 — 예전에 두 곳의 기준이 어긋나
/// "최초 배치 땐 뺐던 공휴일로 밀기가 수업을 옮기는" 식으로 조용히 깨진 적이 있다.
/// 지금은 시수 관리(<c>WeeklyHoursCalculator</c>)가 이 규칙을 쓴다.
/// </summary>
public static class SchoolCalendar
{
    /// <summary>
    /// 수업을 배치하면 안 되는 날인가. 판단 근거는 NEIS 의 <b>수업공제일자명</b>
    /// (<see cref="SchoolSchedule.IsHoliday"/>) 하나다.
    ///
    /// <para>예전에는 행사명에 "휴업·공휴·방학" 이 들어가는지도 함께 봤다. 그 짐작이
    /// <b>"여름방학식" 을 휴업일로 만들었다</b> — 방학식은 수업공제일자명이 "해당없음" 인
    /// 엄연한 수업일인데, 시수에서 하루가 조용히 빠졌다.</para>
    ///
    /// <para>실제 학사일정을 확인해 보니 짐작이 필요한 자리가 아니었다: 수업공제일자명은
    /// <b>모든 행에 채워져 있고</b>(NEIS 자료든 앱에서 손으로 넣은 것이든), 이름 규칙이
    /// 추가로 잡아내던 행은 저 방학식 하나뿐이었다 — 그것도 틀린 쪽으로.</para>
    ///
    /// <para>(schedule 이 null 이어도 안전하다 — 예전에는 이 자리에서 NullReference 가 나면
    ///  바깥 catch 가 삼켜서 <b>휴일 목록 전체</b>가 조용히 비어버렸다)</para>
    /// </summary>
    public static bool IsNonTeachingDay(SchoolSchedule schedule)
        => schedule?.IsHoliday == true;

    /// <summary>
    /// 학교급 이름에서 학년 수를 얻는다. 모르면 0.
    ///
    /// <para>NEIS 가 주는 <c>School.SchoolType</c> 값(초등학교·중학교·고등학교·특수학교…)을 받는다.
    /// 특수학교·각종학교는 유·초·중·고 과정이 섞여 학년 수를 단정할 수 없으므로 0(모름)을 준다.</para>
    /// </summary>
    public static int GradeCountFor(string? schoolType)
    {
        if (string.IsNullOrWhiteSpace(schoolType)) return 0;
        if (schoolType.Contains("초등")) return 6;
        if (schoolType.Contains("중학")) return 3;
        if (schoolType.Contains("고등")) return 3;
        return 0;
    }

    /// <summary>
    /// 특정 학년만 빠지는 행사인가 (현장체험학습·수학여행 등).
    ///
    /// 전 학년이 대상이면 학사일정의 성격이 다르다(개교기념일처럼 휴업 판정에 맡긴다).
    /// 여기서 걸러야 하는 건 "우리 학년만 교실에 없는 날"이다.
    /// 대상 학년 표시가 아예 없는 행사는 학년을 가리지 않는 것으로 본다.
    ///
    /// <para><paramref name="gradeCount"/> 는 그 학교에 실제로 있는 학년 수다
    /// (<see cref="GradeCountFor"/>). 주면 "표시된 학년이 전부는 아닌가"로 판정한다 —
    /// 3학년제 중학교에서 1·2학년만 수련회를 가는 날은 그 두 학년의 수업일에서 빠져야 하는데,
    /// 예전에는 <b>표시된 학년이 하나뿐일 때만</b> 걸러서 두 학년 모두 정상 수업일로 셌다.</para>
    ///
    /// <para>0(모름)이면 종전 기준(<c>표시 학년 == 1</c>)을 쓴다. 학년 수를 모르는 채
    /// "전부는 아님" 을 적용하면 3학년제 학교의 전교 행사(3개 학년 표시)를 학년 전용으로
    /// 오판한다 — NEIS 플래그 배열은 학교급과 무관하게 항상 6칸이기 때문이다.</para>
    /// </summary>
    public static bool IsGradeOnlyEvent(SchoolSchedule schedule, int grade, int gradeCount = 0)
    {
        if (schedule == null) return false;
        if (string.IsNullOrWhiteSpace(schedule.EVENT_NM)) return false;

        bool[] flags =
        [
            schedule.ONE_GRADE_EVENT_YN,
            schedule.TW_GRADE_EVENT_YN,
            schedule.THREE_GRADE_EVENT_YN,
            schedule.FR_GRADE_EVENT_YN,
            schedule.FIV_GRADE_EVENT_YN,
            schedule.SIX_GRADE_EVENT_YN
        ];

        if (grade < 1 || grade > flags.Length) return false;
        if (!flags[grade - 1]) return false;

        // 학년 수를 알고, 그 안에 이 학년이 들어 있을 때만 "전부는 아님" 기준을 쓴다.
        if (gradeCount > 0 && gradeCount <= flags.Length && grade <= gradeCount)
        {
            int markedInSchool = 0;
            for (int i = 0; i < gradeCount; i++)
            {
                if (flags[i]) markedInSchool++;
            }

            // 전 학년이 표시됐으면 학교 전체 행사에 가깝다(휴업 판정에 맡긴다).
            return markedInSchool < gradeCount;
        }

        // 학년 수를 모를 때: 표시된 학년이 하나뿐인 경우만 "그 학년만 빠지는 행사"로 본다.
        int marked = 0;
        foreach (var flag in flags)
        {
            if (flag) marked++;
        }

        return marked == 1;
    }

    /// <summary>
    /// 해당 날짜에 <paramref name="grade"/> 학년이 정상 수업을 하는가.
    /// <paramref name="gradeCount"/> 설명은 <see cref="IsGradeOnlyEvent"/> 참고.
    /// </summary>
    public static bool IsTeachingDayFor(
        DateTime date, IEnumerable<SchoolSchedule> schedules, int grade, int gradeCount = 0)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        foreach (var schedule in schedules)
        {
            if (schedule == null || schedule.IsDeleted) continue;
            if (schedule.AA_YMD.Date != date.Date) continue;

            if (IsNonTeachingDay(schedule)) return false;
            if (IsGradeOnlyEvent(schedule, grade, gradeCount)) return false;
        }

        return true;
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
