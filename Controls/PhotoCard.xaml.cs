using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using NewSchool.Models;
using Windows.Storage;
using Windows.Storage.Streams;

namespace NewSchool.Controls;

/// <summary>
/// 학생 좌석 카드 - WinUI 3 버전
/// </summary>
public sealed partial class PhotoCard : UserControl
{
    #region Fields & Properties

    public int No { get; set; }

    // 행/열 위치
    public int Row { get; set; }
    public int Col { get; set; }

    // 사진 비동기 로딩 경합 방지용 토큰 — 새 요청이 들어올 때마다 증가시켜
    // 늦게 끝나는 이전 디코딩이 Photo.Source 를 덮어쓰지 못하게 한다
    private int _photoLoadToken = 0;

    // 사진 표시 여부
    private bool _isShowPhoto = false;
    public bool IsShowPhoto
    {
        get => _isShowPhoto;
        set => ShowPhoto(value);
    }

    // 학생 정보 (Enrollment + Student)
    private StudentCardData? _studentData = null;
    public StudentCardData? StudentData
    {
        get => _studentData;
        set
        {
            // 미사용 좌석에는 학생을 앉히지 않는다.
            //
            // ⚠ 이 가드가 없으면 조용히 깨진다. 아래 OnStudentChanged 는 IsUnUsed 일 때
            // 이벤트를 발생시키지 않는데, 그 이벤트가 바로 "같은 학생이 다른 자리에 있으면
            // 지우는" 중복 제거를 돌린다. 그래서 미사용 자리에 학생을 놓으면 원래 자리와
            // 미사용 자리에 **같은 학생이 둘** 남고, 저장하면 그대로 DB 에 들어간다.
            // 다음에 불러올 때도 미사용을 먼저 세우고 학생을 넣는 순서라 계속 되살아났다.
            if (value != null && _isUnUsed) return;

            _studentData = value;
            SetStudent(value);
            OnStudentChanged();
        }
    }

    // 미사용 좌석
    private bool _isUnUsed;
    public bool IsUnUsed
    {
        get => _isUnUsed;
        set
        {
            _isUnUsed = value;
            SetUnUsedStyle(value);
            if (MenuSeatDisable != null) MenuSeatDisable.IsChecked = value;
            UnUsedChanged?.Invoke(this, new EventArgs());
        }
    }

