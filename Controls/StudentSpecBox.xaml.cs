using System;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NewSchool.Helpers;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.Controls;

/// <summary>
/// StudentSpecBox - 학생 특이사항 편집 컨트롤
/// 
/// 기능:
/// - NEIS 바이트 계산 (한글 3바이트, 영문 1바이트)
/// - 자동 저장 (LostFocus 시)
/// - 유형별 바이트 제한 (진로활동: 2100, 기타: 1500)
/// - 맞춤법 검사 연동
/// </summary>
public sealed partial class StudentSpecBox : UserControl
{
    #region Fields

    private StudentSpecial? _special;
    private bool _isModified = false;
    private string _originalContent = string.Empty;

    #endregion

    #region Properties

    /// <summary>
    /// 현재 편집 중인 특이사항
    /// </summary>
    public StudentSpecial? Special
    {
        get => _special;
        set
        {
            _special = value;
            LoadSpecial();
        }
    }

    /// <summary>
    /// 수정 여부
    /// </summary>
    public bool IsModified => _isModified;

    #endregion

    #region Constructor

    public StudentSpecBox()
    {
        this.InitializeComponent();
    }

    #endregion

    #region Load Data

    /// <summary>
    /// 특이사항 데이터 로드
    /// </summary>
    private void LoadSpecial()
    {
        if (_special == null)
        {
            ClearUI();
            return;
        }

        // 헤더 정보 표시
        TxtYear.Text = _special.Year.ToString();
        TxtType.Text = _special.Type;
        TxtSubject.Text = _special.SubjectName;

        // 학기 표시 여부 (교과활동인 경우만)
        bool showSemester = _special.Type == "교과활동";
        TxtSemester.Visibility = showSemester ? Visibility.Visible : Visibility.Collapsed;
        TxtSemesterLabel.Visibility = showSemester ? Visibility.Visible : Visibility.Collapsed;

        // 학생 정보 (추후 Enrollment나 Student에서 가져오기)
        TxtStudent.Text = _special.StudentID;

        // 내용
        TxtContent.Text = _special.Content ?? string.Empty;
        _originalContent = TxtContent.Text;
        _isModified = false;

        // 마감 상태에 따른 UI 제어
        TxtContent.IsReadOnly = _special.IsFinalized;
        BtnSave.IsEnabled = !_special.IsFinalized;
        BtnDelete.IsEnabled = !_special.IsFinalized;

        // 바이트 정보 업데이트
        UpdateByteInfo();
    }

    /// <summary>
    /// UI 초기화
    /// </summary>
    private void ClearUI()
    {
        TxtYear.Text = DateTime.Today.Year.ToString();
        TxtType.Text = "";
        TxtSubject.Text = "";
        TxtStudent.Text = "";
        TxtContent.Text = "";
        TxtByteInfo.Text = "0 / 1500 Byte (0자)";
        TxtByteInfo.Foreground = new SolidColorBrush(Colors.Black);
        
        _originalContent = string.Empty;
        _isModified = false;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 텍스트 변경 시
    /// </summary>
    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_special == null)
            return;

        // 수정 여부 체크
        _isModified = TxtContent.Text != _originalContent;

        // 바이트 정보 업데이트
        UpdateByteInfo();
    }

    /// <summary>
    /// 포커스 잃었을 때 - 자동 저장 확인
    /// </summary>
    private async void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_isModified || _special == null)
            return;

        // 저장 여부 확인
        var result = await MessageBox.ShowAsync(
            "변경사항을 저장할까요?",
            "변경 사항 저장",
            MessageBoxButton.YesNo);

        if (result == MessageBoxResult.Yes)
        {
            await SaveAsync();
        }
    }

    /// <summary>
    /// 저장 버튼 클릭
    /// </summary>
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_special == null)
        {
            await MessageBox.ShowAsync("저장할 내용이 없습니다.", "알림");
            return;
        }

        if (!_isModified)
        {
            await MessageBox.ShowAsync("변경된 내용이 없습니다.", "알림");
            return;
        }

        await SaveAsync();
    }

    /// <summary>
    /// 삭제 버튼 클릭
    /// </summary>
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_special == null)
            return;

        // 삭제 확인
        var result = await MessageBox.ShowAsync(
            "학생부 기록 내용을 삭제하고 초기화합니다. 되돌릴 수 없습니다. 계속할까요?",
            "기록 초기화",
            MessageBoxButton.YesNo);

        if (result != MessageBoxResult.Yes)
            return;

        // 삭제 실행
        try
        {
            if (_special.No > 0)
            {
                using var service = new StudentSpecialService();
                await service.DeleteAsync(_special.No);
            }

            // UI 초기화
            TxtContent.Text = string.Empty;
            _originalContent = string.Empty;
            _isModified = false;
            
            UpdateByteInfo();

            await MessageBox.ShowAsync("기록이 삭제되었습니다.", "완료");
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"삭제 실패: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 맞춤법 검사 버튼 클릭
    /// </summary>
    private async void OnSpellCheckClick(object sender, RoutedEventArgs e)
    {
        string url = "https://nara-speller.co.kr/speller";

        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"웹 브라우저를 여는 데 실패했습니다: {ex.Message}", "오류");
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 저장 실행
    /// </summary>
    private async Task SaveAsync()
    {
        if (_special == null)
            return;

        try
        {
            // Content 업데이트
            _special.Content = TxtContent.Text;

            using var service = new StudentSpecialService();

            if (_special.No > 0)
            {
                // 기존 레코드 업데이트
                await service.UpdateAsync(_special);
            }
            else
            {
                // 새 레코드 생성
                _special.No = await service.CreateAsync(_special);
            }

            // 저장 성공
            _originalContent = TxtContent.Text;
            _isModified = false;

            await MessageBox.ShowAsync("저장되었습니다.", "완료");
        }
        catch (InvalidOperationException ex)
        {
            // 마감된 기록 수정 시도
            await MessageBox.ShowAsync(ex.Message, "마감된 기록");
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"저장 실패: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 바이트 정보 업데이트
    /// </summary>
    private void UpdateByteInfo()
    {
        if (_special == null)
            return;

        string text = TxtContent.Text;
        string type = _special.Type;

        // 바이트 계산
        int currentBytes = NeisHelper.CountByte(text);
        int maxBytes = NeisHelper.GetMaxBytes(type);
        int charCount = text.Length;

        // 텍스트 업데이트
        TxtByteInfo.Text = $"{currentBytes} / {maxBytes} Byte ({charCount}자)";

        // 색상 변경 (초과 시 빨간색)
        if (currentBytes > maxBytes)
        {
            TxtByteInfo.Foreground = new SolidColorBrush(Colors.Red);
        }
        else
        {
            TxtByteInfo.Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 강제 저장 (외부에서 호출)
    /// </summary>
    public async Task<bool> ForceSaveAsync()
    {
        if (!_isModified || _special == null)
            return false;

        try
        {
            await SaveAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 변경 사항 버리기
    /// </summary>
    public void DiscardChanges()
    {
        if (_special != null)
        {
            TxtContent.Text = _originalContent;
            _isModified = false;
            UpdateByteInfo();
        }
    }

    #endregion
}
