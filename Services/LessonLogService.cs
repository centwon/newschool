using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// LessonLog 비즈니스 로직 서비스
/// </summary>
public class LessonLogService : IDisposable
{
    private readonly LessonLogRepository _repository;
    private bool _disposed;

    public LessonLogService()
    {
        _repository = new LessonLogRepository(SchoolDatabase.DbPath);
    }

    public LessonLogService(string dbPath)
    {
        _repository = new LessonLogRepository(dbPath);
    }

    #region CRUD Operations

    /// <summary>
    /// 수업 기록 추가
    /// </summary>
    public async Task<int> InsertAsync(LessonLog log)
    {
        ValidateLogOrThrow(log);
        return await _repository.InsertAsync(log);
    }

    /// <summary>
    /// 수업 기록 수정
    /// </summary>
    public async Task<int> UpdateAsync(LessonLog log)
    {
        ValidateLogOrThrow(log);
        return await _repository.UpdateAsync(log);
    }

    /// <summary>
    /// 수업 기록 삭제
    /// </summary>
    public async Task<int> DeleteAsync(int no)
    {
        return await _repository.DeleteAsync(no);
    }

    /// <summary>
    /// 수업 기록 조회 (ID)
    /// </summary>
    public async Task<LessonLog?> GetByIdAsync(int no)
    {
        return await _repository.GetByIdAsync(no);
    }

    #endregion

    #region Query Operations

    /// <summary>
    /// 현재 사용자의 수업 기록 조회
    /// </summary>
    public async Task<List<LessonLog>> GetMyLessonsAsync(int? semester = null)
    {
        return await _repository.GetByTeacherAsync(
            Settings.User.Value,
            Settings.WorkYear.Value,
            semester ?? Settings.WorkSemester.Value);
    }

    /// <summary>
    /// 과목별 수업 기록 조회
    /// </summary>
    public async Task<List<LessonLog>> GetBySubjectAsync(string subject)
    {
        return await _repository.GetBySubjectAsync(
            Settings.User.Value,
            Settings.WorkYear.Value,
            Settings.WorkSemester.Value,
            subject);
    }

    /// <summary>
    /// 과목 + 강의실별 수업 기록 조회
    /// </summary>
    public async Task<List<LessonLog>> GetBySubjectAndRoomAsync(string? subject = null, string? room = null, int limit = 30)
    {
        return await _repository.GetBySubjectAndRoomAsync(
            Settings.User.Value,
            Settings.WorkYear.Value,
            Settings.WorkSemester.Value,
            subject,
            room,
            limit);
    }

    /// <summary>
    /// 과목 + 학급별 수업 기록 조회
    /// </summary>
    public async Task<List<LessonLog>> GetBySubjectAndClassAsync(string? subject = null, int? grade = null, int? classNum = null, int limit = 30)
    {
        return await _repository.GetBySubjectAndClassAsync(
            Settings.User.Value,
            Settings.WorkYear.Value,
            Settings.WorkSemester.Value,
            subject,
            grade,
            classNum,
            limit);
    }

    /// <summary>
    /// 오늘 수업 기록 조회
    /// </summary>
    public async Task<List<LessonLog>> GetTodayLessonsAsync()
    {
        return await _repository.GetByDateAsync(Settings.User.Value, DateTime.Today);
    }

    /// <summary>
    /// 날짜별 수업 기록 조회
    /// </summary>
    public async Task<List<LessonLog>> GetByDateAsync(DateTime date)
    {
        return await _repository.GetByDateAsync(Settings.User.Value, date);
    }

    /// <summary>
    /// 강의실 목록 조회
    /// </summary>
    public async Task<List<string>> GetRoomsAsync(string subject)
    {
        return await _repository.GetRoomsAsync(
            Settings.User.Value,
            Settings.WorkYear.Value,
            Settings.WorkSemester.Value,
            subject);
    }

    /// <summary>
    /// 수업 횟수 조회
    /// </summary>
    public async Task<int> GetLessonCountAsync(string? subject = null, string? room = null)
    {
        return await _repository.GetLessonCountAsync(
            Settings.User.Value,
            Settings.WorkYear.Value,
            Settings.WorkSemester.Value,
            subject,
            room);
    }

    #endregion

    #region Business Logic

    /// <summary>
    /// 새 수업 기록 생성 (기본값 설정)
    /// </summary>
    public LessonLog CreateNew(string? subject = null, string? room = null)
    {
        return new LessonLog
        {
            TeacherID = Settings.User.Value,
            Year = Settings.WorkYear.Value,
            Semester = Settings.WorkSemester.Value,
            Subject = subject ?? string.Empty,
            Room = room ?? string.Empty,
            Date = DateTime.Now,
            Period = LessonLogService.GetCurrentPeriod()
        };
    }

    /// <summary>
    /// 현재 교시 추정 (static, int 반환)
    /// </summary>
    public static int GetCurrentPeriod()
    {
        var now = DateTime.Now.TimeOfDay;

        // 일반적인 학교 시간표 기준
        var periods = new (TimeSpan Start, TimeSpan End, int Number)[]
        {
            (new TimeSpan(8, 50, 0), new TimeSpan(9, 40, 0), 1),
            (new TimeSpan(9, 50, 0), new TimeSpan(10, 40, 0), 2),
            (new TimeSpan(10, 50, 0), new TimeSpan(11, 40, 0), 3),
            (new TimeSpan(11, 50, 0), new TimeSpan(12, 40, 0), 4),
            (new TimeSpan(13, 30, 0), new TimeSpan(14, 20, 0), 5),
            (new TimeSpan(14, 30, 0), new TimeSpan(15, 20, 0), 6),
            (new TimeSpan(15, 30, 0), new TimeSpan(16, 20, 0), 7),
        };

        foreach (var (start, end, number) in periods)
        {
            if (now >= start && now <= end)
            {
                return number;
            }
        }

        return 0;
    }

    /// <summary>
    /// 교시 번호를 문자열로 변환
    /// </summary>
    public static string PeriodToString(int period)
    {
        return period > 0 ? $"{period}교시" : string.Empty;
    }

    /// <summary>
    /// 유효성 검사 (public, 튜플 반환)
    /// </summary>
    public (bool IsValid, string ErrorMessage) ValidateLog(LessonLog log)
    {
        if (string.IsNullOrWhiteSpace(log.TeacherID))
            return (false, "교사 ID가 필요합니다.");

        if (log.Year <= 0)
            return (false, "학년도가 필요합니다.");

        if (log.Semester <= 0 || log.Semester > 2)
            return (false, "학기는 1 또는 2여야 합니다.");

        if (string.IsNullOrWhiteSpace(log.Subject))
            return (false, "과목명이 필요합니다.");

        if (log.Period < 1 || log.Period > 7)
            return (false, "교시를 선택해주세요.");

        return (true, string.Empty);
    }

    /// <summary>
    /// 유효성 검사 (private, 예외 발생)
    /// </summary>
    private void ValidateLogOrThrow(LessonLog log)
    {
        var (isValid, errorMessage) = ValidateLog(log);
        if (!isValid)
            throw new ArgumentException(errorMessage);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _repository?.Dispose();
            }
            _disposed = true;
        }
    }

    #endregion
}
