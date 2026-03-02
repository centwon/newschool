using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.Data.Sqlite;
using NewSchool.Models;
using NewSchool.Helpers;
using NewSchool.Repositories;
using NewSchool.Controls;

namespace NewSchool.Pages;

/// <summary>
/// 학생 추가 페이지 (WinUI3)
/// Excel 파일 또는 수동 입력으로 학생 추가
/// 
/// 주요 기능:
/// 1. Excel 파일에서 대량 학생 추가
/// 2. 수동으로 학생 한 명씩 추가
/// 3. 중복 검사 (DB + 현재 목록)
/// 4. 트랜잭션을 통한 안전한 저장 (Student + Enrollment 동시 저장)
/// 
/// TODO 구현 완료:
/// ✓ DB 저장: Student 테이블 + Enrollment 테이블에 트랜잭션으로 저장
/// ✓ 중복 확인: Enrollment 테이블에서 Year, Grade, Class, Number 조합 확인
/// ✓ ID 확인: Student 테이블에서 StudentID 존재 여부 확인
/// 
/// 데이터 흐름:
/// 1. 사용자 입력/Excel → NewStudents 목록에 추가
/// 2. 중복 검사 (IsDuplicateAsync) - Enrollment 테이블 조회
/// 3. 고유 ID 생성 (GenerateUniqueStudentIDAsync) - Student 테이블 조회
/// 4. DB 저장 (SaveStudentAsync):
///    - Student 테이블에 INSERT
///    - Enrollment 테이블에 INSERT
///    - 트랜잭션 커밋 (둘 다 성공해야 저장)
/// </summary>
public sealed partial class AddStudentsPage : Page
{
    // 추가할 학생 목록
    public ObservableCollection<StudentAddViewModel> NewStudents { get; } = new();

    private static readonly Random _random = new();

    public AddStudentsPage()
    {
        InitializeComponent();

        // 기본값 설정
        TxtYear.Text = DateTime.Today.Year.ToString();
        TxtGrade.Text = "1";
        TxtClass.Text = "1";
    }

    #region Excel에서 학생 추가

    /// <summary>
    /// Excel 파일에서 학생 정보 가져오기
    /// </summary>
    private async void BtnAddFromExcel_Click(object sender, RoutedEventArgs e)
    {
        // 학년도 유효성 검사
        if (!int.TryParse(TxtYear.Text, out int year) || year < 1900 || year > 2100)
        {
            await MessageBox.ShowAsync("학년도를 올바르게 입력하세요 (1900-2100).", "오류");
            return;
        }

        // 확인 메시지
        var confirmed = await MessageBox.ShowConfirmAsync(
            "Excel 파일에서 학생을 추가합니다.\n\n" +
            "필수 열: '번호', '이름' 또는 '성명'\n" +
            "선택 열: '학년', '반' 또는 '학급'\n\n" +
            "계속하시겠습니까?",
            "Excel 파일에서 학생 추가", "확인", "취소");
        if (!confirmed)
            return;

        // 파일 선택
        var file = await PickExcelFileAsync();
        if (file == null) return;

        try
        {
            // 로딩 표시
            LoadingProgressRing.IsActive = true;
            BtnAddFromExcel.IsEnabled = false;

            // Excel 파일 처리
            await ProcessExcelFileAsync(file, year);

            await MessageBox.ShowAsync($"총 {NewStudents.Count}명의 학생을 목록에 추가했습니다.", "알림");
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"Excel 파일 처리 중 오류 발생:\n{ex.Message}", "오류");
        }
        finally
        {
            LoadingProgressRing.IsActive = false;
            BtnAddFromExcel.IsEnabled = true;
            BtnSave.IsEnabled = NewStudents.Count > 0 ? true : false;
            BtnExport.IsEnabled = NewStudents.Count > 0 ? true : false;
            TbBlankList.Visibility = NewStudents.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>
    /// Excel 파일 선택
    /// </summary>
    private async Task<StorageFile?> PickExcelFileAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add(".xlsx");
        picker.FileTypeFilter.Add(".xls");

        // WinUI3에서 필요한 초기화
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        return await picker.PickSingleFileAsync();
    }

