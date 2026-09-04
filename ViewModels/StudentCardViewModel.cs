using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.ViewModels;

/// <summary>
/// StudentCard용 간소화된 ViewModel
/// 모델을 직접 노출하여 불필요한 래핑 제거
/// </summary>
public sealed class StudentCardViewModel : NotifyPropertyChangedBase, IDisposable
{
    #region Fields

    private Student? _student;
    private StudentDetail? _detail;
    private Enrollment? _enrollment;
    private BitmapImage? _photoImage;
    private bool _isChanged = false;
    private bool _isLoading = false;

    // Services
    // ⚠ 미리 만들지 않는다. 이 서비스 셋은 저마다 리포지토리를 만들고,
    //   BaseRepository 는 생성자에서 SQLite 연결을 연다.
    //
    //   예전에는 인자 없는 생성자가 넷을 바로 만들었다. 그런데 이 ViewModel 은
    //   내보내기·인쇄 경로에서 **학급 인원수만큼 루프로** 만들어지고
    //   (StudentCardPrintService.LoadClassStudentsAsync·UnifiedExportService·
    //   PageStudentLog), 그 경로들은 LoadFromModels 로 이미 읽어 둔 모델을 넣어 주므로
    //   **서비스를 쓰지도 않는다**. 30명이면 연결 90개가 열렸다 닫히지 않았다.
    //
    //   이제 아래 접근자를 처음 부를 때 만든다 — 카드 화면에서 직접 읽고 쓸 때뿐이다.
    private StudentService? _studentService;
    private StudentDetailService? _studentDetailService;
    private EnrollmentService? _enrollmentService;
    private PhotoService? _photoService;

    /// <summary>
    /// 서비스를 <b>바깥에서 받았는가</b>. 받았다면 수명은 준 쪽 것이므로 여기서 놓아주지 않는다.
    /// 인자 없는 생성자로 만들었을 때만 이 ViewModel 이 주인이다.
    /// </summary>
    private readonly bool _ownsServices;

    private StudentService StudentSvc => _studentService ??= new StudentService(SchoolDatabase.DbPath);
    private StudentDetailService DetailSvc => _studentDetailService ??= new StudentDetailService(SchoolDatabase.DbPath);
    private EnrollmentService EnrollmentSvc => _enrollmentService ??= new EnrollmentService();
    private PhotoService PhotoSvc => _photoService ??= new PhotoService();

    #endregion

    #region Constructor

    public StudentCardViewModel()
    {
        _ownsServices = true;   // 필요해질 때 만들고, 다 쓰면 여기서 놓아준다

        // ✅ 바인딩 오류 방지를 위해 빈 객체로 초기화
        _student = new Student();
        _detail = new StudentDetail();
    }

    public StudentCardViewModel(
        StudentService studentService,
        StudentDetailService studentDetailService,
        EnrollmentService enrollmentService,
        PhotoService photoService)
    {
        _studentService = studentService;
        _studentDetailService = studentDetailService;
        _enrollmentService = enrollmentService;
        _photoService = photoService;

        // ✅ 바인딩 오류 방지를 위해 빈 객체로 초기화
        _student = new Student();
        _detail = new StudentDetail();
    }

    #endregion

    #region Properties - 모델 직접 노출 (핵심 변경!)

