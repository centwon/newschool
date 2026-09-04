using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.ViewModels;

namespace NewSchool.Controls;

/// <summary>
/// 학생 상세 정보 카드 UserControl (간소화 버전)
/// 모든 로직은 ViewModel에 위임
/// </summary>
public sealed partial class StudentCard : UserControl
{
    #region Fields

    /// <summary>마지막 편집 후 이만큼 지나면 자동 저장한다.</summary>
    private const int AutoSaveDelayMs = 3000;

    private readonly DispatcherQueueTimer _autoSaveTimer;

    /// <summary>
    /// 자동 저장 실패를 이미 알렸는지. 디바운스가 계속 돌면서 같은 실패로 대화상자를
    /// 반복해 띄우지 않도록 한 번만 알리고, 저장에 성공하면 다시 알릴 수 있게 내린다.
    /// </summary>
    private bool _autoSaveFailureReported;

    #endregion

    #region Properties

    /// <summary>
    /// ViewModel (x:Bind용)
    /// </summary>
    public StudentCardViewModel ViewModel { get; }

    /// <summary>
    /// 변경 사항 여부 (외부 접근용)
    /// </summary>
    public bool IsChanged => ViewModel?.IsChanged ?? false;

    /// <summary>
    /// 현재 학생 ID (외부 접근용)
    /// </summary>
    public string StudentID => ViewModel?.StudentID ?? string.Empty;

    #endregion

    #region Events

    /// <summary>
    /// 학생 정보 변경 이벤트
    /// </summary>
    public event EventHandler? StudentChanged;

    #endregion

    #region Constructor

    /// <summary>
    /// 이 컨트롤이 ViewModel 을 <b>만들었는가</b>. 만들었으면 언로드될 때 놓아준다.
    /// DI 생성자로 받은 것은 수명이 준 쪽 것이라 건드리지 않는다.
    /// </summary>
    private readonly bool _ownsViewModel;

    public StudentCard()
    {
        this.InitializeComponent();

        // ViewModel 초기화
        ViewModel = new StudentCardViewModel();
        _ownsViewModel = true;

        // PropertyChanged 이벤트 구독
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        _autoSaveTimer = CreateAutoSaveTimer();

        // Unloaded 이벤트 (자동 저장)
        this.Unloaded += StudentCard_Unloaded;
    }

    // DI 지원 생성자
    public StudentCard(StudentCardViewModel viewModel)
    {
        this.InitializeComponent();

        ViewModel = viewModel;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        _autoSaveTimer = CreateAutoSaveTimer();

        // 기본 생성자와 동일하게 Unloaded 구독 (이중 호출 시 중복 방지는 호출측 책임)
        this.Unloaded -= StudentCard_Unloaded;
        this.Unloaded += StudentCard_Unloaded;
    }

