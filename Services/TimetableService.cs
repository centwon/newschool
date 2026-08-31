using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.ViewModels;

namespace NewSchool.Services;

/// <summary>
/// <b>학급</b> 시간표 — "이 반은 언제 무슨 수업" 을 답한다. <c>ClassTimetable</c> 을 읽는다.
///
/// <para>교사 시간표는 <see cref="TeacherTimetableService"/> 가 맡는다. 둘은 중복이 아니라
/// <b>관점</b>으로 갈리고, 보는 표부터 다르다(이쪽 <c>ClassTimetable</c>, 저쪽 <c>Lesson</c>).</para>
///
/// <para>여기에 있던 교사 시간표(<c>GetTeacherTimetableAsync</c>)는 호출부가 없어
/// 지웠다(39차) — 그 일은 저쪽이 한다. 되살리지 말 것.</para>
/// </summary>
public sealed class TimetableService : IDisposable
{
    private readonly string _dbPath;
    private bool _disposed;

    public TimetableService(string dbPath)
    {
        _dbPath = dbPath;
    }

    // 교사 시간표(GetTeacherTimetableAsync)는 호출부가 없어 지웠다(39차).
    // 이 서비스를 쓰는 곳은 학급일지의 학급 시간표 하나뿐이다.

    #region 학급 시간표

    /// <summary>
    /// 학급별 시간표 조회 (ClassTimetable 사용)
    /// </summary>
    public async Task<TimetableViewModel> GetClassTimetableAsync(
        string schoolCode, int year, int semester, int grade, int classNo)
    {
        var viewModel = new TimetableViewModel
        {
            Year = year,
            Semester = semester,
            Title = $"{grade}학년 {classNo}반 시간표 ({year}학년도 {semester}학기)"
        };

        try
        {
            // 빈 시간표 초기화 (5일 x 7교시)
            viewModel.InitializeEmptyTimetable();

            // ClassTimetable 직접 조회
            using var timetableRepo = new ClassTimetableRepository(_dbPath);
            var timetables = await timetableRepo.GetByClassAsync(
                schoolCode, year, semester, grade, classNo);

            if (timetables.Count == 0)
            {
                Debug.WriteLine($"[TimetableService] {grade}학년 {classNo}반의 시간표가 없습니다.");
                return viewModel;
            }

            // ClassTimetable → TimetableItem 변환
            foreach (var timetable in timetables)
            {
                // 유효성 검사 — 교시 상한은 PeriodCounts.MaxSupported 하나가 정한다.
                // 여기에 숫자를 박아 두면 그 위 교시가 격자에는 있는데 이 조회에서만 빠진다.
                if (timetable.DayOfWeek < 1 || timetable.DayOfWeek > 5 ||
                    timetable.Period < 1 || timetable.Period > PeriodCounts.MaxSupported)
                {
                    Debug.WriteLine($"[TimetableService] 잘못된 시간표: Grade={grade}, Class={classNo}, Day={timetable.DayOfWeek}, Period={timetable.Period}");
                    continue;
                }

                // 시간표 아이템 가져오기
                var item = viewModel.GetItem(timetable.DayOfWeek, timetable.Period);
                if (item != null)
                {
                    item.SubjectName = timetable.SubjectName;
                    item.TeacherName = timetable.TeacherName;
                    item.IsEmpty = false;
                }
            }

            return viewModel;
        }
        catch (Exception ex)
        {
            // 빈 시간표로 돌려주면 "수업이 없음"과 구분되지 않아 호출부가 오류를 알 수 없다
            Debug.WriteLine($"[TimetableService] 학급 시간표 조회 실패: {ex}");
            viewModel.LoadFailed = true;
            viewModel.ErrorMessage = ex.Message;
            return viewModel;
        }
    }

    #endregion

    // Course 생성/수정/삭제와 학급 시간표 일괄 등록을 여기서 제공했지만
    // 호출부가 한 곳도 없었다(전수 조사 34차). 같은 일을 화면들이 직접 하고 있어
    // 실제로는 이 안전한(트랜잭션) 구현이 죽은 채였다 — 화면 쪽을 트랜잭션화하고 지운다.
    // 필요해지면 커밋 decf101 이전 이력에서 되살릴 수 있다.

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }

    #endregion
}
