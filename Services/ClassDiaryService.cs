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