    // 미표시 좌석 (인쇄 시 비표시)
    private bool _isHidden;
    public bool IsHidden
    {
        get => _isHidden;
        set
        {
            _isHidden = value;
            SetHiddenStyle(value);
            if (MenuSeatHidden != null) MenuSeatHidden.IsChecked = value;
            HiddenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // 지정 좌석 (고정)
    private bool _isFixed;
    public bool IsFixed
    {
        get => _isFixed;
        set
        {
            _isFixed = value;
            if (MenuSeatFixed != null) MenuSeatFixed.IsChecked = value;
            FixedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // 카드 크기
    public double CardWidth
    {
        get => this.Width;
        set => SetSize(value);
    }

    // 높이로 크기를 정하는 CardHeight 는 지웠다(2026-08-30) — 읽는 곳도 넣는 곳도 없었다.
    // 좌석은 늘 너비로 정한다(PageSeats 가 CardWidth 만 넣는다). 높이는 사진 비율(3:4)과
    // 이름칸에서 따라 나오므로, 둘 다 열어 두면 어느 쪽이 기준인지 흐려진다.

    #endregion

    #region Events

    public event EventHandler<StudentCardEventArgs>? StudentChanged;
    public event EventHandler? UnUsedChanged;
    public event EventHandler? FixedChanged;
    public event EventHandler? HiddenChanged;

    #endregion

    #region Constructor

    public PhotoCard()
    {
        this.InitializeComponent();
        
        // 💡 중요: UserControl 레벨에서 이벤트 핸들링
        this.DragStarting += PhotoCard_DragStarting;
        this.DragOver += PhotoCard_DragOver;
        this.Drop += PhotoCard_Drop;
    }

    /// <summary>
    /// DragStarting 이벤트 - UserControl 내부에서 데이터 설정
    /// </summary>
    private void PhotoCard_DragStarting(Microsoft.UI.Xaml.UIElement sender, Microsoft.UI.Xaml.DragStartingEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[PhotoCard DragStarting] Row:{Row}, Col:{Col}, Student:{StudentData?.Name}");
        
        if (StudentData != null)
        {
            e.Data.Properties.Add("StudentData", StudentData);
            e.Data.Properties.Add("SourceRow", Row);
            e.Data.Properties.Add("SourceCol", Col);
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            e.AllowedOperations = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move | 
                                 Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;

            // 🎨 드래그 시각적 표시 커스터마이징
            if (e.DragUI != null)
            {
                // 텍스트 설정 (번호와 이름 사이 공백 최소화)
                e.Data.SetText($"{StudentData.Name}({StudentData.Number})");
                e.DragUI.SetContentFromDataPackage();
            }
        }
    }

    /// <summary>
    /// DragOver 이벤트 - UserControl 내부에서 처리 후 외부로 전파
    /// </summary>
    private void PhotoCard_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[PhotoCard DragOver] Row:{Row}, Col:{Col}");
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy | 
                              Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        e.Handled = false; // 외부로 전파
    }

    /// <summary>
    /// Drop 이벤트 - UserControl 내부에서 처리 후 외부로 전파
    /// </summary>
    private void PhotoCard_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[PhotoCard Drop] Row:{Row}, Col:{Col}");
        e.Handled = false; // 외부 PageSeats의 핸들러로 전파
    }

    #endregion

    #region Methods - Student Management

    /// <summary>
    /// 학생 정보 교체 (이벤트 발생 없이)
    /// </summary>
    public void ReplaceStudent(StudentCardData? studentData)
    {
        _studentData = studentData;
        SetStudent(studentData);
    }

    private void SetStudent(StudentCardData? data)
    {
        // 이전 비동기 로딩이 끝나서 늦게 도착해도 무시되도록 토큰을 먼저 증가
        int myToken = ++_photoLoadToken;

        if (data == null)
        {
            Photo.Source = null;
            TBName.Text = string.Empty;
        }
        else
        {
            TBName.Text = $"{data.Name}({data.Number})";
            if (_isShowPhoto)
            {
                _ = LoadPhotoAsync(data.PhotoPath, myToken).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        System.Diagnostics.Debug.WriteLine($"[PhotoCard] {t.Exception?.InnerException?.Message}");
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            else
            {
                // 사진 비표시 모드에서도 잔여 이미지 제거
                Photo.Source = null;
            }
        }
    }

    private void OnStudentChanged()
    {
        if (IsUnUsed) { return; }
        StudentChanged?.Invoke(this, new StudentCardEventArgs(Row, Col, _studentData));
    }

    #endregion

    #region Methods - Photo Management

    private void ShowPhoto(bool isShowPhoto)
    {
        _isShowPhoto = isShowPhoto;

        if (_isShowPhoto)
        {
            PhotoControl.Visibility = Visibility.Visible;
            RowPhoto.Height = GridLength.Auto;
            SetSize(this.Width);

            if (_studentData != null && !string.IsNullOrEmpty(_studentData.PhotoPath))
            {
                int myToken = ++_photoLoadToken;
                _ = LoadPhotoAsync(_studentData.PhotoPath, myToken).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        System.Diagnostics.Debug.WriteLine($"[PhotoCard] {t.Exception?.InnerException?.Message}");
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        else
        {
            PhotoControl.Visibility = Visibility.Collapsed;
            RowPhoto.Height = new GridLength(0);
            SetSize(this.Width);
        }
    }

    /// <summary>
    /// 비동기 사진 로딩
    /// 메모리 최적화: DecodePixelWidth 설정으로 메모리 사용량 80% 감소
    /// </summary>
    private async Task LoadPhotoAsync(string photoPath, int token)
    {
        // 토큰이 최신이 아니면(이미 다른 학생으로 변경됨) 즉시 종료
        if (token != _photoLoadToken) return;

        if (string.IsNullOrWhiteSpace(photoPath))
        {
            if (token == _photoLoadToken) Photo.Source = null;
            return;
        }

        try
        {
            // 절대 경로 생성 (저장 기준인 UserDataPath 기준 — PhotoService 와 동일하게 해석)
            string fullPath = NewSchool.Services.PhotoService.ResolveFullPath(photoPath) ?? "";

            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                if (token == _photoLoadToken) Photo.Source = null;
                return;
            }

            // WinUI 3 방식으로 이미지 로딩
            StorageFile file = await StorageFile.GetFileFromPathAsync(fullPath);
            if (token != _photoLoadToken) return; // 파일 열기 도중 학생 변경됨

            BitmapImage bitmap = new();

            // 메모리 최적화: 고정 400px 대신 실제 카드 표시 폭에 맞춘 적응형 디코딩.
            // 비트맵 메모리는 폭²에 비례하므로, 표시 크기의 2배(고DPI 대비)로만 디코딩한다.
            // 예) 기본 80px 카드 → ~160px 디코딩 (기존 400px 대비 약 6배 절감)
            double displayWidth = PhotoControl.Width;
            if (double.IsNaN(displayWidth) || displayWidth <= 0)
                displayWidth = double.IsNaN(this.Width) ? 80 : this.Width;
            bitmap.DecodePixelWidth = Math.Clamp((int)(displayWidth * 2), 120, 400);
            bitmap.DecodePixelType = DecodePixelType.Logical;

            // Stream을 using으로 명시적으로 관리하여 즉시 해제
            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read))
            {
                if (token != _photoLoadToken) return; // 디코딩 직전 학생 변경됨
                await bitmap.SetSourceAsync(stream);
            }

            // 디코딩 완료 시점에 한 번 더 검증 — 늦게 끝난 stale 응답이 최신 사진을 덮어쓰지 않도록
            if (token != _photoLoadToken) return;
            Photo.Source = bitmap;
        }
        catch (Exception)
        {
            // 로딩 실패 시 기본 이미지 또는 null (단, 토큰이 최신일 때만)
            if (token == _photoLoadToken) Photo.Source = null;
        }
    }

    #endregion

    #region Methods - Size Management

    /// <summary>
    /// 너비를 정하면 나머지가 따라 나온다 — 사진은 3:4, 이름칸은 너비의 1/3.
    ///
    /// <para>예전에는 높이로도 정할 수 있었다(<c>SetSize(null, height)</c>). 그 갈래는
    /// <c>CardHeight</c> 하나만 쓰던 길인데 그것도 부르는 곳이 없어 함께 걷었다.
    /// 기준이 하나면 둘이 어긋날 일도 없다.</para>
    /// </summary>
    private void SetSize(double width)
    {
        this.Width = width;
        PhotoControl.Width = this.Width - 2;
        PhotoControl.Height = IsShowPhoto ? PhotoControl.Width / 3 * 4 : 0;
        NameBox.Height = PhotoControl.Width / 3;
        this.Height = PhotoControl.Height + 2 + NameBox.Height;
    }

    #endregion

    #region Methods - Style Management

    private void SetUnUsedStyle(bool value)
    {
        if (value)
        {
            this.StudentData = null;
            this.Photo.Source = null;
            TBName.Text = string.Empty;
            BrdrOutLine.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.AntiqueWhite);
        }
        else
        {
            BrdrOutLine.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.CornflowerBlue);
        }
    }