    /// <summary>
    /// Excel 파일 처리 (MiniExcel 사용)
    /// </summary>
    private async Task ProcessExcelFileAsync(StorageFile file, int year)
    {
        // Task.Run 제거 - UI 스레드에서 실행
        var sheetsData = ExcelHelper.DataToText(file.Path);

        foreach (var sheetData in sheetsData)
        {
            await ProcessWorksheetData(sheetData, year);
        }
    }

    /// <summary>
    /// 워크시트 데이터 처리 (string[,] 배열 사용)
    /// string[,] 배열은 1-based 인덱스 사용 (Excel과 동일)
    /// </summary>
    private async Task ProcessWorksheetData(string[,] sheetData, int year)
    {
        int rowCount = sheetData.GetLength(0);
        int colCount = sheetData.GetLength(1);

        if (rowCount < 2 || colCount < 2)
            return;

        // 열 인덱스 찾기 (1-based)
        int gradeCol = -1, classCol = -1, numberCol = -1, nameCol = -1;
        int titleRow = -1;

        // 제목 행 찾기 (처음 10행 이내)
        for (int row = 1; row <= Math.Min(10, rowCount - 1); row++)
        {
            for (int col = 1; col <= colCount - 1; col++)
            {
                var cellValue = (sheetData[row, col] ?? string.Empty).Replace(" ", string.Empty);

                if (cellValue.Equals("학년", StringComparison.OrdinalIgnoreCase))
                    gradeCol = col;
                else if (cellValue.Equals("반", StringComparison.OrdinalIgnoreCase) ||
                         cellValue.Equals("학급", StringComparison.OrdinalIgnoreCase))
                    classCol = col;
                else if (cellValue.Equals("번호", StringComparison.OrdinalIgnoreCase))
                    numberCol = col;
                else if (cellValue.Equals("이름", StringComparison.OrdinalIgnoreCase) ||
                         cellValue.Equals("성명", StringComparison.OrdinalIgnoreCase))
                {
                    nameCol = col;
                    titleRow = row;
                }
            }

            if (titleRow > 0) break;
        }

        // 필수 열 확인
        if (titleRow == -1 || numberCol == -1 || nameCol == -1)
        {
            await MessageBox.ShowAsync("필수 열('번호', '이름' 또는 '성명')을 찾을 수 없습니다.", "오류");
            return;
        }

        // 기본값 설정
        int defaultGrade = 0, defaultClass = 0;

        if (gradeCol == -1)
        {
            defaultGrade = await GetGradeInputAsync("학년 정보가 없습니다. 이 시트의 모든 학생에게 적용할 학년을 입력하세요.");
            if (defaultGrade == 0) return;
        }

        if (classCol == -1)
        {
            defaultClass = await GetClassInputAsync("학급 정보가 없습니다. 이 시트의 모든 학생에게 적용할 반을 입력하세요.");
            if (defaultClass == 0) return;
        }

        // 데이터 행 처리 (1-based 인덱스)
        for (int row = titleRow + 1; row < rowCount; row++)
        {
            // 번호 (1-based)
            if (!int.TryParse(sheetData[row, numberCol], out int number) || number < 1)
                continue;

            // 이름 (1-based)
            string name = (sheetData[row, nameCol] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // 학년
            int grade = defaultGrade;
            if (gradeCol != -1)
            {
                if (int.TryParse(sheetData[row, gradeCol], out int g) && g >= 1 && g <= 3)
                    grade = g;
                else if (defaultGrade == 0)
                    grade = await GetGradeInputAsync($"학생 '{name}'의 학년 정보를 입력하세요.");
            }
            if (grade == 0) continue;

            // 학급
            int cls = defaultClass;
            if (classCol != -1)
            {
                if (int.TryParse(sheetData[row, classCol], out int c) && c >= 1)
                    cls = c;
                else if (defaultClass == 0)
                    cls = await GetClassInputAsync($"학생 '{name}'의 학급 정보를 입력하세요.");
            }
            if (cls == 0) continue;

            // 중복 검사
            if (await IsDuplicateAsync(year, grade, cls, number))
            {
                if (!await MessageBox.ShowConfirmAsync(
                    $"{year}학년도 {grade}학년 {cls}반 {number}번은 이미 존재합니다.\n계속하시겠습니까?",
                    "중복 학생", "계속", "중단"))
                    return;

                continue;
            }

            // 학생 추가
            string studentId = await GenerateUniqueStudentIDAsync(year);
            if (string.IsNullOrEmpty(studentId))
            {
                await MessageBox.ShowAsync("고유 ID 생성 실패", "오류");
                return;
            }

            NewStudents.Add(new StudentAddViewModel
            {
                StudentID = studentId,
                Year = year,
                Grade = grade,
                Class = cls,
                Number = number,
                Name = name
            });
        }
    }

    #endregion

    #region 수동 학생 추가

    /// <summary>
    /// 학생 한 명 추가
    /// </summary>
    private async void BtnAddStudent_Click(object sender, RoutedEventArgs e)
    {
        // 유효성 검사
        if (!int.TryParse(TxtYear.Text, out int year) || year < 1900 || year > 2100)
        {
            await MessageBox.ShowAsync("학년도를 올바르게 입력하세요.", "오류");
            TxtYear.Focus(FocusState.Programmatic);
            return;
        }

        if (!int.TryParse(TxtGrade.Text, out int grade) || grade < 1 || grade > 3)
        {
            await MessageBox.ShowAsync("학년은 1~3 사이의 숫자로 입력하세요.", "오류");
            TxtGrade.Focus(FocusState.Programmatic);
            return;
        }

        if (!int.TryParse(TxtClass.Text, out int cls) || cls < 1)
        {
            await MessageBox.ShowAsync("학급은 1 이상의 숫자로 입력하세요.", "오류");
            TxtClass.Focus(FocusState.Programmatic);
            return;
        }

        if (!int.TryParse(TxtNumber.Text, out int number) || number < 1)
        {
            await MessageBox.ShowAsync("번호는 1 이상의 숫자로 입력하세요.", "오류");
            TxtNumber.Focus(FocusState.Programmatic);
            return;
        }

        string name = TxtName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await MessageBox.ShowAsync("이름을 입력하세요.", "오류");
            TxtName.Focus(FocusState.Programmatic);
            return;
        }

        // 중복 검사
        if (await IsDuplicateAsync(year, grade, cls, number))
        {
            await MessageBox.ShowAsync($"{year}학년도 {grade}학년 {cls}반 {number}번은 이미 존재합니다.", "오류");
            return;
        }

        // 학생 추가
        string studentId = await GenerateUniqueStudentIDAsync(year);
        if (string.IsNullOrEmpty(studentId))
        {
            await MessageBox.ShowAsync("고유 ID 생성 실패", "오류");
            return;
        }

        NewStudents.Add(new StudentAddViewModel
        {
            StudentID = studentId,
            Year = year,
            Grade = grade,
            Class = cls,
            Number = number,
            Name = name
        });

        // 입력 필드 초기화
        TxtNumber.Text = string.Empty;
        TxtName.Text = string.Empty;
        TxtNumber.Focus(FocusState.Programmatic);
        BtnSave.IsEnabled = NewStudents.Count > 0 ? true : false;
        BtnExport.IsEnabled = NewStudents.Count > 0 ? true : false;
        TbBlankList.Visibility = NewStudents.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    #endregion

    #region 템플릿 다운로드

    /// <summary>
    /// Excel 템플릿 다운로드
    /// </summary>
    private async void BtnDownloadTemplate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadingProgressRing.IsActive = true;
            BtnDownloadTemplate.IsEnabled = false;
            var window = App.MainWindow;
            if (window == null)
            {
                await MessageBox.ShowAsync("메인 창을 찾을 수 없습니다.", "오류");
                return;
            }
            bool success = await ExcelHelpers.DownloadStudentTemplateAsync(
                window);

            if (success)
            {
                await MessageBox.ShowAsync("템플릿 파일이 다운로드되고 열렸습니다.\n" +
                    "이 템플릿을 참고하여 학생 정보를 입력해주세요.", "알림");
            }
            else
            {
                await MessageBox.ShowAsync("템플릿 다운로드가 취소되었습니다.", "오류");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"템플릿 다운로드 중 오류:\n{ex.Message}", "오류");
        }
        finally
        {
            LoadingProgressRing.IsActive = false;
            BtnDownloadTemplate.IsEnabled = true;
        }
    }

    #endregion

    #region 목록 내보내기

    /// <summary>
    /// 현재 목록을 Excel로 내보내기
    /// </summary>
    private async void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (NewStudents.Count == 0)
        {
            await MessageBox.ShowAsync("내보낼 학생이 없습니다.", "오류");
            return;
        }

        try
        {
            LoadingProgressRing.IsActive = true;
            BtnExport.IsEnabled = false;
            Window? window = App.MainWindow;
            if (window == null)
            {
                await MessageBox.ShowAsync("메인 창을 찾을 수 없습니다.", "오류");
                return;
            } 
            bool success = await ExcelHelpers.ExportStudentsToExcelAsync(
                window,
                NewStudents.Select(s => new StudentExportModel
                {
                    학년도 = s.Year,
                    학년 = s.Grade,
                    반 = s.Class,
                    번호 = s.Number,
                    이름 = s.Name,
                    학생ID = s.StudentID
                }),
                title: "추가할_학생_목록",
                openAfterSave: true
            );

            if (success)
            {
                await MessageBox.ShowAsync($"{NewStudents.Count}명의 학생 목록이 Excel 파일로 저장되었습니다.", "알림");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"내보내기 중 오류:\n{ex.Message}", "오류");
        }
        finally
        {
            LoadingProgressRing.IsActive = false;
            BtnExport.IsEnabled = true;
        }
    }

    /// <summary>
    /// Excel 내보내기용 모델
    /// </summary>
    private class StudentExportModel
    {
        public int 학년도 { get; set; }
        public int 학년 { get; set; }
        public int 반 { get; set; }
        public int 번호 { get; set; }
        public string 이름 { get; set; } = string.Empty;
        public string 학생ID { get; set; } = string.Empty;
    }

    #endregion

    #region 학생 삭제

    /// <summary>
    /// 학생 삭제
    /// </summary>
    private void BtnRemoveStudent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string studentId)
        {
            var student = NewStudents.FirstOrDefault(s => s.StudentID == studentId);
            if (student != null)
            {
                NewStudents.Remove(student);
            }
        }
    }

    #endregion

    #region DB 저장

    /// <summary>
    /// DB에 저장
    /// </summary>
    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (NewStudents.Count == 0)
        {
            await MessageBox.ShowAsync("추가할 학생이 없습니다.", "오류");
            return;
        }

        try
        {
            LoadingProgressRing.IsActive = true;
            BtnSave.IsEnabled = false;

            int successCount = 0;
            int failCount = 0;
            var errors = new List<string>();

            // 각 학생마다 독립적으로 저장 (트랜잭션 분리)
            foreach (var vm in NewStudents.ToList())
            {
                bool saved = await SaveStudentAsync(vm);

                if (saved)
                {
                    successCount++;
                    NewStudents.Remove(vm);
                }
                else
                {
                    failCount++;
                    errors.Add($"{vm.Name}({vm.Grade}-{vm.Class}-{vm.Number})");
                }
            }

            // 결과 메시지
            string resultMessage = $"저장 완료: {successCount}명";
            if (failCount > 0)
            {
                resultMessage += $"\n실패: {failCount}명";
                if (errors.Count > 0)
                {
                    resultMessage += "\n\n실패 목록:\n" + string.Join("\n", errors.Take(5));
                    if (errors.Count > 5)
                    {
                        resultMessage += $"\n... 외 {errors.Count - 5}건";
                    }
                }
            }

            await MessageBox.ShowAsync(resultMessage, "알림");
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"저장 중 오류 발생:\n{ex.Message}", "오류");
        }
        finally
        {
            LoadingProgressRing.IsActive = false;
            BtnSave.IsEnabled = true;
        }
    }

    /// <summary>
    /// 학생 한 명 저장 (Student + Enrollment 동시 저장)
    /// 트랜잭션을 사용하여 원자성 보장
    /// </summary>
    private async Task<bool> SaveStudentAsync(StudentAddViewModel vm)
    {
        // ⭐ SchoolDatabase.DbPath 사용 (Data 폴더 자동 포함)
        string dbPath = SchoolDatabase.DbPath;

        try
        {
            // StudentRepository만 사용 (단일 Connection)
            using var studentRepo = new StudentRepository(dbPath);

            // 트랜잭션 시작
            studentRepo.BeginTransaction();

            try
            {
                // 1. Student 테이블에 학생 기본정보 저장
                var student = new Student
                {
                    StudentID = vm.StudentID,
                    Name = vm.Name,
                    Sex = "남", // 기본값 (향후 입력받을 수 있음)
                    //BirthDate = string.Empty,
                    Phone = string.Empty,
                    Email = string.Empty,
                    Address = string.Empty,
                    Memo = string.Empty,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    IsDeleted = false
                };

                await studentRepo.CreateAsync(student);
                System.Diagnostics.Debug.WriteLine($"[AddStudents] Student 저장 완료: {vm.StudentID}");

                // 2. Enrollment 테이블에 학적정보 저장 (같은 Connection 사용)
                await InsertEnrollmentDirectlyAsync(studentRepo, vm);
                System.Diagnostics.Debug.WriteLine($"[AddStudents] Enrollment 저장 완료: {vm.Grade}-{vm.Class}-{vm.Number}");

                // 트랜잭션 커밋 - 둘 다 성공해야 저장됨
                studentRepo.Commit();

                System.Diagnostics.Debug.WriteLine($"[AddStudents] 저장 성공: {vm.Name} ({vm.StudentID})");
                return true;
            }
            catch (Exception ex)
            {
                // 오류 발생 시 롤백 - Student와 Enrollment 모두 취소
                studentRepo.Rollback();
                System.Diagnostics.Debug.WriteLine($"[AddStudents] 저장 실패 (롤백): {vm.Name} - {ex.Message}");
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AddStudents] 연결 오류: {vm.Name} - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// StudentRepository의 Connection과 Transaction을 직접 사용하여 Enrollment INSERT
    /// </summary>
    private async Task InsertEnrollmentDirectlyAsync(StudentRepository studentRepo, StudentAddViewModel vm)
    {
        var connection = studentRepo.GetConnection();
        var transaction = studentRepo.GetTransaction();

        if (connection == null)
            throw new InvalidOperationException("Connection이 null입니다.");

        if (transaction == null)
            throw new InvalidOperationException("Transaction이 null입니다.");

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;

        // Enrollment INSERT 쿼리 (Name, Sex, Photo 포함)
        cmd.CommandText = @"
                INSERT INTO Enrollment (
                    StudentID, Name, Sex, Photo, SchoolCode, Year, Semester, Grade, Class, Number,
                    Status, TeacherID, AdmissionDate, GraduationDate,
                    TransferOutDate, TransferOutSchool, TransferInDate, TransferInSchool,
                    Memo, CreatedAt, UpdatedAt, IsDeleted
                ) VALUES (
                    @StudentID, @Name, @Sex, @Photo, @SchoolCode, @Year, @Semester, @Grade, @Class, @Number,
                    @Status, @TeacherID, @AdmissionDate, @GraduationDate,
                    @TransferOutDate, @TransferOutSchool, @TransferInDate, @TransferInSchool,
                    @Memo, @CreatedAt, @UpdatedAt, @IsDeleted
                )";

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 파라미터 추가
        cmd.Parameters.AddWithValue("@StudentID", vm.StudentID);
        cmd.Parameters.AddWithValue("@Name", vm.Name); // ⭐ Name 추가
        cmd.Parameters.AddWithValue("@Sex", "남"); // ⭐ Sex 추가 (기본값)
        cmd.Parameters.AddWithValue("@Photo", string.Empty); // ⭐ Photo 추가
        cmd.Parameters.AddWithValue("@SchoolCode", Settings.SchoolCode.Value);
        cmd.Parameters.AddWithValue("@Year", vm.Year);
        cmd.Parameters.AddWithValue("@Semester", Settings.WorkSemester.Value);
        cmd.Parameters.AddWithValue("@Grade", vm.Grade);
        cmd.Parameters.AddWithValue("@Class", vm.Class);
        cmd.Parameters.AddWithValue("@Number", vm.Number);
        cmd.Parameters.AddWithValue("@Status", "재학");
        cmd.Parameters.AddWithValue("@TeacherID", Settings.User.Value ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@AdmissionDate", DateTime.Now.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@GraduationDate", DBNull.Value);
        cmd.Parameters.AddWithValue("@TransferOutDate", DBNull.Value);
        cmd.Parameters.AddWithValue("@TransferOutSchool", DBNull.Value);
        cmd.Parameters.AddWithValue("@TransferInDate", DBNull.Value);
        cmd.Parameters.AddWithValue("@TransferInSchool", DBNull.Value);
        cmd.Parameters.AddWithValue("@Memo", string.Empty);
        cmd.Parameters.AddWithValue("@CreatedAt", now);
        cmd.Parameters.AddWithValue("@UpdatedAt", now);
        cmd.Parameters.AddWithValue("@IsDeleted", 0);

        int rowsAffected = await cmd.ExecuteNonQueryAsync();

        if (rowsAffected == 0)
            throw new Exception("Enrollment 저장 실패: 영향받은 행이 0개입니다.");
    }

    #endregion

    #region 헬퍼 메서드

    /// <summary>
    /// 중복 확인 (현재 목록 + DB)
    /// Enrollment 테이블에서 Year, Grade, Class, Number 조합이 존재하는지 확인
    /// </summary>
    private async Task<bool> IsDuplicateAsync(int year, int grade, int cls, int number)
    {
        // 1. 현재 추가 목록에서 중복 확인
        if (NewStudents.Any(s => s.Year == year && s.Grade == grade && s.Class == cls && s.Number == number))
        {
            System.Diagnostics.Debug.WriteLine($"[AddStudents] 목록 내 중복: {year}년 {grade}-{cls}-{number}");
            return true;
        }

        // 2. DB에서 중복 확인
        try
        {
            // ⭐ SchoolDatabase.DbPath 사용
            string dbPath = SchoolDatabase.DbPath;
            string schoolCode = Settings.SchoolCode.Value;
            int semester = Settings.WorkSemester.Value;

            using var enrollmentRepo = new EnrollmentRepository(dbPath);

            // 해당 학급의 모든 학생 조회
            var classStudents = await enrollmentRepo.GetByClassAsync(
                schoolCode, year, grade, cls);

            // 같은 번호가 있는지 확인
            bool exists = classStudents.Any(e => e.Number == number && !e.IsDeleted);

            if (exists)
            {
                System.Diagnostics.Debug.WriteLine($"[AddStudents] DB 중복: {year}년 {grade}-{cls}-{number}");
            }

            return exists;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AddStudents] 중복 확인 오류: {ex.Message}");
            // 오류 발생 시 안전을 위해 중복으로 간주하지 않음 (사용자에게 추가 기회 제공)
            return false;
        }
    }

    /// <summary>
    /// 고유 학생 ID 생성 (DB 확인 포함)
    /// 형식: 학교코드(7자리) + 입학년도(4자리) + 일련번호(4자리) = 총 15자리
    /// </summary>
    private async Task<string> GenerateUniqueStudentIDAsync(int year)
    {
        string schoolCode = Settings.SchoolCode?.Value ?? "0000000";

        // 최대 100번 시도
        for (int attempt = 0; attempt < 100; attempt++)
        {
            // 1~9999 범위의 랜덤 일련번호 생성
            int sequence = _random.Next(1, 10000);
            string studentId = Student.GenerateStudentID(schoolCode, year, sequence);

            // 고유성 확인
            if (await IsUniqueIDAsync(studentId))
            {
                System.Diagnostics.Debug.WriteLine($"[AddStudents] 고유 ID 생성 성공: {studentId}");
                return studentId;
            }
        }

        System.Diagnostics.Debug.WriteLine("[AddStudents] 고유 ID 생성 실패: 100번 시도 후 실패");
        return string.Empty;
    }

    /// <summary>
    /// ID 고유성 확인 (현재 목록 + DB)
    /// Student 테이블에서 StudentID가 존재하는지 확인
    /// </summary>
    private async Task<bool> IsUniqueIDAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        // 1. 현재 추가 목록에서 확인
        if (NewStudents.Any(s => s.StudentID == id))
        {
            System.Diagnostics.Debug.WriteLine($"[AddStudents] 목록 내 ID 중복: {id}");
            return false;
        }

        // 2. DB에서 확인
        try
        {
            // ⭐ SchoolDatabase.DbPath 사용
            string dbPath = SchoolDatabase.DbPath;
            using var studentRepo = new StudentRepository(dbPath);

            var existingStudent = await studentRepo.GetByIdAsync(id);

            if (existingStudent != null && !existingStudent.IsDeleted)
            {
                System.Diagnostics.Debug.WriteLine($"[AddStudents] DB에 ID 존재: {id}");
                return false;
            }

            return true; // 중복 없음
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AddStudents] ID 확인 오류: {ex.Message}");
            // 오류 발생 시 안전을 위해 중복으로 간주 (데이터 무결성 보호)
            return false;
        }
    }

    /// <summary>
    /// 학년 입력 받기 (UI 스레드에서 실행)
    /// </summary>
    private async Task<int> GetGradeInputAsync(string message)
    {
        // UI 작업이므로 반드시 UI 스레드에서 실행되어야 함
        var inputBox = new TextBox { PlaceholderText = "1~3" };
        var stackPanel = new StackPanel();
        stackPanel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });
        stackPanel.Children.Add(inputBox);

        var dialog = new ContentDialog
        {
            Title = "학년 입력",
            Content = stackPanel,
            PrimaryButtonText = "확인",
            CloseButtonText = "취소",
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (int.TryParse(inputBox.Text, out int grade) && grade >= 1 && grade <= 3)
                return grade;
        }

        return 0;
    }

    /// <summary>
    /// 학급 입력 받기 (UI 스레드에서 실행)
    /// </summary>
    private async Task<int> GetClassInputAsync(string message)
    {
        // UI 작업이므로 반드시 UI 스레드에서 실행되어야 함
        var inputBox = new TextBox { PlaceholderText = "1 이상" };
        var stackPanel = new StackPanel();
        stackPanel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });
        stackPanel.Children.Add(inputBox);

        var dialog = new ContentDialog
        {
            Title = "학급 입력",
            Content = stackPanel,
            PrimaryButtonText = "확인",
            CloseButtonText = "취소",
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (int.TryParse(inputBox.Text, out int cls) && cls >= 1)
                return cls;
        }

        return 0;
    }

    /// <summary>
    /// 숫자만 입력 허용
    /// </summary>
    private void NumberBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // 숫자, 백스페이스, 탭, 화살표 키만 허용
        if (e.Key < Windows.System.VirtualKey.Number0 || e.Key > Windows.System.VirtualKey.Number9)
        {
            if (e.Key < Windows.System.VirtualKey.NumberPad0 || e.Key > Windows.System.VirtualKey.NumberPad9)
            {
                if (e.Key != Windows.System.VirtualKey.Back &&
                    e.Key != Windows.System.VirtualKey.Tab &&
                    e.Key != Windows.System.VirtualKey.Left &&
                    e.Key != Windows.System.VirtualKey.Right)
                {
                    e.Handled = true;
                }
            }
        }
    }

    #endregion
}

/// <summary>
/// 학생 추가 ViewModel
/// </summary>
public class StudentAddViewModel : NotifyPropertyChangedBase
{
    private string _studentId = string.Empty;
    private int _year;
    private int _grade;
    private int _class;
    private int _number;
    private string _name = string.Empty;

    public string StudentID
    {
        get => _studentId;
        set => SetProperty(ref _studentId, value);
    }

    public int Year
    {
        get => _year;
        set => SetProperty(ref _year, value);
    }

    public int Grade
    {
        get => _grade;
        set => SetProperty(ref _grade, value);
    }

    public int Class
    {
        get => _class;
        set => SetProperty(ref _class, value);
    }

    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string ClassInfo => $"{Year}학년도 {Grade}학년 {Class}반 {Number}번";
}
