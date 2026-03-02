using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services
{
    /// <summary>
    /// 주차별 시수 수정 헬퍼
    /// 사용자가 시수를 수정할 때 사용하는 유틸리티 클래스
    /// </summary>
    public class WeeklyHoursHelper
    {
        private readonly WeeklyLessonHoursRepository _weeklyHoursRepo;
        private readonly SubjectYearPlanRepository _yearPlanRepo;

        public WeeklyHoursHelper(
            WeeklyLessonHoursRepository weeklyHoursRepo,
            SubjectYearPlanRepository yearPlanRepo)
        {
            _weeklyHoursRepo = weeklyHoursRepo;
            _yearPlanRepo = yearPlanRepo;
        }

        /// <summary>
        /// 일괄 시수 추가 (균등 배분)
        /// </summary>
        public async Task AddHoursEvenlyAsync(
            int yearPlanNo,
            int additionalHours,
            int? fromWeek = null)
        {
            var weeklyHours = await _weeklyHoursRepo.GetByYearPlanAsync(yearPlanNo);

            // 적용 범위
            var targetWeeks = fromWeek.HasValue
                ? weeklyHours.Where(w => w.Week >= fromWeek.Value).ToList()
                : weeklyHours;

            if (targetWeeks.Count == 0) return;

            // 주당 추가 시수
            int hoursPerWeek = additionalHours / targetWeeks.Count;
            int remainder = additionalHours % targetWeeks.Count;

            for (int i = 0; i < targetWeeks.Count; i++)
            {
                var week = targetWeeks[i];
                int addHours = hoursPerWeek + (i < remainder ? 1 : 0);

                if (addHours > 0)
                {
                    week.PlannedHours += addHours;
                    week.IsModified = 1;

                    if (string.IsNullOrEmpty(week.Notes))
                    {
                        week.Notes = $"추가 수업 {addHours}시간";
                    }
                    else
                    {
                        week.Notes += $", 추가 {addHours}시간";
                    }

                    await _weeklyHoursRepo.UpdateAsync(week);
                }
            }

            // 전체 총 시수 업데이트
            await UpdateTotalPlannedHoursAsync(yearPlanNo);
        }

        /// <summary>
        /// 일괄 시수 추가 (비율 배분)
        /// 기존 시수가 많은 주에 더 많이 배분
        /// </summary>
        public async Task AddHoursProportionallyAsync(
            int yearPlanNo,
            int additionalHours,
            int? fromWeek = null)
        {
            var weeklyHours = await _weeklyHoursRepo.GetByYearPlanAsync(yearPlanNo);

            // 적용 범위
            var targetWeeks = fromWeek.HasValue
                ? weeklyHours.Where(w => w.Week >= fromWeek.Value).ToList()
                : weeklyHours;

            if (targetWeeks.Count == 0) return;

            // 총 기존 시수
            int totalExisting = targetWeeks.Sum(w => w.PlannedHours);
            if (totalExisting == 0)
            {
                // 기존 시수가 0이면 균등 배분
                await AddHoursEvenlyAsync(yearPlanNo, additionalHours, fromWeek);
                return;
            }

            int distributed = 0;

            for (int i = 0; i < targetWeeks.Count; i++)
            {
                var week = targetWeeks[i];
                int addHours;

                if (i == targetWeeks.Count - 1)
                {
                    // 마지막 주에 나머지 할당
                    addHours = additionalHours - distributed;
                }
                else
                {
                    // 비율에 따라 배분
                    double ratio = (double)week.PlannedHours / totalExisting;
                    addHours = (int)(additionalHours * ratio);
                }

                if (addHours > 0)
                {
                    week.PlannedHours += addHours;
                    week.IsModified = 1;

                    if (string.IsNullOrEmpty(week.Notes))
                    {
                        week.Notes = $"비율 배분 {addHours}시간";
                    }
                    else
                    {
                        week.Notes += $", 비율 배분 {addHours}시간";
                    }

                    await _weeklyHoursRepo.UpdateAsync(week);
                    distributed += addHours;
                }
            }

            // 전체 총 시수 업데이트
            await UpdateTotalPlannedHoursAsync(yearPlanNo);
        }

        /// <summary>
        /// 특정 주 시수 수정
        /// </summary>
        public async Task UpdateWeekHoursAsync(
            int weeklyHoursNo,
            int newPlannedHours,
            string? notes = null)
        {
            var week = await _weeklyHoursRepo.GetByIdAsync(weeklyHoursNo);
            if (week == null) return;

            week.PlannedHours = newPlannedHours;
            week.IsModified = 1;

            if (notes != null)
            {
                week.Notes = notes;
            }

            await _weeklyHoursRepo.UpdateAsync(week);

            // 전체 계획 총 시수 업데이트
            await UpdateTotalPlannedHoursAsync(week.YearPlanNo);
        }

        /// <summary>
        /// 여러 주 시수 일괄 수정
        /// </summary>
        public async Task UpdateMultipleWeeksAsync(
            List<(int weeklyHoursNo, int newPlannedHours, string? notes)> updates)
        {
            if (updates == null || updates.Count == 0) return;

            int? yearPlanNo = null;

            foreach (var (weeklyHoursNo, newPlannedHours, notes) in updates)
            {
                var week = await _weeklyHoursRepo.GetByIdAsync(weeklyHoursNo);
                if (week == null) continue;

                yearPlanNo = week.YearPlanNo;

                week.PlannedHours = newPlannedHours;
                week.IsModified = 1;

                if (notes != null)
                {
                    week.Notes = notes;
                }

                await _weeklyHoursRepo.UpdateAsync(week);
            }

            if (yearPlanNo.HasValue)
            {
                await UpdateTotalPlannedHoursAsync(yearPlanNo.Value);
            }
        }

        /// <summary>
        /// 자동 계산 값으로 리셋 (특정 주)
        /// </summary>
        public async Task ResetToAutoCalculatedAsync(int weeklyHoursNo)
        {
            var week = await _weeklyHoursRepo.GetByIdAsync(weeklyHoursNo);
            if (week == null) return;

            week.PlannedHours = week.AutoCalculatedHours;
            week.IsModified = 0;
            week.Notes = string.Empty;

            await _weeklyHoursRepo.UpdateAsync(week);
            await UpdateTotalPlannedHoursAsync(week.YearPlanNo);
        }

        /// <summary>
        /// 자동 계산 값으로 전체 리셋
        /// </summary>
        public async Task ResetAllToAutoCalculatedAsync(int yearPlanNo)
        {
            var weeklyHours = await _weeklyHoursRepo.GetByYearPlanAsync(yearPlanNo);

            foreach (var week in weeklyHours)
            {
                week.PlannedHours = week.AutoCalculatedHours;
                week.IsModified = 0;
                week.Notes = string.Empty;

                await _weeklyHoursRepo.UpdateAsync(week);
            }

            await UpdateTotalPlannedHoursAsync(yearPlanNo);
        }

        /// <summary>
        /// 특정 주부터 자동 계산 값으로 리셋
        /// </summary>
        public async Task ResetFromWeekAsync(int yearPlanNo, int fromWeek)
        {
            var weeklyHours = await _weeklyHoursRepo.GetByYearPlanAsync(yearPlanNo);
            var targetWeeks = weeklyHours.Where(w => w.Week >= fromWeek).ToList();

            foreach (var week in targetWeeks)
            {
                week.PlannedHours = week.AutoCalculatedHours;
                week.IsModified = 0;
                week.Notes = string.Empty;

                await _weeklyHoursRepo.UpdateAsync(week);
            }

            await UpdateTotalPlannedHoursAsync(yearPlanNo);
        }

        /// <summary>
        /// 시수 시뮬레이션 (실제 저장하지 않고 결과 미리보기)
        /// </summary>
        public async Task<HoursSimulationResult> SimulateAddHoursAsync(
            int yearPlanNo,
            int additionalHours,
            int? fromWeek = null,
            bool proportional = false)
        {
            var weeklyHours = await _weeklyHoursRepo.GetByYearPlanAsync(yearPlanNo);

            var result = new HoursSimulationResult
            {
                OriginalTotal = weeklyHours.Sum(w => w.PlannedHours),
                AdditionalHours = additionalHours,
                FromWeek = fromWeek,
                IsProportional = proportional
            };

            // 적용 범위
            var targetWeeks = fromWeek.HasValue
                ? weeklyHours.Where(w => w.Week >= fromWeek.Value).ToList()
                : weeklyHours;

            if (targetWeeks.Count == 0)
            {
                result.NewTotal = result.OriginalTotal;
                return result;
            }

            int totalExisting = targetWeeks.Sum(w => w.PlannedHours);
            int distributed = 0;

            foreach (var week in targetWeeks)
            {
                int addHours;

                if (proportional && totalExisting > 0)
                {
                    double ratio = (double)week.PlannedHours / totalExisting;
                    addHours = (int)(additionalHours * ratio);
                }
                else
                {
                    int hoursPerWeek = additionalHours / targetWeeks.Count;
                    int remainder = additionalHours % targetWeeks.Count;
                    int index = targetWeeks.IndexOf(week);
                    addHours = hoursPerWeek + (index < remainder ? 1 : 0);
                }

                result.WeeklyChanges.Add(new WeekHoursChange
                {
                    Week = week.Week,
                    OriginalHours = week.PlannedHours,
                    AddedHours = addHours,
                    NewHours = week.PlannedHours + addHours
                });

                distributed += addHours;
            }

            result.NewTotal = result.OriginalTotal + additionalHours;
            result.AffectedWeeks = targetWeeks.Count;

            return result;
        }

        /// <summary>
        /// 총 계획 시수 재계산 및 업데이트
        /// </summary>
        private async Task UpdateTotalPlannedHoursAsync(int yearPlanNo)
        {
            var plan = await _yearPlanRepo.GetByIdAsync(yearPlanNo);
            if (plan == null) return;

            int totalPlannedHours = await _weeklyHoursRepo.GetTotalPlannedHoursAsync(yearPlanNo);

            if (plan.TotalPlannedHours != totalPlannedHours)
            {
                await _yearPlanRepo.UpdateTotalPlannedHoursAsync(yearPlanNo, totalPlannedHours);
            }
        }

        /// <summary>
        /// 시수 통계 조회
        /// </summary>
        public async Task<HoursStatistics> GetStatisticsAsync(int yearPlanNo)
        {
            var weeklyHours = await _weeklyHoursRepo.GetByYearPlanAsync(yearPlanNo);

            return new HoursStatistics
            {
                TotalWeeks = weeklyHours.Count,
                TotalAutoCalculated = weeklyHours.Sum(w => w.AutoCalculatedHours),
                TotalPlanned = weeklyHours.Sum(w => w.PlannedHours),
                TotalActual = weeklyHours.Sum(w => w.ActualHours),
                ModifiedWeeksCount = weeklyHours.Count(w => w.IsModified == 1),
                AverageHoursPerWeek = weeklyHours.Count > 0
                    ? (double)weeklyHours.Sum(w => w.PlannedHours) / weeklyHours.Count
                    : 0,
                MaxHoursInWeek = weeklyHours.Count > 0 ? weeklyHours.Max(w => w.PlannedHours) : 0,
                MinHoursInWeek = weeklyHours.Count > 0 ? weeklyHours.Min(w => w.PlannedHours) : 0,
                TotalAddedHours = weeklyHours.Sum(w => w.PlannedHours - w.AutoCalculatedHours)
            };
        }
    }

    /// <summary>
    /// 시수 추가 시뮬레이션 결과
    /// </summary>
    public class HoursSimulationResult
    {
        public int OriginalTotal { get; set; }
        public int AdditionalHours { get; set; }
        public int NewTotal { get; set; }
        public int? FromWeek { get; set; }
        public bool IsProportional { get; set; }
        public int AffectedWeeks { get; set; }
        public List<WeekHoursChange> WeeklyChanges { get; set; } = new List<WeekHoursChange>();
    }

    /// <summary>
    /// 주차별 시수 변경 정보
    /// </summary>
    public class WeekHoursChange
    {
        public int Week { get; set; }
        public int OriginalHours { get; set; }
        public int AddedHours { get; set; }
        public int NewHours { get; set; }

        public string Display => $"{Week}주차: {OriginalHours} → {NewHours} (+{AddedHours})";
    }

    /// <summary>
    /// 시수 통계
    /// </summary>
    public class HoursStatistics
    {
        public int TotalWeeks { get; set; }
        public int TotalAutoCalculated { get; set; }
        public int TotalPlanned { get; set; }
        public int TotalActual { get; set; }
        public int ModifiedWeeksCount { get; set; }
        public double AverageHoursPerWeek { get; set; }
        public int MaxHoursInWeek { get; set; }
        public int MinHoursInWeek { get; set; }
        public int TotalAddedHours { get; set; }

        public string Summary => $"총 {TotalWeeks}주, 계획 {TotalPlanned}시간 (자동계산 {TotalAutoCalculated} + 추가 {TotalAddedHours})";
    }
}