    /// <summary>
    /// 입력이 멎고 <see cref="AutoSaveDelayMs"/> 뒤에 저장하는 디바운스 타이머를 만든다.
    ///
    /// <para>⚠ 예전에는 저장이 <c>Unloaded</c>(다른 학생 선택·페이지 이탈·대화상자 닫기)에서만
    /// 일어나서, 고친 내용을 두고 <b>앱을 그냥 닫으면 그대로 사라졌다</b>. 창 닫기 훅으로는
    /// 못 막는다 — 핸들러가 동기라 비동기 DB 저장이 프로세스 종료와 경합한다.
    /// 학급일지(<see cref="ClassDiaryBox"/>)와 같은 방식으로 저장 시점을 앞당긴다.</para>
    /// </summary>
    private DispatcherQueueTimer CreateAutoSaveTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(AutoSaveDelayMs);
        timer.IsRepeating = false;
        timer.Tick += async (_, _) => await SaveChangedAsync();
        return timer;
    }

    /// <summary>편집이 있을 때마다 자동 저장 타이머를 다시 센다(디바운스).</summary>
    private void RestartAutoSaveTimer()
    {
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    #endregion

    #region Event Handlers

    private void StudentCard_Unloaded(object sender, RoutedEventArgs e)
    {
        // 대기 중인 디바운스 저장은 아래에서 바로 처리하므로 타이머는 멈춘다
        _autoSaveTimer.Stop();

        // 자동 저장 시도.
        // ⚠ 저장이 끝난 뒤에 ViewModel 을 놓아준다 — SaveChangedAsync 가 그 서비스를 쓴다.
        //   여기서 바로 Dispose 하면 저장과 경합한다.
        _ = SaveChangedAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[StudentCard] {t.Exception?.InnerException?.Message}");

            // 우리가 만든 ViewModel 만 놓아준다. DI 생성자로 받은 것은 준 쪽 것이다.
            // 서비스가 지연 생성이라, 이 컨트롤이 다시 붙어 쓰이면 그때 다시 만들어진다.
            if (_ownsViewModel) ViewModel?.Dispose();
        });

        // 이벤트 구독 해제 (메모리 누수 방지)
        if (ViewModel != null)
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // IsChanged가 true가 되면 이벤트 발생
        if (e.PropertyName == nameof(StudentCardViewModel.IsChanged) && ViewModel.IsChanged)
        {
            StudentChanged?.Invoke(this, EventArgs.Empty);
        }

        // 미저장 편집이 남아 있는 동안은 어떤 항목이 바뀌든 저장 시각을 뒤로 민다.
        // 로드·초기화는 끝에서 IsChanged 를 내리므로 여기 걸리지 않는다.
        if (ViewModel.IsChanged)
        {
            RestartAutoSaveTimer();
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 학생 정보 로드
    /// </summary>
    public async Task LoadStudentAsync(string studentId)
    {
        try
        {
            await ViewModel.LoadStudentAsync(studentId);
            StudentChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"학생 정보 로드 오류: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 변경 사항 저장 (확인 메시지 포함) - 이벤트 핵들러
    /// </summary>
    private async void SaveAsync(object sender, RoutedEventArgs e)
    {
        await SaveAsync();
    }

    /// <summary>
    /// 변경 사항 저장 (확인 메시지 포함) - Public 메서드
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        if (!ViewModel.IsChanged)
            return true;

        try
        {
            bool success = await ViewModel.SaveAsync();

            if (success)
            {
                await MessageBox.ShowAsync("저장되었습니다.", "저장");
            }
            else
            {
                await MessageBox.ShowAsync("저장에 실패했습니다.", "오류");
            }

            return success;
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"저장 오류: {ex.Message}", "오류");
            return false;
        }
    }

    /// <summary>
    /// 자동 저장 (확인 없이).
    ///
    /// <para>⚠ <b>실패를 알려야 한다.</b> 이 자동 저장은 "앱을 그냥 닫아도 잃지 않게" 하려고
    /// 넣은 것인데, 실패를 <c>Debug.WriteLine</c> 으로만 흘리면 사용자는 저장된 줄 알고
    /// 앱을 닫아 결국 잃는다. <see cref="StudentCardViewModel.SaveAsync"/> 는 0행 갱신도
    /// <c>false</c> 로 내며 <c>IsChanged</c> 를 유지하는데(주석에 "호출부가 사용자에게 알릴 수
    /// 있게" 라고 적혀 있다), 정작 이 호출부가 삼키고 있었다.
    /// 학급일지(<see cref="ClassDiaryBox"/>)와 같은 처리로 맞춘다.</para>
    /// </summary>
    private async Task<bool> SaveChangedAsync()
    {
        _autoSaveTimer.Stop();

        if (!ViewModel.IsChanged)
            return true;

        try
        {
            if (await ViewModel.SaveAsync())
            {
                _autoSaveFailureReported = false;
                return true;
            }

            System.Diagnostics.Debug.WriteLine("[StudentCard] 자동 저장 0행 — 변경 표시를 유지한다");
            await ReportAutoSaveFailureAsync(
                "학생 정보를 저장하지 못했습니다.\n" +
                "고친 내용은 화면에 그대로 있으니 [저장] 을 눌러 다시 시도하세요.");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentCard] SaveChangedAsync 오류: {ex.Message}");
            NewSchool.Logging.Log.Error("StudentCard", "학생 정보 자동 저장 실패", ex);
            await ReportAutoSaveFailureAsync(
                "학생 정보를 저장하지 못했습니다.\n" +
                $"{ex.Message}\n\n" +
                "고친 내용은 화면에 그대로 있으니 [저장] 을 눌러 다시 시도하세요.");
            return false;
        }
    }

    /// <summary>같은 실패로 대화상자를 반복해 띄우지 않도록 한 번만 알린다.</summary>
    private async Task ReportAutoSaveFailureAsync(string message)
    {
        if (_autoSaveFailureReported) return;
        _autoSaveFailureReported = true;

        await MessageBox.ShowAsync(message, "자동 저장 실패");
    }

    /// <summary>
    /// 사진 등록
    /// </summary>
    private async void AddPhotoAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            bool success = await ViewModel.AddPhotoAsync();

            if (!success)
            {
                await MessageBox.ShowAsync("사진 등록이 취소되었습니다.", "알림");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"사진 등록 오류: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 사진 삭제
    /// </summary>
    private async void DeletePhotoAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await MessageBox.ShowYesNoAsync("사진을 삭제하시겠습니까?", "확인");

            if (result != ContentDialogResult.Primary)
                return;

            bool success = await ViewModel.DeletePhotoAsync();

            if (success)
            {
                await MessageBox.ShowAsync("사진이 삭제되었습니다.", "삭제");
            }
            else
            {
                await MessageBox.ShowAsync("사진 삭제에 실패했습니다.", "오류");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"사진 삭제 오류: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 모든 정보 초기화
    /// </summary>
    private async void ResetAllInfoAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await MessageBox.ShowYesNoAsync(
                $"{ViewModel.Name} 학생의 정보를 모두 삭제하고 초기화합니다.\n" +
                "되돌릴 수 없습니다. 계속할까요?",
                "학생 정보 삭제");

            if (result != ContentDialogResult.Primary)
                return;

            bool success = await ViewModel.ResetAllInfoAsync();

            if (success)
            {
                // ⚠ 화면만 비우는 것이 ResetAllInfoAsync 이고, DB 에 쓰는 것은 SaveAsync 다.
                //   그 결과를 버리면 저장이 실패해도 "초기화되었습니다" 라고 말하게 되고,
                //   다른 학생을 골랐다 돌아오면 옛 값이 그대로 보인다(사진은 이미 지워진 뒤다).
                //   형제인 PageStudentInfo 의 같은 흐름은 처음부터 확인하고 있었다.
                if (!await ViewModel.SaveAsync())
                {
                    await MessageBox.ShowAsync(
                        "화면은 비웠지만 저장하지 못했습니다. [저장] 으로 다시 시도하세요.",
                        "저장 실패");
                    return;
                }

                await MessageBox.ShowAsync("초기화되었습니다.", "초기화");
            }
            else
            {
                await MessageBox.ShowAsync("초기화에 실패했습니다.", "오류");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"초기화 오류: {ex.Message}", "오류");
        }
    }

    #endregion
}
