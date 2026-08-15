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
    public sealed class SchedulerService : IDisposable
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

        // 아래 위임 메서드들에는 try/catch 를 두지 않는다.
        // KEventRepository 가 이미 catch 에서 LogError(FileLogger) 후 rethrow 하므로,
        // 여기서 다시 잡아 Debug.WriteLine 하고 throw 하면 로그만 두 번 남고 동작은 같다.
        // (게다가 Debug.WriteLine 은 [Conditional("DEBUG")] 이라 릴리스에선 사라진다.)

        /// <summary>
        /// 작업 생성 (KEvent, ItemType="task" 자동 설정)
        /// </summary>
        public async Task<int> CreateTaskAsync(KEvent task)
        {
            task.ItemType = "task";
            return await KEventRepo.CreateAsync(task);
        }

        /// <summary>작업 수정</summary>
        public async Task<bool> UpdateTaskAsync(KEvent task)
            => await KEventRepo.UpdateAsync(task);

        /// <summary>작업 삭제</summary>
        public async Task<bool> DeleteTaskAsync(int no)
            => await SmartDeleteAsync(no);

        /// <summary>
        /// 동기화 여부에 따른 스마트 삭제:
        /// - GoogleId 가 있는(동기화된) 항목은 soft-delete(cancelled) → 다음 동기화에서 Google 에도 삭제 전파
        /// - 동기화 안 된 항목은 즉시 hard-delete
        /// </summary>
        private async Task<bool> SmartDeleteAsync(int no)
        {
            var ev = await KEventRepo.GetByIdAsync(no);
            return await SmartDeleteAsync(KEventRepo, ev?.No ?? no, ev?.GoogleId);
        }

        /// <summary>
        /// 스마트 삭제의 실제 규칙. 호출부가 이미 항목을 들고 있으면
        /// <paramref name="googleId"/> 를 넘겨 재조회를 피한다.
        /// </summary>
        private static async Task<bool> SmartDeleteAsync(
            Repositories.KEventRepository repo, int no, string? googleId)
        {
            if (!string.IsNullOrEmpty(googleId))
                return await repo.MarkCancelledAsync(no);
            return await repo.DeleteAsync(no);
        }

        /// <summary>영구 삭제(hard-delete). Google 삭제 전파 완료 후 cancelled 행 정리에 사용.</summary>
        public async Task<bool> PurgeEventAsync(int no) => await KEventRepo.DeleteAsync(no);

        /// <summary>
        /// 반복 시리즈 중 기준일 이후(포함) 항목을 모두 삭제 ("이후 반복 항목 모두 삭제").
        /// 항목별로 SmartDeleteAsync 규칙(동기화된 항목은 soft-delete)을 적용.
        /// </summary>
        public async Task<int> DeleteSeriesFromAsync(string seriesId, DateTime fromDate)
        {
            // 단일 연결·단일 트랜잭션으로 처리한다. 예전에는 항목마다 따로 커밋해서
            // 도중에 실패하면 시리즈가 반만 지워진 채 남았다(생성 경로는 이미
            // UnifiedItemDialog 에서 UnitOfWork 로 원자적으로 넣고 있었다).
            //
            // 또 항목마다 SmartDeleteAsync(no) 가 GoogleId 를 보려고 GetByIdAsync 를
            // 다시 던졌는데, 조회 결과에 이미 GoogleId 가 들어 있다 → 1+2N 쿼리였던 것을
            // 1+N 으로 줄인다(365개 시리즈면 731회 → 366회).
            using var uow = new UnitOfWork(_dbPath);
            int count = await uow.ExecuteInTransactionAsync(async () =>
            {
                var members = await uow.KEvents.GetBySeriesIdFromAsync(seriesId, fromDate);
                int n = 0;
                foreach (var ev in members)
                {
                    if (await SmartDeleteAsync(uow.KEvents, ev.No, ev.GoogleId))
                        n++;
                }
                return n;
            });

            Debug.WriteLine($"[SchedulerService] 시리즈 삭제 완료: SeriesId={seriesId}, {count}건");
            return count;
        }

        /// <summary>작업 조회 (ID)</summary>
        public async Task<KEvent?> GetTaskAsync(int no)
            => await KEventRepo.GetByIdAsync(no);

        /// <summary>날짜 범위로 작업 조회 (ItemType="task"만)</summary>
        public async Task<List<KEvent>> GetTasksByDateAsync(
            DateTime startDate,
            int days = 1,
            bool showCompleted = true)
            => await KEventRepo.GetTasksByDateRangeAsync(startDate, days, showCompleted);

        /// <summary>오늘 기준 미완료 할일 + 오늘 이후 모든 할일 조회 (ItemType="task"만)</summary>
        public async Task<List<KEvent>> GetPendingAndFutureTasksAsync()
            => await KEventRepo.GetPendingAndFutureTasksAsync(DateTime.Today);

        /// <summary>CalendarId 기준 미완료 + 미래 작업 조회 (ItemType="task"만)</summary>
        public async Task<List<KEvent>> GetTasksByCalendarIdAsync(int calendarId)
            => await KEventRepo.GetTasksByCalendarIdPendingAsync(calendarId, DateTime.Today);

        /// <summary>
        /// 모든 작업 조회 (ItemType="task"만)
        /// </summary>
        public async Task<List<KEvent>> GetAllTasksAsync(bool showCompleted = true)
            => await KEventRepo.GetAllTasksAsync(showCompleted);

        /// <summary>작업 개수 조회 (ItemType="task"만)</summary>
        public async Task<int> GetTaskCountAsync(bool onlyIncomplete = false)
            => await KEventRepo.GetTaskCountAsync(onlyIncomplete);

        #endregion

        #region KEvent Operations

        public async Task<int> CreateEventAsync(KEvent ev)
            => await KEventRepo.CreateAsync(ev);

        public async Task<bool> UpdateEventAsync(KEvent ev)
            => await KEventRepo.UpdateAsync(ev);

        /// <summary>
        /// 구글 업로드 직후 식별자만 되써 넣는다 — 배경 업로드 도중 사용자가 같은 항목을
        /// 수정했을 때 전체 행을 덮어써 그 편집을 지우는 일이 없도록 두 열만 갱신한다.
        /// </summary>
        public async Task<bool> UpdateGoogleSyncFieldsAsync(int no, string googleId, string updated)
            => await KEventRepo.UpdateGoogleSyncFieldsAsync(no, googleId, updated);

        public async Task<bool> DeleteEventAsync(int no)
            => await SmartDeleteAsync(no);

        public async Task<KEvent?> GetEventAsync(int no)
            => await KEventRepo.GetByIdAsync(no);

        /// <summary>날짜 범위로 이벤트 조회</summary>
        public async Task<List<KEvent>> GetEventsByDateAsync(DateTime startDate, int days = 1)
            => await KEventRepo.GetByDateRangeAsync(startDate, days);

        /// <summary>특정 캘린더 소속 + 특정 ItemType 전체 조회 (학사일정 재조정 동기화용)</summary>
        public async Task<List<KEvent>> GetEventsByCalendarAndTypeAsync(int calendarId, string itemType)
            => await KEventRepo.GetByCalendarIdAndTypeAsync(calendarId, itemType);

        #endregion

        #region KCalendarList Operations

        public async Task<List<KCalendarList>> GetAllCalendarsAsync()
            => await KCalendarListRepo.GetAllAsync();

        public async Task<int> CreateCalendarAsync(KCalendarList cal)
            => await KCalendarListRepo.CreateAsync(cal);

        public async Task<int> GetOrCreateCalendarIdAsync(string title, string color = "#4285F4")
            => await KCalendarListRepo.GetOrCreateAsync(title, color);

        /// <summary>학교별로 분리되는 캘린더 조회/생성 (학사일정 전용)</summary>
        public async Task<KCalendarList> GetOrCreateCalendarForSchoolAsync(string title, string schoolCode, string color)
            => await KCalendarListRepo.GetOrCreateForSchoolAsync(title, schoolCode, color);

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