    private void SetHiddenStyle(bool value)
    {
        this.Opacity = value ? 0.35 : 1.0;
    }

    #endregion

    #region Event Handlers - Context Menu

    private void MenuSeatDisable_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem menuItem)
        {
            IsUnUsed = menuItem.IsChecked;
            BrdrOutLine.BorderBrush = menuItem.IsChecked
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.AntiqueWhite)
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.CornflowerBlue);
        }
    }

    private void MenuSeatFixed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem menuItem)
        {
            if (StudentData is null)
            {
                menuItem.IsChecked = false;
            }
            IsFixed = menuItem.IsChecked;
        }
    }

    private void MenuSeatHidden_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem menuItem)
        {
            IsHidden = menuItem.IsChecked;
        }
    }

    #endregion
}

#region Supporting Classes

/// <summary>
/// 학생 카드 데이터 (Enrollment + Student 조합)
/// </summary>
public class StudentCardData
{
    public string StudentID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Number { get; set; }
    public int Grade { get; set; }
    public int Class { get; set; }
    public string PhotoPath { get; set; } = string.Empty;

    /// <summary>성별 ("남"/"여") — 남녀 교차 짝 옵션용</summary>
    public string Sex { get; set; } = string.Empty;

    /// <summary>
    /// Enrollment + Student에서 생성
    /// </summary>
    public static StudentCardData FromEnrollment(Enrollment enrollment, Student student)
    {
        return new StudentCardData
        {
            StudentID = enrollment.StudentID,
            Name = student.Name,
            Number = enrollment.Number,
            Grade = enrollment.Grade,
            Class = enrollment.Class,
            PhotoPath = student.Photo,
            Sex = student.Sex ?? string.Empty
        };
    }
}

/// <summary>
/// 학생 카드 이벤트 인자
/// </summary>
public class StudentCardEventArgs : EventArgs
{
    public int Row { get; }
    public int Col { get; }
    public StudentCardData? StudentData { get; }

    public StudentCardEventArgs(int row, int col, StudentCardData? studentData)
    {
        Row = row;
        Col = col;
        StudentData = studentData;
    }
}

#endregion
