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
public class StudentCardViewModel : NotifyPropertyChangedBase
{
    #region Fields

    private Student? _student;
    private StudentDetail? _detail;
    private Enrollment? _enrollment;
    private BitmapImage? _photoImage;
    private bool _isChanged = false;
    private bool _isLoading = false;

    // Services
    private readonly StudentService _studentService;
    private readonly StudentDetailService _studentDetailService;
    private readonly EnrollmentService _enrollmentService;
    private readonly PhotoService _photoService;

    #endregion

    #region Constructor

    public StudentCardViewModel()
    {
        _studentService = new StudentService(SchoolDatabase.DbPath);
        _studentDetailService = new StudentDetailService(SchoolDatabase.DbPath);
        _enrollmentService = new EnrollmentService();
        _photoService = new PhotoService();

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
            OnPropertyChanged(nameof(PrimaryContact));
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

    /// <summary>주 연락처 (우선순위: 어머니 → 아버지 → 보호자)</summary>
    public string PrimaryContact => Detail?.GetPrimaryContact() ?? string.Empty;

    /// <summary>특이사항 여부</summary>
    public bool HasSpecialConsiderations => Detail?.HasSpecialConsiderations() ?? false;

    #endregion

    #region Methods - 데이터 로드

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
            Enrollment = await _enrollmentService.GetCurrentEnrollmentAsync(studentId);

            // 4. 사진 로드
            if (Student != null)
            {
                PhotoImage = await _photoService.LoadPhotoAsync(Student.Photo);
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
            // 1. Student 업데이트
            Student.UpdatedAt = DateTime.Now;
            await _studentService.UpdateBasicInfoAsync(Student);

            // 2. StudentDetail 업데이트 (있는 경우)
            if (Detail != null)
            {
                Detail.UpdatedAt = DateTime.Now;

                // 기존 레코드 확인
                var existing = await _studentDetailService.GetByStudentIdAsync(Student.StudentID);

                if (existing != null)
                {
                    // 업데이트
                    Detail.No = existing.No; // PK 유지
                    await _studentDetailService.UpdateAsync(Detail);
                }
                else
                {
                    // 신규 생성
                    Detail.StudentID = Student.StudentID;
                    await _studentDetailService.CreateAsync(Detail);
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
            System.Diagnostics.Debug.WriteLine($"[StudentCardViewModel] SaveAsync 오류: {ex.Message}");
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
            var photoPath = await _photoService.PickAndSavePhotoAsync(Student.StudentID);

            if (!string.IsNullOrEmpty(photoPath))
            {
                Student.Photo = photoPath;
                PhotoImage = await _photoService.LoadPhotoAsync(photoPath);
                IsChanged = true;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentCardViewModel] AddPhotoAsync 오류: {ex.Message}");
            return false;
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
            await _photoService.DeletePhotoAsync(Student.Photo);

            Student.Photo = string.Empty;
            PhotoImage = null;
            IsChanged = true;

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentCardViewModel] DeletePhotoAsync 오류: {ex.Message}");
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
            // 사진 삭제
            if (!string.IsNullOrEmpty(Student.Photo))
            {
                await _photoService.DeletePhotoAsync(Student.Photo);
            }

            // 모든 정보 초기화
            ResetAllInfo();
            IsChanged = true;

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentCardViewModel] ResetAllInfoAsync 오류: {ex.Message}");
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

        // 학적 상태는 재학으로
        if (Enrollment != null)
        {
            Enrollment.Status = "재학";
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
        // 모델이 변경되면 IsChanged = true
        IsChanged = true;

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
                    _ = ReloadPhotoAsync();
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
                    OnPropertyChanged(nameof(PrimaryContact));
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
            // 계산 프로퍼티만 업데이트
            switch (e.PropertyName)
            {
                case nameof(Enrollment.Grade):
                case nameof(Enrollment.Class):
                case nameof(Enrollment.Number):
                    OnPropertyChanged(nameof(ClassInfo));
                    break;
            }
            
            // IsChanged를 false로 되돌림 (읽기 전용 모델이므로)
            IsChanged = false;
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

        PhotoImage = await _photoService.LoadPhotoAsync(Student.Photo);
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

        // 서비스 정리
        _studentService?.Dispose();
        _studentDetailService?.Dispose();
        _enrollmentService?.Dispose();
    }

    #endregion
}
