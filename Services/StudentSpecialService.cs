using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// StudentSpecial Service
/// 학교생활기록부 특기사항 비즈니스 로직
/// 마감(IsFinalized)된 기록은 수정/삭제가 거부됨
/// </summary>
public sealed class StudentSpecialService : IDisposable
{
    private readonly StudentSpecialRepository _repository;
    private bool _disposed;

    public StudentSpecialService() : this(SchoolDatabase.DbPath) { }

    /// <summary>DB 경로 주입 생성자 (테스트용 임시 DB 지원)</summary>
    public StudentSpecialService(string dbPath)
    {
        _repository = new StudentSpecialRepository(dbPath);
    }

    #region CRUD Operations

    /// <summary>
    /// 학생부 기록 생성
    /// </summary>
    public async Task<int> CreateAsync(StudentSpecial special)
    {
        ValidateSpecial(special);
        return await _repository.CreateAsync(special);
    }

    /// <summary>
    /// No로 학생부 기록 조회
    /// </summary>
    public async Task<StudentSpecial?> GetByIdAsync(int no)
    {
        return await _repository.GetByIdAsync(no);
    }

    /// <summary>
    /// 학생별 학생부 기록 조회
    /// </summary>
    public async Task<List<StudentSpecial>> GetByStudentAsync(string studentId, int year)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            throw new ArgumentException("학생 ID가 필요합니다.", nameof(studentId));

        return await _repository.GetByStudentAsync(studentId, year);
    }

    /// <summary>
    /// 여러 학생의 학생부 기록 일괄 조회 (N+1 해소)
    /// </summary>
    public async Task<Dictionary<string, List<StudentSpecial>>> GetByStudentIdsAsync(
        IEnumerable<string> studentIds, int year)
    {
        return await _repository.GetByStudentIdsAsync(studentIds, year);
    }

    // 미마감 조회(GetDraftByStudentAsync·GetDraftByTypeAsync)·영역별 조회(GetByTypeAsync
    // 두 오버로드)·교사별 조회(GetByTeacherAsync)·키워드 검색(SearchAsync)은 호출부가 없어
    // 지웠다(44차). 화면은 학생 단위(GetByStudentAsync·GetByStudentIdsAsync)와 수업 단위로만
    // 읽고, 영역·마감 상태는 받아 온 목록을 메모리에서 거른다.
    //
    // ⚠ 미마감 조회 셋은 애초에 학년도 조건이 없었다 — 되살린다면 옛 학년도가 섞여 나온다.

    /// <summary>
    /// 수업별 학생부 기록 조회
    /// </summary>
    public async Task<List<StudentSpecial>> GetByCourseAsync(int courseNo, int year)
    {
        return await _repository.GetByCourseAsync(courseNo, year);
    }


    /// <summary>
    /// 학생부 기록 수정 (마감된 기록은 거부)
    /// </summary>
    public async Task<bool> UpdateAsync(StudentSpecial special)
    {
        ValidateSpecial(special);
        await EnsureNotFinalizedAsync(special.No);
        return await _repository.UpdateAsync(special);
    }

    /// <summary>
    /// 여러 학생부 기록을 한 트랜잭션으로 저장 (신규는 생성, 기존은 수정).
    /// 하나라도 실패하면 전체 롤백.
    /// </summary>
    public async Task SaveManyAsync(IEnumerable<StudentSpecial> specials)
    {
        var list = specials.ToList();
        if (list.Count == 0) return;

        foreach (var special in list)
        {
            ValidateSpecial(special);
        }

        try
        {
            _repository.BeginTransaction();

            foreach (var special in list)
            {
                await EnsureNotFinalizedAsync(special.No);
                if (special.No > 0)
                {
                    // 반영 여부를 확인해야 "하나라도 실패하면 전체 롤백"이 실제로 성립한다.
                    // 예전에는 결과를 버려서 0행 갱신(이미 지워진 기록 등)도 그대로 커밋됐다.
                    if (!await _repository.UpdateAsync(special))
                        throw new InvalidOperationException(
                            $"기록 #{special.No}({special.Type})이 갱신되지 않았습니다. 이미 지워졌을 수 있습니다.");
                }
                else
                {
                    special.No = await _repository.CreateAsync(special);
                    if (special.No <= 0)
                        throw new InvalidOperationException($"{special.Type} 기록이 저장되지 않았습니다.");
                }
            }

            _repository.Commit();
        }
        catch
        {
            _repository.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 마감 상태 변경 (마감/마감해제)
    /// </summary>
    public async Task<bool> UpdateFinalizedStatusAsync(int no, bool isFinalized)
    {
        return await _repository.UpdateFinalizedStatusAsync(no, isFinalized);
    }

    /// <summary>
    /// 학생부 기록 삭제 (마감된 기록은 거부)
    /// </summary>
    public async Task<bool> DeleteAsync(int no)
    {
        await EnsureNotFinalizedAsync(no);
        return await _repository.DeleteAsync(no);
    }

    #endregion

    #region Finalization Guard

    /// <summary>
    /// 마감 여부 확인 — 마감된 기록이면 예외 발생
    /// </summary>
    private async Task EnsureNotFinalizedAsync(int no)
    {
        if (no <= 0) return; // 신규 레코드는 체크 불필요

        var existing = await _repository.GetByIdAsync(no);
        if (existing != null && existing.IsFinalized)
        {
            throw new InvalidOperationException(
                "마감된 학생부 기록은 수정할 수 없습니다. 수정하려면 먼저 마감을 해제해주세요.");
        }
    }

    #endregion

    // 영역별 통계(GetCountByTypeAsync)·미마감 통계(GetDraftCountByTypeAsync) 는
    // 호출부가 없어 지웠다(44차). 통계를 보여 주는 화면이 처음부터 없었다.

    #region Validation

    /// <summary>
    /// StudentSpecial 유효성 검사
    /// </summary>
    private void ValidateSpecial(StudentSpecial special)
    {
        if (special == null)
            throw new ArgumentNullException(nameof(special));

        if (string.IsNullOrWhiteSpace(special.StudentID))
            throw new ArgumentException("학생 ID가 필요합니다.");

        if (string.IsNullOrWhiteSpace(special.Type))
            throw new ArgumentException("학생부 영역이 필요합니다.");

        if (special.Year < 2000 || special.Year > 2100)
            throw new ArgumentException("올바른 학년도를 입력하세요.");
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed)
            return;

        _repository?.Dispose();
        _disposed = true;
    }

    #endregion
}
