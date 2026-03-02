using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using NewSchool.Scheduler.Repositories;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.Scheduler
{
    /// <summary>
    /// Scheduler Service - 비즈니스 로직 레이어
    /// ✅ Ktask → KEvent 통합 완료: 모든 task는 KEvent(ItemType="task")으로 관리
    /// </summary>
    public class SchedulerService : IDisposable
    {
        private readonly string _dbPath;
        private KEventRepository? _keventRepo;
        private KCalendarListRepository? _kcalendarListRepo;
        private bool _disposed;

        public SchedulerService(string dbPath)
        {
            _dbPath = dbPath;
        }

        private KEventRepository KEventRepo => _keventRepo ??= new KEventRepository(_dbPath);
        private KCalendarListRepository KCalendarListRepo => _kcalendarListRepo ??= new KCalendarListRepository(_dbPath);

        #region Task Operations (KEvent ItemType="task")

        /// <summary>
        /// 작업 생성 (KEvent, ItemType="task" 자동 설정)
        /// </summary>
        public async Task<int> CreateTaskAsync(KEvent task)
        {
            try
            {
                task.ItemType = "task";
                int no = await KEventRepo.CreateAsync(task);
                Debug.WriteLine($"[SchedulerService] 작업 생성 완료: {no}");
                return no;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulerService] 작업 생성 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 작업 수정
        /// </summary>
        public async Task<bool> UpdateTaskAsync(KEvent task)
        {
            try
            {
                bool success = await KEventRepo.UpdateAsync(task);
                Debug.WriteLine($"[SchedulerService] 작업 수정 완료: {task.No}");
                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulerService] 작업 수정 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 작업 삭제
        /// </summary>
        public async Task<bool> DeleteTaskAsync(int no)
        {
            try
            {
                bool success = await KEventRepo.DeleteAsync(no);
                Debug.WriteLine($"[SchedulerService] 작업 삭제 완료: {no}");
                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulerService] 작업 삭제 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 작업 조회 (ID)
        /// </summary>
        public async Task<KEvent?> GetTaskAsync(int no)
        {
            try
            {
                return await KEventRepo.GetByIdAsync(no);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulerService] 작업 조회 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 날짜 범위로 작업 조회 (ItemType="task"만)
        /// </summary>
        public async Task<List<KEvent>> GetTasksByDateAsync(
            DateTime startDate,
            int days = 1,
            bool showCompleted = true)
        {
            try
            {
                return await KEventRepo.GetTasksByDateRangeAsync(startDate, days, showCompleted);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulerService] 작업 조회 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 오늘 기준 미완료 할일 + 오늘 이후 모든 할일 조회 (ItemType="task"만)
        /// </summary>
        public async Task<List<KEvent>> GetPendingAndFutureTasksAsync()
        {
            try
            {
                return await KEventRepo.GetPendingAndFutureTasksAsync(DateTime.Today);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulerService] 작업 조회 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// CalendarId 기준 미완료 + 미래 작업 조회 (ItemType="task"만)
        /// </summary>
        public async Task<List<KEvent>> GetTasksByCalendarIdAsync(int calendarId)
        {
            try
            {
                return await KEventRepo.GetTasksByCalendarIdPendingAsync(calendarId, DateTime.Today);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulerService] CalendarId={calendarId} 작업 조회 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 모든 작업 조회 (ItemType="task"만)
        /// </summary>
        public async Task<List<KEvent>> GetAllTasksAsync(bool showCompleted = true)
        {
            try
            {
                return await KEventRepo.GetAllTasksAsync(showCompleted);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulerService] 전체 작업 조회 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 작업 개수 조회 (ItemType="task"만)
        /// </summary>
        public async Task<int> GetTaskCountAsync(bool onlyIncomplete = false)
        {
            try
            {
                return await KEventRepo.GetTaskCountAsync(onlyIncomplete);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulerService] 작업 개수 조회 실패: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region KEvent Operations

        public async Task<int> CreateEventAsync(KEvent ev)
        {
            try
            {
                int no = await KEventRepo.CreateAsync(ev);
                Debug.WriteLine($"[SchedulerService] 이벤트 생성 완료: {no}");
                return no;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulerService] 이벤트 생성 실패: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> UpdateEventAsync(KEvent ev)
        {
            try
            {
                bool ok = await KEventRepo.UpdateAsync(ev);
                Debug.WriteLine($"[SchedulerService] 이벤트 수정 완료: {ev.No}");
                return ok;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulerService] 이벤트 수정 실패: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> DeleteEventAsync(int no)
        {
            try
            {
                bool ok = await KEventRepo.DeleteAsync(no);
                Debug.WriteLine($"[SchedulerService] 이벤트 삭제 완료: {no}");
                return ok;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulerService] 이벤트 삭제 실패: {ex.Message}");
                throw;
            }
        }

        public async Task<KEvent?> GetEventAsync(int no)
        {
            try { return await KEventRepo.GetByIdAsync(no); }
            catch (Exception ex) { Debug.WriteLine($"[SchedulerService] 이벤트 조회 실패: {ex.Message}"); throw; }
        }

        /// <summary>날짜 범위로 이벤트 조회</summary>
        public async Task<List<KEvent>> GetEventsByDateAsync(DateTime startDate, int days = 1)
        {
            try { return await KEventRepo.GetByDateRangeAsync(startDate, days); }
            catch (Exception ex) { Debug.WriteLine($"[SchedulerService] 이벤트 범위 조회 실패: {ex.Message}"); throw; }
        }

        #endregion

        #region KCalendarList Operations

        public async Task<List<KCalendarList>> GetAllCalendarsAsync()
            => await KCalendarListRepo.GetAllAsync();

        public async Task<int> CreateCalendarAsync(KCalendarList cal)
            => await KCalendarListRepo.CreateAsync(cal);

        public async Task<int> GetOrCreateCalendarIdAsync(string title, string color = "#4285F4")
            => await KCalendarListRepo.GetOrCreateAsync(title, color);

        public async Task<bool> UpdateCalendarAsync(KCalendarList cal)
            => await KCalendarListRepo.UpdateAsync(cal);

        public async Task<bool> DeleteCalendarAsync(int no)
            => await KCalendarListRepo.DeleteAsync(no);

        public async Task<KCalendarList?> GetCalendarByGoogleIdAsync(string googleId)
            => await KCalendarListRepo.GetByGoogleIdAsync(googleId);

        public async Task<List<KCalendarList>> GetSyncableCalendarsAsync()
            => await KCalendarListRepo.GetSyncableAsync();

        #endregion

        #region Google Sync Operations

        public async Task<KEvent?> GetEventByGoogleIdAsync(string googleId)
            => await KEventRepo.GetByGoogleIdAsync(googleId);

        public async Task<List<KEvent>> GetUnsyncedEventsAsync(int calendarId)
            => await KEventRepo.GetUnsyncedAsync(calendarId);

        public async Task<List<KEvent>> GetModifiedEventsSinceAsync(int calendarId, string sinceUtc)
            => await KEventRepo.GetModifiedSinceAsync(calendarId, sinceUtc);

        public async Task<List<KEvent>> GetDeletedEventsWithGoogleIdAsync(int calendarId)
            => await KEventRepo.GetDeletedWithGoogleIdAsync(calendarId);

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _keventRepo?.Dispose();
                _kcalendarListRepo?.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }
}
