using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.ViewModels;
using Windows.Media.Miracast;

namespace NewSchool.Services
{
    /// <summary>
    /// ClassDiary Service
    /// 학급 일지 통합 관리 (출결, 메모, 알림장, 생활 기록)
    /// StudentLog와 연동하여 Life 필드 자동 생성
    /// </summary>
    public class ClassDiaryService : IDisposable
    {
        private readonly ClassDiaryRepository _diaryRepo;
        private readonly StudentLogRepository _logRepo;
        private bool _disposed;

        public ClassDiaryService(string dbPath)
        {
            _diaryRepo = new ClassDiaryRepository(dbPath);
            _logRepo = new StudentLogRepository(dbPath);
        }

        #region 일지 생성 & 수정

        /// <summary>
        /// 학급 일지 생성 또는 수정
        /// 이미 존재하면 수정, 없으면 생성
        /// </summary>
        public async Task<ClassDiary> CreateOrUpdateAsync(ClassDiary diary)
        {
            // 유효성 검증
            if (!diary.IsValid())
            {
                throw new ArgumentException("학급 일지 정보가 유효하지 않습니다.");
            }

            try
            {
                // 기존 일지 확인
                var existing = await _diaryRepo.GetByDateAsync(diary.SchoolCode, diary.Year, diary.Grade, diary.Class, diary.Date);

                if (existing != null)
                {
                    // 수정
                    diary.No = existing.No;
                    await _diaryRepo.UpdateAsync(diary);
                    return diary;
                }
                else
                {
                    // 생성
                    await _diaryRepo.CreateAsync(diary);
                    return diary;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"학급 일지 저장 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 학급 일지 조회 (없으면 빈 일지 생성)
        /// </summary>
        public async Task<ClassDiary> GetOrCreateDiaryAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, DateTime date, string teacherId)
        {
            var diary = await _diaryRepo.GetByDateAsync(
                schoolCode, year, grade, classNum, date);

            if (diary == null)
            {
                // 빈 일지 생성 (DB에 저장하지 않음)
                diary = new ClassDiary(schoolCode, year, semester, grade, classNum, date, teacherId);
            }

            return diary;
        }

        /// <summary>
        /// 학급 일지 조회
        /// </summary>
        public async Task<ClassDiary?> GetDiaryAsync(
            string schoolCode, int year, 
            int grade, int classNum, DateTime date)
        {
            return await _diaryRepo.GetByDateAsync(schoolCode, year, grade, classNum, date);
        }

        /// <summary>
        /// 월별 일지 목록 조회
        /// </summary>
        public async Task<List<ClassDiary>> GetMonthDiariesAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, int month)
        {
            return await _diaryRepo.GetByMonthAsync(
                schoolCode, year, semester, grade, classNum, month);
        }

        /// <summary>
        /// 기간별 일지 목록 조회
        /// </summary>
        public async Task<List<ClassDiary>> GetDateRangeDiariesAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, DateTime startDate, DateTime endDate)
        {
            return await _diaryRepo.GetByDateRangeAsync(
                schoolCode, year, semester, grade, classNum, startDate, endDate);
        }

        /// <summary>
        /// 학급 전체 일지 조회
        /// </summary>
        public async Task<List<ClassDiary>> GetClassDiariesAsync(
            string schoolCode, int year, int semester, int grade, int classNum)
        {
            return await _diaryRepo.GetByClassAsync(
                schoolCode, year, grade, classNum);
        }

        #endregion

        #region 출결 관리

        /// <summary>
        /// 출결 정보 업데이트
        /// </summary>
        public async Task<bool> UpdateAttendanceAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, DateTime date,
            string? absent = null, string? late = null, string? leaveEarly = null)
        {
            var diary = await GetOrCreateDiaryAsync(
                schoolCode, year, semester, grade, classNum, date, string.Empty);

            if (absent != null) diary.Absent = absent;
            if (late != null) diary.Late = late;
            if (leaveEarly != null) diary.LeaveEarly = leaveEarly;

            var result = await CreateOrUpdateAsync(diary);
            return result.No > 0;
        }

