using System;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// 학생 한 명을 새로 만드는 단일 경로.
///
/// <para>학생 추가 페이지(엑셀·수동 일괄)와 학생 편집 다이얼로그(한 명 추가)가 같은 일을
/// 하므로 여기 모아 둔다. 특히 <see cref="CreateAsync"/> 의 <b>연결 공유</b>는 두 번 쓰면
/// 반드시 한 쪽이 틀리는 종류의 코드다 — 학적 리포지토리가 학생 리포지토리와 다른 연결을
/// 열면 트랜잭션이 공유되지 않아(BaseRepository 가 "이 연결에서 시작된 트랜잭션인가"를
/// 확인하고 아니면 무시한다) 학생만 저장되고 학적은 밖에서 도는 상태가 된다.</para>
/// </summary>
public static class StudentCreationService
{
    // 학기 컬럼은 없앴다(2026-08-28). 학적은 학년 단위로 다루고
    // UNIQUE(StudentID, SchoolCode, Year) 가 "한 학년도에 학적 한 건" 을 강제한다.
    //
    // ⚠ 학기를 되살리지 말 것. 예전에는 등록 시점의 학기를 넣었고, 학기로 거르던 화면들에서
    // 1학기에 등록한 학생이 2학기에 통째로 사라졌다. 학기가 필요한 것은 명부가 아니라
    // 기록 쪽이며(StudentLog.Semester 등) 거기에는 이미 있다.

    private static readonly Random _random = new();

    /// <summary>
    /// 이미 쓰이고 있는 StudentID 인지 확인한다.
    ///
    /// <para>IsDeleted 를 봐선 안 된다. 학생 삭제는 논리 삭제(행 유지)이고 StudentID 에
    /// UNIQUE 제약이 있어, 삭제된 학생의 ID 를 "사용 가능" 으로 판정하면 저장 단계에서
    /// UNIQUE 위반으로 실패한다.</para>
    /// </summary>
    public static async Task<bool> IsIdTakenAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return true;

        try
        {
            using var studentRepo = new StudentRepository(SchoolDatabase.DbPath);
            return await studentRepo.GetByIdAsync(id) != null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentCreation] ID 확인 오류: {ex.Message}");
            // 확인에 실패하면 안전한 쪽 — 쓰이는 중으로 본다.
            return true;
        }
    }

    /// <summary>
    /// 고유 학생 ID 생성. 형식은 학교코드(7) + 입학년도(4) + 일련번호(4) = 15자리.
    /// </summary>
    /// <param name="alsoTaken">
    /// DB 말고도 피해야 할 ID 판정기. 아직 저장 전인 목록을 들고 있는 화면이 넘긴다.
    /// </param>
    /// <returns>실패하면 빈 문자열.</returns>
    public static async Task<string> GenerateUniqueStudentIdAsync(int year, Func<string, bool>? alsoTaken = null)
    {
        string schoolCode = Settings.SchoolCode?.Value ?? "0000000";

        for (int attempt = 0; attempt < 100; attempt++)
        {
            int sequence = _random.Next(1, 10000);
            string studentId = Student.GenerateStudentID(schoolCode, year, sequence);

            if (alsoTaken?.Invoke(studentId) == true) continue;
            if (await IsIdTakenAsync(studentId)) continue;

            return studentId;
        }

        System.Diagnostics.Debug.WriteLine("[StudentCreation] 고유 ID 생성 실패: 100번 시도");
        return string.Empty;
    }

    /// <summary>
    /// 같은 학년도-학년-반에 같은 번호가 이미 있는지.
    ///
    /// <para>학기는 보지 않는다 — 명부는 학년 단위라 학기와 무관하게 중복이다.
    /// (학년,반,번호) UNIQUE 제약이 없어 이 검사가 유일한 방어선이므로,
    /// 확인에 실패하면 중복으로 간주해 잘못된 삽입을 막는다.</para>
    /// </summary>
    public static async Task<bool> IsSeatTakenAsync(int year, int grade, int cls, int number)
    {
        try
        {
            using var enrollmentRepo = new EnrollmentRepository(SchoolDatabase.DbPath);
            var classStudents = await enrollmentRepo.GetByClassAsync(
                Settings.SchoolCode.Value, year, grade, cls);

            // 전출·졸업한 학적까지 센다(GetByClassAsync 는 재적으로 거르지 않는다).
            // 그 학생이 명단에 안 보여도 행은 남아 있으므로, 같은 번호를 새로 넣으면
            // 한 반에 같은 번호가 두 줄이 된다.
            return classStudents.Any(e => e.Number == number);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentCreation] 중복 확인 오류: {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// 학생 한 명 저장 (Student + Enrollment 동시 저장). 트랜잭션으로 원자성을 보장한다.
    /// </summary>
    /// <returns>성공하면 null, 실패하면 사용자에게 보여줄 실패 사유.</returns>
    public static async Task<string?> CreateAsync(
        string studentId, string name, string sex,
        int year, int grade, int cls, int number)
    {
        try
        {
            using var studentRepo = new StudentRepository(SchoolDatabase.DbPath);

            // 학적 리포지토리는 학생 리포지토리의 연결을 공유한다(클래스 주석 참고).
            using var enrollmentRepo = new EnrollmentRepository(studentRepo.GetConnection());

            studentRepo.BeginTransaction();
            enrollmentRepo.SetTransaction(studentRepo.GetTransaction());

            try
            {
                var student = new Student
                {
                    StudentID = studentId,
                    Name = name,
                    Sex = sex,
                    Phone = string.Empty,
                    Email = string.Empty,
                    Address = string.Empty,
                    Memo = string.Empty,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    IsDeleted = false
                };

                await studentRepo.CreateAsync(student);

                // 이름·성별은 Enrollment 에 넣지 않는다 — Student 가 정본이고 조회는
                // EnrollmentFull 뷰가 JOIN 으로 읽는다.
                var enrollment = new Enrollment
                {
                    StudentID = studentId,
                    SchoolCode = Settings.SchoolCode.Value,
                    Year = year,
                    Grade = grade,
                    Class = cls,
                    Number = number,
                    // 담임이 비어 있으면 Teacher FK 위반이 되므로 리포지토리가 NULL 로 바꿔 넣는다.
                    TeacherID = Settings.User.Value ?? string.Empty,
                    Memo = string.Empty,
                };

                // 학년이 기본 변동을 정한다 — 1학년은 입학, 그 위는 진급.
                // ApplyChange 가 IsActive 까지 함께 맞춘다.
                enrollment.ApplyChange(EnrollmentChange.DefaultFor(grade), DateTime.Today);

                if (await enrollmentRepo.CreateAsync(enrollment) <= 0)
                    throw new InvalidOperationException("학적이 저장되지 않았습니다.");

                // 둘 다 성공해야 저장된다.
                studentRepo.Commit();

                System.Diagnostics.Debug.WriteLine($"[StudentCreation] 저장 성공: {name} ({studentId})");
                return null;
            }
            catch (Exception ex)
            {
                studentRepo.Rollback();
                System.Diagnostics.Debug.WriteLine($"[StudentCreation] 저장 실패 (롤백): {name} - {ex}");
                return ex.Message;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentCreation] 연결 오류: {name} - {ex}");
            return ex.Message;
        }
    }
}
