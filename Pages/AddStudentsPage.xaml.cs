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

    /// <summary>
    /// 새 학적에 넣을 학기 값.
    ///
    /// <para><b>학적은 학년 단위로 다룬다(2026-07-30 확정).</b> 조회는 어디서도 학기를 걸지 않으므로
    /// (<c>EnrollmentService.GetEnrollmentsAsync</c> 참고) 이 값은 사실상 자리표시자이고,
    /// 1 로 고정해 두면 <c>UNIQUE(StudentID, SchoolCode, Year, Semester)</c> 가
    /// "한 학년도에 학적 한 건"을 그대로 강제해 준다.</para>
    ///
    /// ⚠ 예전에는 <c>Settings.WorkSemester</c> 를 넣었다. 그래서 2학기에 등록하면 학기로
    /// 거르던 화면들에서 그 학생이 통째로 사라졌다. 다시 현재 학기를 넣지 말 것.
    /// </summary>
    private const int EnrollmentSemester = 1;

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (this.Frame != null && this.Frame.CanGoBack)
            this.Frame.GoBack();
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
        // .xls(BIFF)는 넣지 않는다 — MiniExcel 은 xlsx/csv 만 읽어서
        // 구버전 .xls 를 고르면 "Excel 파일 읽기 오류" 로만 끝난다.
        picker.FileTypeFilter.Add(".xlsx");

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
        // 파일 파싱은 백그라운드에서 (대용량 파일에서도 UI 멈춤 방지)
        var sheetsData = await ExcelHelper.DataToTextAsync(file.Path);

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
        int gradeCol = -1, classCol = -1, numberCol = -1, nameCol = -1, sexCol = -1;
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
                else if (cellValue.Equals("성별", StringComparison.OrdinalIgnoreCase))
                    sexCol = col;
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
            // 번호 ("1번", "1" 등 처리)
            if (!TryParseNumberFromText(sheetData[row, numberCol], out int number) || number < 1)
                continue;

            // 이름 (1-based)
            string name = (sheetData[row, nameCol] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // 성별 ("남"/"여", 없으면 "남" 기본값)
            string sex = "남";
            if (sexCol != -1)
                sex = NormalizeSex(sheetData[row, sexCol]);

            // 학년 ("1학년", "1" 등 처리)
            int grade = defaultGrade;
            if (gradeCol != -1)
            {
                if (TryParseNumberFromText(sheetData[row, gradeCol], out int g) && g >= 1 && g <= 6)
                    grade = g;
                else if (defaultGrade == 0)
                    grade = await GetGradeInputAsync($"학생 '{name}'의 학년 정보를 입력하세요.");
            }
            if (grade == 0) continue;

            // 학급 ("1반", "1" 등 처리)
            int cls = defaultClass;
            if (classCol != -1)
            {
                if (TryParseNumberFromText(sheetData[row, classCol], out int c) && c >= 1)
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
                Name = name,
                Sex = sex
            });
        }
    }

    /// <summary>
    /// 성별 텍스트 정규화 ("남"/"남자"/"M" → "남", "여"/"여자"/"F" → "여", 그 외 → "남")
    /// </summary>
    private static string NormalizeSex(string? text) => ImportParsing.NormalizeSex(text);

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

        // 상한 6 - 엑셀 가져오기는 1~6 을 받는데 여기만 3 이라 초등 4~6학년은 수동 추가가 막혀 있었다
        if (!int.TryParse(TxtGrade.Text, out int grade) || grade < 1 || grade > 6)
        {
            await MessageBox.ShowAsync("학년은 1~6 사이의 숫자로 입력하세요.", "오류");
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

        // 성별 (ComboBox 선택값, 기본 "남")
        string sex = (CboSex.SelectedItem as ComboBoxItem)?.Content as string ?? "남";

        NewStudents.Add(new StudentAddViewModel
        {
            StudentID = studentId,
            Year = year,
            Grade = grade,
            Class = cls,
            Number = number,
            Name = name,
            Sex = sex
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
            var result = await ExcelHelpers.DownloadStudentTemplateAsync(window);

            switch (result)
            {
                case TemplateDownloadResult.Completed:
                    await MessageBox.ShowAsync("템플릿 파일이 다운로드되고 열렸습니다.\n" +
                        "이 템플릿을 참고하여 학생 정보를 입력해주세요.", "알림");
                    break;

                case TemplateDownloadResult.Failed:
                    await MessageBox.ShowAsync(
                        "템플릿 파일을 만들지 못했습니다.\n" +
                        "앱 설정 > 고급 > [로그 폴더 열기] 에서 자세한 내용을 볼 수 있습니다.",
                        "오류");
                    break;

                // 취소는 사용자가 한 일이라 따로 알리지 않는다.
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
                    성별 = s.Sex,
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
        public string 성별 { get; set; } = string.Empty;
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

    /// <summary>
    /// 전체 선택
    /// </summary>
    private void ChkSelectAll_Checked(object sender, RoutedEventArgs e)
    {
        foreach (var s in NewStudents) s.IsSelected = true;
    }

    /// <summary>
    /// 전체 선택 해제
    /// </summary>
    private void ChkSelectAll_Unchecked(object sender, RoutedEventArgs e)
    {
        foreach (var s in NewStudents) s.IsSelected = false;
    }

    /// <summary>
    /// 선택 항목 삭제
    /// </summary>
    private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = NewStudents.Where(s => s.IsSelected).ToList();
        foreach (var s in selected)
        {
            NewStudents.Remove(s);
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
                // 실패 사유까지 받는다 - 예전에는 이름만 보여주고 원인은 Debug 로그에만 남아
                // 사용자가 무엇을 고쳐야 할지 알 수 없었다
                string? error = await SaveStudentAsync(vm);

                if (error == null)
                {
                    successCount++;
                    NewStudents.Remove(vm);
                }
                else
                {
                    failCount++;
                    errors.Add($"{vm.Name}({vm.Grade}-{vm.Class}-{vm.Number}): {error}");
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
    /// 학생 한 명 저장 (Student + Enrollment 동시 저장). 트랜잭션으로 원자성 보장.
    /// </summary>
    /// <returns>성공하면 null, 실패하면 사용자에게 보여줄 실패 사유.</returns>
    private async Task<string?> SaveStudentAsync(StudentAddViewModel vm)
    {
        // ⭐ SchoolDatabase.DbPath 사용 (Data 폴더 자동 포함)
        string dbPath = SchoolDatabase.DbPath;

        try
        {
            using var studentRepo = new StudentRepository(dbPath);

            // 학적 Repository 는 학생 Repository 의 **연결을 공유**한다. 각자 연결을 열면
            // 트랜잭션이 공유되지 않아(BaseRepository 가 "이 연결에서 시작된 트랜잭션인가"를
            // 확인하고 아니면 무시한다) 학생만 저장되고 학적은 밖에서 도는 상태가 된다.
            using var enrollmentRepo = new EnrollmentRepository(studentRepo.GetConnection());

            // 트랜잭션 시작
            studentRepo.BeginTransaction();
            enrollmentRepo.SetTransaction(studentRepo.GetTransaction());

            try
            {
                // 1. Student 테이블에 학생 기본정보 저장
                var student = new Student
                {
                    StudentID = vm.StudentID,
                    Name = vm.Name,
                    Sex = vm.Sex,
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

                // 2. Enrollment 테이블에 학적정보 저장 (같은 Connection·Transaction)
                var enrollment = new Enrollment
                {
                    StudentID = vm.StudentID,
                    Name = vm.Name,
                    Sex = vm.Sex,
                    Photo = string.Empty,
                    SchoolCode = Settings.SchoolCode.Value,
                    Year = vm.Year,
                    Semester = EnrollmentSemester,
                    Grade = vm.Grade,
                    Class = vm.Class,
                    Number = vm.Number,
                    Status = EnrollmentStatus.Enrolled,
                    // 담임이 비어 있으면 Teacher FK 위반이 되므로 리포지토리가 NULL 로 바꿔 넣는다.
                    TeacherID = Settings.User.Value ?? string.Empty,
                    AdmissionDate = DateTime.Now.ToString("yyyy-MM-dd"),
                    Memo = string.Empty,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    IsDeleted = false
                };

                if (await enrollmentRepo.CreateAsync(enrollment) <= 0)
                    throw new InvalidOperationException("학적이 저장되지 않았습니다.");

                System.Diagnostics.Debug.WriteLine($"[AddStudents] Enrollment 저장 완료: {vm.Grade}-{vm.Class}-{vm.Number}");

                // 트랜잭션 커밋 - 둘 다 성공해야 저장됨
                studentRepo.Commit();

                System.Diagnostics.Debug.WriteLine($"[AddStudents] 저장 성공: {vm.Name} ({vm.StudentID})");
                return null;
            }
            catch (Exception ex)
            {
                // 오류 발생 시 롤백 - Student와 Enrollment 모두 취소
                studentRepo.Rollback();
                System.Diagnostics.Debug.WriteLine($"[AddStudents] 저장 실패 (롤백): {vm.Name} - {ex}");
                return ex.Message;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AddStudents] 연결 오류: {vm.Name} - {ex}");
            return ex.Message;
        }
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
        if (NewStudents.Any(s => s.Year == year &&
                                 s.Grade == grade && s.Class == cls && s.Number == number))
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

            using var enrollmentRepo = new EnrollmentRepository(dbPath);

            // 해당 학급 학생 조회. 학기는 보지 않는다 - 명부는 학년 단위이므로
            // 같은 학년도-학년-반에 같은 번호가 있으면 학기와 무관하게 중복이다.
            var classStudents = await enrollmentRepo.GetByClassAsync(
                schoolCode, year, grade, cls);

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
            // (학년,반,번호) UNIQUE 제약이 없어 이 검사가 유일한 방어선이므로,
            // 검증 실패 시엔 중복으로 간주해 잘못된 중복 삽입을 막는다.
            return true;
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

            // IsDeleted 를 봐선 안 된다. 학생 삭제는 논리 삭제(행 유지)이고 StudentID 에
            // UNIQUE 제약이 있어, 삭제된 학생의 ID 를 "사용 가능"으로 판정하면 저장 단계에서
            // UNIQUE 위반으로 실패한다(그 실패 이유는 예전엔 Debug 로그에만 남았다).
            if (existingStudent != null)
            {
                System.Diagnostics.Debug.WriteLine($"[AddStudents] DB에 ID 존재: {id} (삭제됨={existingStudent.IsDeleted})");
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
    /// 텍스트에서 숫자 추출 ("1학년" → 1, "3반" → 3, "1" → 1)
    /// </summary>
    private static bool TryParseNumberFromText(string? text, out int result)
        => ImportParsing.TryParseNumberFromText(text, out result);

    /// <summary>
    /// 학년 입력 받기 (UI 스레드에서 실행)
    /// </summary>
    private async Task<int> GetGradeInputAsync(string message)
    {
        // UI 작업이므로 반드시 UI 스레드에서 실행되어야 함
        // 초등학교는 6학년까지다 — 1~3 으로 막아 두어 4~6학년 명단을 엑셀로 등록할 수 없었다.
        var inputBox = new TextBox { PlaceholderText = "1~6" };
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

        if (await MessageBox.ShowDialogAsync(dialog) == ContentDialogResult.Primary)
        {
            if (int.TryParse(inputBox.Text, out int grade) && grade >= 1 && grade <= 6)
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

        if (await MessageBox.ShowDialogAsync(dialog) == ContentDialogResult.Primary)
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
public partial class StudentAddViewModel : NotifyPropertyChangedBase
{
    private string _studentId = string.Empty;
    private int _year;
    private int _grade;
    private int _class;
    private int _number;
    private string _name = string.Empty;
    private string _sex = "남";

    public string StudentID
    {
        get => _studentId;
        set => SetProperty(ref _studentId, value);
    }

    public string Sex
    {
        get => _sex;
        set => SetProperty(ref _sex, value);
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

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