    /// <summary>학생 기본 정보 (직접 바인딩)</summary>
    public Student? Student
    {
        get => _student;
        private set
        {
            // 기존 모델 이벤트 해제
            if (_student != null)
                _student.PropertyChanged -= OnModelPropertyChanged;

            SetProperty(ref _student, value);

            // 새 모델 이벤트 구독
            if (_student != null)
                _student.PropertyChanged += OnModelPropertyChanged;

            // 관련 프로퍼티 업데이트
            OnPropertyChanged(nameof(StudentID));
            OnPropertyChanged(nameof(IsLoaded));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Age));
        }
    }

    /// <summary>학생 상세 정보 (직접 바인딩)</summary>
    public StudentDetail? Detail
    {
        get => _detail;
        private set
        {
            // 기존 모델 이벤트 해제
            if (_detail != null)
                _detail.PropertyChanged -= OnModelPropertyChanged;

            SetProperty(ref _detail, value);

            // Detail이 없으면 자동 생성
            EnsureDetailExists();

            // 새 모델 이벤트 구독
            if (_detail != null)
                _detail.PropertyChanged += OnModelPropertyChanged;

            // 관련 프로퍼티 업데이트
            OnPropertyChanged(nameof(HasSpecialConsiderations));
        }
    }

    /// <summary>학적 정보 (직접 바인딩)</summary>
    public Enrollment? Enrollment
    {
        get => _enrollment;
        private set
        {
            // 기존 모델 이벤트 해제
            if (_enrollment != null)
                _enrollment.PropertyChanged -= OnModelPropertyChanged;

            SetProperty(ref _enrollment, value);

            // 새 모델 이벤트 구독
            if (_enrollment != null)
                _enrollment.PropertyChanged += OnModelPropertyChanged;

            // 관련 프로퍼티 업데이트
            OnPropertyChanged(nameof(ClassInfo));
        }
    }

    #endregion

    #region Properties - ViewModel 고유 상태

    /// <summary>변경 사항 여부</summary>
    public bool IsChanged
    {
        get => _isChanged;
        private set => SetProperty(ref _isChanged, value);
    }

    /// <summary>로딩 중 여부</summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    /// <summary>사진 이미지 (UI 바인딩용)</summary>
    public BitmapImage? PhotoImage
    {
        get => _photoImage;
        private set => SetProperty(ref _photoImage, value);
    }

    #endregion

    #region Properties - 계산 프로퍼티 (읽기 전용)

    /// <summary>학생 ID</summary>
    public string StudentID => Student?.StudentID ?? string.Empty;

    /// <summary>학생 이름 (빠른 접근용)</summary>
    public string Name => Student?.Name ?? string.Empty;

    /// <summary>나이 (계산)</summary>
    public int Age => Student?.GetAge() ?? 0;

    /// <summary>데이터 로드 완료 여부</summary>
    public bool IsLoaded => !string.IsNullOrEmpty(Student?.StudentID);

    /// <summary>학급 정보 문자열 (예: "2학년 3반 15번")</summary>
    public string ClassInfo => Enrollment?.GetClassInfo() ?? string.Empty;

    /// <summary>특이사항 여부</summary>
    public bool HasSpecialConsiderations => Detail?.HasSpecialConsiderations() ?? false;

    #endregion

    #region Methods - 데이터 로드

    /// <summary>
    /// Enrollment 기반 간이 초기화 (배치 내보내기용 — DB 호출 없음)
    /// </summary>
    public void LoadFromEnrollment(Enrollment enrollment)
    {
        Student = new Student
        {
            StudentID = enrollment.StudentID,
            Name = enrollment.Name,
            Sex = enrollment.Sex,
            Photo = enrollment.Photo
        };
        Enrollment = enrollment;
        IsChanged = false;
    }

    /// <summary>
    /// Enrollment + Student + StudentDetail 일괄 초기화 (학급 배치 내보내기용).
    /// DB 호출 없이 미리 로드한 모델들을 주입한다.
    /// </summary>
    public void LoadFromModels(Enrollment enrollment, Student? student, StudentDetail? detail)
    {
        Student = student ?? new Student
        {
            StudentID = enrollment.StudentID,
            Name = enrollment.Name,
            Sex = enrollment.Sex,
            Photo = enrollment.Photo
        };
        Enrollment = enrollment;
        if (detail != null) Detail = detail;
        IsChanged = false;
    }

    /// <summary>
    /// 학생 정보 로드 (Student + StudentDetail + Enrollment)
    /// </summary>
    public async Task LoadStudentAsync(string studentId)
    {
        if (string.IsNullOrEmpty(studentId))
            return;

        IsLoading = true;

        try
        {
            // 1. Student 로드
            using var StudentService = new StudentService(SchoolDatabase.DbPath);
            Student = await StudentService.GetBasicInfoAsync(studentId);

            // 2. StudentDetail 로드
            using var StudentDetailService = new StudentDetailService(SchoolDatabase.DbPath);
            Detail = await StudentDetailService.GetByStudentIdAsync(studentId);

            // 3. Enrollment 로드 (현재 학기)
            Enrollment = await EnrollmentSvc.GetCurrentEnrollmentAsync(studentId);

            // 4. 사진 로드
            if (Student != null)
            {
                PhotoImage = await PhotoSvc.LoadPhotoAsync(Student.Photo);
            }

            IsChanged = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentCardViewModel] LoadStudentAsync 오류: {ex.Message}");
            throw;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 현재 학생 정보 새로고침
    /// </summary>
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(StudentID))
            return;

        await LoadStudentAsync(StudentID);
    }

    #endregion

    #region Methods - 데이터 저장

    /// <summary>
    /// 변경 사항 저장
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        if (Student == null)    
            return false;
        if (!IsChanged || !IsLoaded)
            return false;

        try
        {
            // 1. Student 업데이트 — 0행 갱신(정본 없음)은 실패로 취급해야
            //    IsChanged 를 유지하고 호출부가 사용자에게 알릴 수 있다.
            Student.UpdatedAt = DateTime.Now;
            if (!await StudentSvc.UpdateBasicInfoAsync(Student))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[StudentCardViewModel] 기본정보 갱신 0행: StudentID={Student.StudentID}, No={Student.No}");
                return false;
            }

            // 2. StudentDetail 업데이트 (있는 경우)
            if (Detail != null)
            {
                Detail.UpdatedAt = DateTime.Now;

                // 기존 레코드 확인
                var existing = await DetailSvc.GetByStudentIdAsync(Student.StudentID);

                if (existing != null)
                {
                    // 업데이트
                    Detail.No = existing.No; // PK 유지
                    if (!await DetailSvc.UpdateAsync(Detail))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[StudentCardViewModel] 상세정보 갱신 0행: No={Detail.No}");
                        return false;
                    }
                }
                else
                {
                    // 신규 생성
                    Detail.StudentID = Student.StudentID;
                    await DetailSvc.CreateAsync(Detail);
                }
            }

            // 3. Enrollment는 StudentCard에서 직접 수정하지 않음
            // 학적 정보(학년, 반, 번호)는 별도 관리 화면에서만 변경
            // StudentCard에서는 읽기 전용으로만 표시

            IsChanged = false;
            return true;
        }
        catch (Exception ex)
        {
            // 호출부는 "저장에 실패했습니다" 만 띄운다 — 이유는 여기서 남기지 않으면 사라진다.
            NewSchool.Logging.Log.Error("StudentCardViewModel", $"학생 정보 저장 실패: {Student?.StudentID}", ex);
            return false;
        }
    }

    #endregion

    #region Methods - 사진 관리

    /// <summary>
    /// 사진 등록 (파일 선택기)
    /// </summary>
    public async Task<bool> AddPhotoAsync()
    {
        if (Student == null)
            return false;

        try
        {
            var photoPath = await PhotoSvc.PickAndSavePhotoAsync(Student.StudentID);

            if (!string.IsNullOrEmpty(photoPath))
            {
                Student.Photo = photoPath;
                PhotoImage = await PhotoSvc.LoadPhotoAsync(photoPath);
                IsChanged = true;
                return true;
            }

            // 여기까지 왔다 = 파일 선택기에서 아무것도 고르지 않았다(취소).
            return false;
        }
        catch (Exception ex)
        {
            // ⚠ 삼켜서 false 를 내면 호출부가 "사진 등록이 취소되었습니다" 라고 말한다 —
            //   실패를 취소라고 부르면 사용자는 다시 시도하지 않는다. 호출부의 catch 가
            //   이유까지 붙여 알리도록 그대로 올린다.
            NewSchool.Logging.Log.Error("StudentCardViewModel", $"사진 등록 실패: {Student?.StudentID}", ex);
            throw;
        }
    }

    /// <summary>
    /// 사진 삭제
    /// </summary>
    public async Task<bool> DeletePhotoAsync()
    {
        if (Student == null || string.IsNullOrEmpty(Student.Photo))
            return true;

        try
        {
            // ⚠ 결과를 버리면 파일은 그대로 남은 채 학생 카드에서 연결만 끊기고,
            //   사용자에게는 "사진이 삭제되었습니다" 로 보인다. 지우지 못했으면 연결도 둔다.
            if (!await PhotoSvc.DeletePhotoAsync(Student.Photo))
                return false;

            Student.Photo = string.Empty;
            PhotoImage = null;
            IsChanged = true;

            return true;
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("StudentCardViewModel", $"사진 삭제 실패: {Student?.StudentID}", ex);
            return false;
        }
    }

    #endregion

    #region Methods - 초기화

    /// <summary>
    /// 모든 정보 초기화 (사진 포함)
    /// </summary>
    public async Task<bool> ResetAllInfoAsync()
    {
        if (Student == null)
            return false;

        try
        {
            // 사진 삭제. 지우지 못했으면 초기화 자체를 멈춘다 — "모두 지웠다" 고 말해 놓고
            // 사진 파일만 남으면 그것이 어디에 남았는지 사용자가 알 길이 없다.
            if (!string.IsNullOrEmpty(Student.Photo) && !await PhotoSvc.DeletePhotoAsync(Student.Photo))
                return false;

            // 모든 정보 초기화
            ResetAllInfo();
            IsChanged = true;

            return true;
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("StudentCardViewModel", $"학생 정보 초기화 실패: {Student?.StudentID}", ex);
            return false;
        }
    }

    /// <summary>
    /// 모든 정보 초기화 (내부 메서드)
    /// </summary>
    private void ResetAllInfo()
    {
        if (Student == null)
            return;

        // 기본 정보 초기화 (이름과 ID는 유지)
        Student.Photo = string.Empty;
        Student.Sex = string.Empty;
        Student.BirthDate = null;
        Student.Phone = string.Empty;
        Student.Email = string.Empty;
        Student.Address = string.Empty;
        Student.Memo = string.Empty;

        // 상세 정보 초기화
        if (Detail != null)
        {
            Detail.FatherName = string.Empty;
            Detail.FatherPhone = string.Empty;
            Detail.FatherJob = string.Empty;
            Detail.MotherName = string.Empty;
            Detail.MotherPhone = string.Empty;
            Detail.MotherJob = string.Empty;
            Detail.GuardianName = string.Empty;
            Detail.GuardianPhone = string.Empty;
            Detail.GuardianRelation = string.Empty;
            Detail.FamilyInfo = string.Empty;
            Detail.Friends = string.Empty;
            Detail.Interests = string.Empty;
            Detail.Talents = string.Empty;
            Detail.CareerGoal = string.Empty;
            Detail.HealthInfo = string.Empty;
            Detail.Allergies = string.Empty;
            Detail.SpecialNeeds = string.Empty;
            Detail.Memo = string.Empty;
        }

        // 학적 변동은 기본값(1학년은 입학, 그 위는 진급)으로 되돌린다.
        if (Enrollment != null)
        {
            Enrollment.ApplyChange(EnrollmentChange.DefaultFor(Enrollment.Grade));
        }

        PhotoImage = null;
    }

    /// <summary>
    /// 새 학생으로 초기화
    /// </summary>
    public void Clear()
    {
        // 이벤트 해제
        if (Student != null)
            Student.PropertyChanged -= OnModelPropertyChanged;
        if (Detail != null)
            Detail.PropertyChanged -= OnModelPropertyChanged;
        if (Enrollment != null)
            Enrollment.PropertyChanged -= OnModelPropertyChanged;

        // ✅ 모델 초기화 - 빈 객체로 (바인딩 오류 방지)
        _student = new Student();
        _detail = new StudentDetail();
        _enrollment = null;

        PhotoImage = null;
        IsChanged = false;

        // 모든 프로퍼티 변경 알림
        OnPropertyChanged(string.Empty);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// StudentDetail이 없으면 생성
    /// </summary>
    private void EnsureDetailExists()
    {
        if (_detail == null && _student != null)
        {
            _detail = new StudentDetail
            {
                StudentID = _student.StudentID
            };
        }
    }

    /// <summary>
    /// 모델 속성 변경 이벤트 핸들러
    /// </summary>
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 편집 대상(Student/Detail)이 바뀔 때만 IsChanged 를 세운다.
        // Enrollment 는 읽기 전용이므로 여기서 플래그를 건드리면 안 된다
        // (예전에는 무조건 true 로 올린 뒤 Enrollment 분기에서 false 로 되돌려,
        //  편집 도중 학적 갱신이 들어오면 미저장 편집이 통째로 유실됐다).
        if (sender == Student || sender == Detail)
        {
            IsChanged = true;
        }

        // 특정 프로퍼티 변경 시 관련 계산 프로퍼티 업데이트
        if (sender == Student)
        {
            switch (e.PropertyName)
            {
                case nameof(Student.Name):
                    OnPropertyChanged(nameof(Name));
                    break;
                case nameof(Student.BirthDate):
                    OnPropertyChanged(nameof(Age));
                    break;
                case nameof(Student.Photo):
                    _ = ReloadPhotoAsync().ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            System.Diagnostics.Debug.WriteLine($"[StudentCardViewModel] {t.Exception?.InnerException?.Message}");
                    }, TaskContinuationOptions.OnlyOnFaulted);
                    break;
            }
        }
        else if (sender == Detail)
        {
            switch (e.PropertyName)
            {
                case nameof(StudentDetail.MotherPhone):
                case nameof(StudentDetail.FatherPhone):
                case nameof(StudentDetail.GuardianPhone):
                            break;
                case nameof(StudentDetail.HealthInfo):
                case nameof(StudentDetail.Allergies):
                case nameof(StudentDetail.SpecialNeeds):
                    OnPropertyChanged(nameof(HasSpecialConsiderations));
                    break;
            }
        }
        else if (sender == Enrollment)
        {
            // Enrollment 변경은 IsChanged를 발생시키지 않음 (읽기 전용)
            // 계산 프로퍼티만 업데이트 — IsChanged 는 그대로 둔다.
            switch (e.PropertyName)
            {
                case nameof(Enrollment.Grade):
                case nameof(Enrollment.Class):
                case nameof(Enrollment.Number):
                    OnPropertyChanged(nameof(ClassInfo));
                    break;
            }
        }
    }

    /// <summary>
    /// 사진 재로드 (내부용)
    /// </summary>
    private async Task ReloadPhotoAsync()
    {
        if (Student == null)
        {
            PhotoImage = null;
            return;
        }

        PhotoImage = await PhotoSvc.LoadPhotoAsync(Student.Photo);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        // 이벤트 해제
        if (Student != null)
            Student.PropertyChanged -= OnModelPropertyChanged;
        if (Detail != null)
            Detail.PropertyChanged -= OnModelPropertyChanged;
        if (Enrollment != null)
            Enrollment.PropertyChanged -= OnModelPropertyChanged;

        // 서비스 정리 — 바깥에서 받은 것은 건드리지 않는다(수명은 준 쪽 것이다).
        // 지연 생성이라 한 번도 안 쓴 ViewModel 은 여기서 놓아줄 것도 없다.
        if (!_ownsServices) return;

        _studentService?.Dispose();
        _studentDetailService?.Dispose();
        _enrollmentService?.Dispose();

        _studentService = null;
        _studentDetailService = null;
        _enrollmentService = null;
        _photoService = null;
    }

    #endregion
}