        /// <summary>
        /// 출결 통계 (기간별)
        /// </summary>
        public async Task<AttendanceStats> GetAttendanceStatsAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, DateTime startDate, DateTime endDate)
        {
            var diaries = await _diaryRepo.GetByDateRangeAsync(
                schoolCode, year, semester, grade, classNum, startDate, endDate);

            var stats = new AttendanceStats();

            foreach (var diary in diaries)
            {
                if (!string.IsNullOrWhiteSpace(diary.Absent))
                    stats.AbsentCount += diary.Absent.Split(',').Length;

                if (!string.IsNullOrWhiteSpace(diary.Late))
                    stats.LateCount += diary.Late.Split(',').Length;

                if (!string.IsNullOrWhiteSpace(diary.LeaveEarly))
                    stats.LeaveEarlyCount += diary.LeaveEarly.Split(',').Length;
            }

            stats.TotalDays = diaries.Count;
            return stats;
        }

        /// <summary>
        /// 특정 학생의 출결 기록 조회
        /// </summary>
        public async Task<List<StudentAttendanceRecord>> GetStudentAttendanceAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, string studentName, DateTime startDate, DateTime endDate)
        {
            var diaries = await _diaryRepo.GetByDateRangeAsync(
                schoolCode, year, semester, grade, classNum, startDate, endDate);

            var records = new List<StudentAttendanceRecord>();

            foreach (var diary in diaries)
            {
                if (diary.HasAttendanceIssue(studentName))
                {
                    records.Add(new StudentAttendanceRecord
                    {
                        Date = diary.Date,
                        StudentName = studentName,
                        Status = diary.GetAttendanceStatus(studentName)
                    });
                }
            }

            return records;
        }

        #endregion

        #region Life 필드 자동 생성

        /// <summary>
        /// Life 필드 자동 생성 (StudentLog에서)
        /// 특정 날짜의 모든 학생 기록을 요약
        /// </summary>
        public async Task<string> GenerateLifeRecordAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, DateTime date)
        {
            // 해당 날짜의 모든 학생 로그 조회 (구현 필요)
            // 현재는 간단한 구현
            var dateStr = date.ToString("yyyy-MM-dd");
            var logs = await _logRepo.GetByDateRangeAsync(
                "", dateStr, dateStr); // StudentID가 없으므로 모든 학생

            if (logs.Count == 0)
                return string.Empty;

            // 카테고리별 분류
            var categorized = logs.GroupBy(l => l.Category);

            var sb = new StringBuilder();

            foreach (var group in categorized)
            {
                //var categoryName = GetCategoryName(group.Key);
                //sb.AppendLine($"[{categoryName}]");

                foreach (var log in group)
                {
                    if (!string.IsNullOrWhiteSpace(log.Log))
                    {
                        sb.AppendLine($"- {log.Log}");
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// 특정 학생의 Life 기록 생성
        /// </summary>
        public async Task<string> GenerateStudentLifeRecordAsync(
            string studentId, int year, int semester, DateTime date)
        {
            var dateStr = date.ToString("yyyy-MM-dd");
            var logs = await _logRepo.GetByDateRangeAsync(studentId, dateStr, dateStr);

            if (logs.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();

            foreach (var log in logs)
            {
                if (!string.IsNullOrWhiteSpace(log.Log))
                {
                    sb.AppendLine($"• {log.Log}");
                }
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Life 필드 업데이트
        /// </summary>
        public async Task<bool> UpdateLifeRecordAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, DateTime date)
        {
            var diary = await _diaryRepo.GetByDateAsync(
                schoolCode, year, grade, classNum, date);

            if (diary == null)
                return false;

            var lifeRecord = await GenerateLifeRecordAsync(
                schoolCode, year, semester, grade, classNum, date);

            diary.Life = lifeRecord;

            return await _diaryRepo.UpdateAsync(diary);
        }

        #endregion

        #region 메모 & 알림장

        /// <summary>
        /// 메모 업데이트
        /// </summary>
        public async Task<bool> UpdateMemoAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, DateTime date, string memo)
        {
            var diary = await GetOrCreateDiaryAsync(
                schoolCode, year, semester, grade, classNum, date, string.Empty);

            diary.Memo = memo;

            var result = await CreateOrUpdateAsync(diary);
            return result.No > 0;
        }

        /// <summary>
        /// 알림장 업데이트
        /// </summary>
        public async Task<bool> UpdateNoticeAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, DateTime date, string notice)
        {
            var diary = await GetOrCreateDiaryAsync(
                schoolCode, year, semester, grade, classNum, date, string.Empty);

            diary.Notice = notice;

            var result = await CreateOrUpdateAsync(diary);
            return result.No > 0;
        }

        #endregion

        #region 검색 & 통계

        /// <summary>
        /// 일지 검색
        /// </summary>
        public async Task<List<ClassDiary>> SearchDiariesAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new List<ClassDiary>();

            return await _diaryRepo.SearchAsync(
                schoolCode, year, semester, grade, classNum, keyword);
        }

        /// <summary>
        /// 일지 작성 완성도 통계
        /// </summary>
        public async Task<DiaryCompletionStats> GetCompletionStatsAsync(
            string schoolCode, int year, int semester, int grade, int classNum)
        {
            var diaries = await _diaryRepo.GetByClassAsync(
                schoolCode, year, grade, classNum);

            var stats = new DiaryCompletionStats
            {
                TotalDiaries = diaries.Count,
                WithAttendance = diaries.Count(d => d.HasAttendanceIssues),
                WithMemo = diaries.Count(d => d.HasMemo),
                WithNotice = diaries.Count(d => d.HasNotice),
                WithLife = diaries.Count(d => d.HasLifeRecord)
            };

            return stats;
        }

        /// <summary>
        /// 일지 개수 조회
        /// </summary>
        public async Task<int> GetDiaryCountAsync(
            string schoolCode, int year, int semester, int grade, int classNum)
        {
            return await _diaryRepo.GetCountAsync(
                schoolCode, year, semester, grade, classNum);
        }

        /// <summary>
        /// 최근 일지 조회
        /// </summary>
        public async Task<ClassDiary?> GetLatestDiaryAsync(
            string schoolCode, int year, int semester, int grade, int classNum)
        {
            return await _diaryRepo.GetLatestAsync(
                schoolCode, year, semester, grade, classNum);
        }

        #endregion

        #region 일괄 처리

        /// <summary>
        /// 월별 일지 일괄 생성 (빈 일지)
        /// </summary>
        public async Task<int> CreateMonthDiariesAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, int month, string teacherId)
        {
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            int count = 0;

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                // 주말 제외 (선택사항)
                if (date.DayOfWeek == DayOfWeek.Saturday || 
                    date.DayOfWeek == DayOfWeek.Sunday)
                    continue;

                // 이미 존재하는지 확인
                var exists = await _diaryRepo.ExistsAsync(
                    schoolCode, year, semester, grade, classNum, date);

                if (!exists)
                {
                    var diary = new ClassDiary(
                        schoolCode, year, semester, grade, classNum, date, teacherId);

                    await _diaryRepo.CreateAsync(diary);
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 일지 삭제
        /// </summary>
        public async Task<bool> DeleteDiaryAsync(int no)
        {
            return await _diaryRepo.DeleteAsync(no);
        }

        #endregion

        #region 학생 로그 관리

        /// <summary>
        /// 특정 날짜와 학급의 학생 생활 로그 조회 (ViewModel으로 변환)
        /// </summary>
        public async Task<List<ViewModels.StudentLogViewModel>> GetStudentLogsByDateAsync(
            int grade, int classNum, DateTime date)
        {
            // 해당 날짜의 로그 조회 (작업 학년도 사용)
            var logs = await StudentLogService.GetByClassAsync(
                Settings.SchoolCode.Value, 
                Settings.WorkYear,  // date.Year 대신 Settings.WorkYear 사용
                grade, 
                classNum, 
                date);

            // StudentLogViewModel으로 변환
            var result = new List<ViewModels.StudentLogViewModel>();

            foreach(var log in logs)
            {
                // 학생 정보 조회 필요 (Student 테이블에서)
                // 현재는 기본값으로 설정
                var viewModel = await StudentLogViewModel.CreateAsync(log);
                result.Add(viewModel);
            }
            return result;
        }

        /// <summary>
        /// 학생 로그 저장 (생성 또는 수정)
        /// </summary>
        public async Task<bool> SaveStudentLogAsync(StudentLog log)
        {
            try
            {
                if (log.No > 0)
                {
                    // 수정
                    return await _logRepo.UpdateAsync(log);
                }
                else
                {
                    // 생성
                    await _logRepo.CreateAsync(log);
                    return log.No > 0;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"학생 로그 저장 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 학생 로그 삭제
        /// </summary>
        public async Task<bool> DeleteStudentLogAsync(int logNo)
        {
            try
            {
                return await _logRepo.DeleteAsync(logNo);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"학생 로그 삭제 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 학생 로그 일괄 저장
        /// </summary>
        public async Task<int> SaveStudentLogsAsync(List<StudentLog> logs)
        {
            int count = 0;
            foreach (var log in logs)
            {
                if (await SaveStudentLogAsync(log))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 학생 로그 일괄 삭제
        /// </summary>
        public async Task<int> DeleteStudentLogsAsync(List<int> logNos)
        {
            int count = 0;
            foreach (var logNo in logNos)
            {
                if (await DeleteStudentLogAsync(logNo))
                    count++;
            }
            return count;
        }

        #endregion

        #region Helper Methods

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _diaryRepo?.Dispose();
                _logRepo?.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }

    #region ViewModel Classes

    /// <summary>
    /// 출결 통계
    /// </summary>
    public class AttendanceStats
    {
        public int TotalDays { get; set; }
        public int AbsentCount { get; set; }
        public int LateCount { get; set; }
        public int LeaveEarlyCount { get; set; }

        public int TotalIssues => AbsentCount + LateCount + LeaveEarlyCount;

        public override string ToString()
        {
            return $"총 {TotalDays}일 - 결석: {AbsentCount}, 지각: {LateCount}, 조퇴: {LeaveEarlyCount}";
        }
    }

    /// <summary>
    /// 학생 출결 기록
    /// </summary>
    public class StudentAttendanceRecord
    {
        public DateTime Date { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Date:yyyy-MM-dd} - {StudentName}: {Status}";
        }
    }

    /// <summary>
    /// 일지 작성 완성도 통계
    /// </summary>
    public class DiaryCompletionStats
    {
        public int TotalDiaries { get; set; }
        public int WithAttendance { get; set; }
        public int WithMemo { get; set; }
        public int WithNotice { get; set; }
        public int WithLife { get; set; }
        public int AttendancePercentage => TotalDiaries > 0 
            ? (WithAttendance * 100 / TotalDiaries) : 0;

        public int MemoPercentage => TotalDiaries > 0 
            ? (WithMemo * 100 / TotalDiaries) : 0;

        public int NoticePercentage => TotalDiaries > 0 
            ? (WithNotice * 100 / TotalDiaries) : 0;

        public int LifePercentage => TotalDiaries > 0 
            ? (WithLife * 100 / TotalDiaries) : 0;

        public override string ToString()
        {
            return $"총 {TotalDiaries}건 - 메모: {MemoPercentage}%, 알림장: {NoticePercentage}%, 생활기록: {LifePercentage}%";
        }
    }

    #endregion
}
