using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.ViewModels;

namespace NewSchool.Controls
{
    /// <summary>
    /// 학생 상세 정보 카드 UserControl (간소화 버전)
    /// 모든 로직은 ViewModel에 위임
    /// </summary>
    public sealed partial class StudentCard : UserControl
    {
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

        public StudentCard()
        {
            this.InitializeComponent();

            // ViewModel 초기화
            ViewModel = new StudentCardViewModel();

            // PropertyChanged 이벤트 구독
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Unloaded 이벤트 (자동 저장)
            this.Unloaded += StudentCard_Unloaded;
        }

        // DI 지원 생성자
        public StudentCard(StudentCardViewModel viewModel)
        {
            this.InitializeComponent();

            ViewModel = viewModel;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            // 기본 생성자와 동일하게 Unloaded 구독 (이중 호출 시 중복 방지는 호출측 책임)
            this.Unloaded -= StudentCard_Unloaded;
            this.Unloaded += StudentCard_Unloaded;
        }

        #endregion

        #region Event Handlers

        private void StudentCard_Unloaded(object sender, RoutedEventArgs e)
        {
            // 자동 저장 시도
            _ = SaveChangedAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    System.Diagnostics.Debug.WriteLine($"[StudentCard] {t.Exception?.InnerException?.Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);

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
        /// 자동 저장 (확인 없이)
        /// </summary>
        private async Task<bool> SaveChangedAsync()
        {
            if (!ViewModel.IsChanged)
                return true;

            try
            {
                return await ViewModel.SaveAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StudentCard] SaveChangedAsync 오류: {ex.Message}");
                return false;
            }
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
                    await ViewModel.SaveAsync();
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
}
