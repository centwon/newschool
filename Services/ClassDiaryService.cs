using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.ViewModels;

namespace NewSchool.Services
{
    /// <summary>
    /// ClassDiary Service - 학급 일지(출결·메모·알림장)와 그 날짜의 학생 로그 조회.
    ///
    /// <para>⚠ 2026-07-30 정리: 호출부가 0건이던 메서드 16개와 전용 결과 타입 3종
    /// (AttendanceStats·StudentAttendanceRecord·DiaryCompletionStats)을 제거했다.
    /// 출결 통계·생활기록 자동 생성·일지 검색/완성도 통계·월간 일괄 생성이 여기 있었지만
    /// 어느 화면도 쓰지 않았고 테스트도 없었다. 필요해지면 그때 화면과 함께 다시 만들 것.</para>
    public sealed class ClassDiaryService : IDisposable
    {
        private readonly ClassDiaryRepository _diaryRepo;
        private bool _disposed;

        public ClassDiaryService(string dbPath)
        {
            _diaryRepo = new ClassDiaryRepository(dbPath);
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

                // 실제로 반영됐는지 확인한다. 예전에는 결과를 버려서, 대상 행이 없어
                // 0행 갱신이 나도 "저장 완료"로 돌아갔다 — 호출부는 변경 표시를 지우므로
                // 사용자가 쓴 알림장·특기사항이 조용히 사라졌다.
                if (existing != null)
                {
                    // 수정
                    diary.No = existing.No;
                    if (!await _diaryRepo.UpdateAsync(diary))
                        throw new InvalidOperationException("저장 대상 일지를 찾지 못했습니다.");
                    return diary;
                }
                else
                {
                    // 생성
                    if (await _diaryRepo.CreateAsync(diary) <= 0)
                        throw new InvalidOperationException("일지를 생성하지 못했습니다.");
                    return diary;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"학급 일지 저장 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 학급 일지 조회. 없으면 <b>저장하지 않은</b> 빈 일지를 만들어 돌려준다.
        ///
        /// <para>화면이 "조회하고 null 이면 새로 만들기" 를 각자 짜지 않도록 여기에 둔다 —
        /// 실제로 ClassDiaryViewModel 이 같은 로직을 따로 들고 있었다.</para>
        ///
        /// <para>날짜는 일 단위로 자른다. 일지는 하루에 하나이므로 시각이 섞여 들어오면
        /// 조회한 날과 만들어 준 일지의 날짜가 어긋난다.</para>
        /// </summary>
        public async Task<ClassDiary> GetOrCreateDiaryAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, DateTime date, string teacherId)
        {
            var day = date.Date;
            var diary = await GetDiaryAsync(schoolCode, year, grade, classNum, day);

            // 빈 일지 생성 (DB에 저장하지 않음)
            return diary ?? new ClassDiary(schoolCode, year, semester, grade, classNum, day, teacherId);
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
        /// 기간별 일지 목록 조회
        /// </summary>
        public async Task<List<ClassDiary>> GetDateRangeDiariesAsync(
            string schoolCode, int year, int semester, 
            int grade, int classNum, DateTime startDate, DateTime endDate)
        {
            return await _diaryRepo.GetByDateRangeAsync(
                schoolCode, year, semester, grade, classNum, startDate, endDate);
        }

        // 미사용 메서드 제거 (2026-08-19): GetMonthDiariesAsync·GetClassDiariesAsync·UpdateMemoAsync —
        //   셋 다 호출처 0건. 월별 조회는 기간 조회(GetDateRangeDiariesAsync)가 상위 호환이고,
        //   메모만 고치는 경로는 화면이 일지를 통째로 저장하는 방식이라 쓸 데가 없었다.
        //   GetClassDiariesAsync 는 semester 를 받아 놓고 리포지토리에 넘기지 않아,
        //   불렸다면 학기로 걸렀다고 믿는 쪽에서 학년도 전체를 받았을 것이다.

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
            // 배치 조회 (기록 건마다 학적·기본정보를 재조회하던 N+1 제거)
            return await StudentLogViewModel.CreateManyAsync(logs);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _diaryRepo?.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }
}
