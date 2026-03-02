using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services
{
    /// <summary>
    /// 주차별 수업 시수 계산기
    /// Course + ClassTimetable + SchoolSchedule 기반 자동 계산
    /// </summary>
    public class WeeklyHoursCalculator
    {
        private readonly ClassTimetableRepository _timetableRepo;
        private readonly SchoolScheduleRepository _scheduleRepo;
        private readonly CourseRepository _courseRepo;

        public WeeklyHoursCalculator(
            ClassTimetableRepository timetableRepo,
            SchoolScheduleRepository scheduleRepo,
            CourseRepository courseRepo)
        {
            _timetableRepo = timetableRepo;
            _scheduleRepo = scheduleRepo;
            _courseRepo = courseRepo;
        }

        /// <summary>
        /// Course + 학급 기반 주차별 수업 시수 자동 계산
        /// </summary>
        public async Task<List<WeeklyLessonHours>> CalculateAsync(
            int courseNo,
            int yearPlanNo,
            int targetGrade,
            int? targetClass,
            DateTime semesterStart,
            DateTime semesterEnd)
        {
            // 1. Course 정보 조회
            var course = await _courseRepo.GetByIdAsync(courseNo);
            if (course == null)
                throw new ArgumentException($"Course를 찾을 수 없습니다: No={courseNo}");

            // 2. 해당 학급의 시간표 조회 (과목 + 학급 기준)
            var timetables = await GetTimetablesForSubjectAsync(
                course.SchoolCode,
                course.Year,
                course.Semester,
                course.Subject,
                targetGrade,
                targetClass);

            // 3. 학사일정 조회
            var schedules = await _scheduleRepo.GetBySchoolYearAsync(
                course.SchoolCode,
                course.Year);

            // 4. 학기 주차 분할
            var weeks = GetSemesterWeeks(semesterStart, semesterEnd);

            // 5. 주차별 시수 계산
            var weeklyHours = new List<WeeklyLessonHours>();

            foreach (var week in weeks)
            {
                int calculatedHours = CalculateHoursForWeek(
                    week,
                    timetables,
                    schedules,
                    targetGrade);

                weeklyHours.Add(new WeeklyLessonHours
                {
                    YearPlanNo = yearPlanNo,
                    Week = week.Number,
                    WeekStartDate = week.StartDate.ToString("yyyy-MM-dd"),
                    WeekEndDate = week.EndDate.ToString("yyyy-MM-dd"),
                    AutoCalculatedHours = calculatedHours,
                    PlannedHours = calculatedHours,  // 초기값 (사용자 수정 가능)
                    ActualHours = 0,
                    IsModified = 0
                });
            }

            return weeklyHours;
        }

        /// <summary>
        /// 특정 과목 + 학급의 시간표 조회
        /// </summary>
        private async Task<List<ClassTimetable>> GetTimetablesForSubjectAsync(
            string schoolCode,
            int year,
            int semester,
            string subject,
            int grade,
            int? classNo)
        {
            // ClassTimetable에서 해당 학년의 시간표 조회
            var allTimetables = await _timetableRepo.GetByGradeAsync(
                schoolCode, year, semester, grade);

            // 과목명으로 필터링
            var filtered = allTimetables
                .Where(t => t.SubjectName == subject);

            // 특정 반인 경우 반으로 추가 필터링
            if (classNo.HasValue)
            {
                filtered = filtered.Where(t => t.Class == classNo.Value);
            }

            return filtered.ToList();
        }

        /// <summary>
        /// 특정 주의 수업 가능 시수 계산
        /// </summary>
        private int CalculateHoursForWeek(
            WeekInfo week,
            List<ClassTimetable> timetables,
            List<SchoolSchedule> schedules,
            int targetGrade)
        {
            int hours = 0;

            // 주의 각 날짜를 순회
            for (var date = week.StartDate; date <= week.EndDate; date = date.AddDays(1))
            {
                // 주말 제외
                if (date.DayOfWeek == DayOfWeek.Saturday ||
                    date.DayOfWeek == DayOfWeek.Sunday)
                    continue;

                // 수업 가능일인지 확인
                if (!IsClassDay(date, schedules, targetGrade))
                    continue;

                // 해당 요일의 시간표 시수 추가
                int dayOfWeek = GetDayOfWeekNumber(date.DayOfWeek);
                hours += timetables.Count(t => t.DayOfWeek == dayOfWeek);
            }

            return hours;
        }

        /// <summary>
        /// 특정 날짜가 수업 가능일인지 판단
        /// </summary>
        private bool IsClassDay(DateTime date, List<SchoolSchedule> schedules, int targetGrade)
        {
            // 해당 날짜의 학사일정 찾기
            var daySchedules = schedules.Where(s =>
                s.AA_YMD.Date == date.Date && !s.IsDeleted).ToList();

            // 휴업일/공휴일 체크
            if (daySchedules.Any(s => s.IsHoliday))
                return false;

            // 학년별 행사 체크 (해당 학년만 대상인 행사가 있으면 수업 불가)
            var gradeSpecificEvents = daySchedules.Where(s =>
                IsGradeSpecificEvent(s, targetGrade) && !string.IsNullOrEmpty(s.EVENT_NM)).ToList();

            if (gradeSpecificEvents.Any())
            {
                // 현장체험학습 등 학년 대상 행사가 있으면 수업 불가
                return false;
            }

            return true;
        }

        /// <summary>
        /// 특정 학년 대상 행사인지 확인
        /// </summary>
        private bool IsGradeSpecificEvent(SchoolSchedule schedule, int grade)
        {
            // 해당 학년이 대상인 행사인지 확인
            bool isTarget = grade switch
            {
                1 => schedule.ONE_GRADE_EVENT_YN,
                2 => schedule.TW_GRADE_EVENT_YN,
                3 => schedule.THREE_GRADE_EVENT_YN,
                4 => schedule.FR_GRADE_EVENT_YN,
                5 => schedule.FIV_GRADE_EVENT_YN,
                6 => schedule.SIX_GRADE_EVENT_YN,
                _ => false
            };

            // 전체 학년 대상인지 확인 (중학교: 1,2,3학년)
            bool allGrades = schedule.ONE_GRADE_EVENT_YN &&
                            schedule.TW_GRADE_EVENT_YN &&
                            schedule.THREE_GRADE_EVENT_YN;

            // 대상이면서 전체 학년 대상이 아닌 경우
            return isTarget && !allGrades;
        }

        /// <summary>
        /// 학기를 주 단위로 분할
        /// </summary>
        public List<WeekInfo> GetSemesterWeeks(DateTime start, DateTime end)
        {
            var weeks = new List<WeekInfo>();
            int weekNumber = 1;

            // 첫 주 시작일 (해당 주의 월요일로 정렬)
            var weekStart = start;
            while (weekStart.DayOfWeek != DayOfWeek.Monday && weekStart > start.AddDays(-7))
            {
                weekStart = weekStart.AddDays(-1);
            }
            if (weekStart < start)
                weekStart = start;

            while (weekStart <= end)
            {
                var weekEnd = weekStart.AddDays(6); // 일요일
                if (weekEnd > end)
                    weekEnd = end;

                // 평일이 하나라도 포함된 주만 추가
                bool hasWeekday = false;
                for (var d = weekStart; d <= weekEnd; d = d.AddDays(1))
                {
                    if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                    {
                        hasWeekday = true;
                        break;
                    }
                }

                if (hasWeekday)
                {
                    weeks.Add(new WeekInfo
                    {
                        Number = weekNumber++,
                        StartDate = weekStart,
                        EndDate = weekEnd
                    });
                }

                weekStart = weekStart.AddDays(7);
            }

            return weeks;
        }

        /// <summary>
        /// System.DayOfWeek → ClassTimetable.DayOfWeek (1~5)
        /// </summary>
        private int GetDayOfWeekNumber(DayOfWeek dayOfWeek)
        {
            return dayOfWeek switch
            {
                DayOfWeek.Monday => 1,
                DayOfWeek.Tuesday => 2,
                DayOfWeek.Wednesday => 3,
                DayOfWeek.Thursday => 4,
                DayOfWeek.Friday => 5,
                _ => 0
            };
        }

        /// <summary>
        /// 재계산 (학사일정 변경 시)
        /// 사용자가 수정한 주차(IsModified=1)는 유지
        /// </summary>
        public async Task<List<WeeklyLessonHours>> RecalculateAsync(
            int yearPlanNo,
            int fromWeek,
            List<WeeklyLessonHours> existingHours,
            int courseNo,
            int targetGrade,
            int? targetClass,
            DateTime semesterStart,
            DateTime semesterEnd)
        {
            // 새로 계산
            var newHours = await CalculateAsync(
                courseNo, yearPlanNo, targetGrade, targetClass,
                semesterStart, semesterEnd);

            // 사용자가 수정한 주차는 유지
            foreach (var existing in existingHours.Where(e => e.Week >= fromWeek))
            {
                var newWeek = newHours.FirstOrDefault(n => n.Week == existing.Week);
                if (newWeek != null && existing.IsModified == 1)
                {
                    newWeek.PlannedHours = existing.PlannedHours;
                    newWeek.IsModified = 1;
                    newWeek.Notes = existing.Notes;
                }
            }

            return newHours;
        }

        /// <summary>
        /// 특정 날짜가 몇 주차인지 계산
        /// </summary>
        public int GetWeekNumberForDate(DateTime date, DateTime semesterStart)
        {
            var weeks = GetSemesterWeeks(semesterStart, date.AddDays(7));
            var week = weeks.FirstOrDefault(w => date >= w.StartDate && date <= w.EndDate);
            return week?.Number ?? 0;
        }
    }

    /// <summary>
    /// 주차 정보 헬퍼 클래스
    /// </summary>
    public class WeekInfo
    {
        public int Number { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public string DateRange => $"{StartDate:MM/dd} ~ {EndDate:MM/dd}";

        public override string ToString()
        {
            return $"{Number}주차 ({DateRange})";
        }
    }
}
