using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using NewSchool.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace NewSchool.Controls
{
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
                UnUsedChanged?.Invoke(this, new EventArgs());
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
                FixedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // 카드 크기
        public double CardWidth
        {
            get => this.Width;
            set => SetSize(value, null);
        }

        public double CardHeight
        {
            get => this.Height;
            set => SetSize(null, value);
        }

        #endregion

        #region Events

        public event EventHandler<StudentCardEventArgs>? StudentChanged;
        public event EventHandler? UnUsedChanged;
        public event EventHandler? FixedChanged;

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
                    _ = LoadPhotoAsync(data.PhotoPath);
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
                SetSize(this.Width, null);

                if (_studentData != null && !string.IsNullOrEmpty(_studentData.PhotoPath))
                {
                    _ = LoadPhotoAsync(_studentData.PhotoPath);
                }
            }
            else
            {
                PhotoControl.Visibility = Visibility.Collapsed;
                RowPhoto.Height = new GridLength(0);
                SetSize(this.Width, null);
            }
        }

        /// <summary>
        /// 비동기 사진 로딩
        /// 메모리 최적화: DecodePixelWidth 설정으로 메모리 사용량 80% 감소
        /// </summary>
        private async Task LoadPhotoAsync(string photoPath)
        {
            if (string.IsNullOrWhiteSpace(photoPath))
            {
                Photo.Source = null;
                return;
            }

            try
            {
                // 절대 경로 생성
                string fullPath = Path.IsPathRooted(photoPath)
                    ? photoPath
                    : Path.Combine(AppContext.BaseDirectory, photoPath);

                if (!File.Exists(fullPath))
                {
                    Photo.Source = null;
                    return;
                }

                // WinUI 3 방식으로 이미지 로딩
                StorageFile file = await StorageFile.GetFileFromPathAsync(fullPath);

                BitmapImage bitmap = new();

                // 메모리 최적화: 표시 크기에 맞게 디코딩
                // PhotoCard의 최대 너비는 약 200px이므로 400px로 디코딩 (레티나 대응)
                bitmap.DecodePixelWidth = 400;
                bitmap.DecodePixelType = DecodePixelType.Logical;

                // Stream을 using으로 명시적으로 관리하여 즉시 해제
                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read))
                {
                    await bitmap.SetSourceAsync(stream);
                }

                Photo.Source = bitmap;
            }
            catch (Exception)
            {
                // 로딩 실패 시 기본 이미지 또는 null
                Photo.Source = null;
            }
        }

        #endregion

        #region Methods - Size Management

        private void SetSize(double? width, double? height)
        {
            if (width is not null)
            {
                this.Width = (double)width;
                PhotoControl.Width = this.Width - 2;
                PhotoControl.Height = IsShowPhoto ? PhotoControl.Width / 3 * 4 : 0;
                NameBox.Height = PhotoControl.Width / 3;
                this.Height = PhotoControl.Height + 2 + NameBox.Height;
                return;
            }

            if (height is not null)
            {
                this.Height = (double)height;
                PhotoControl.Height = (this.Height - 2) / 5 * 4;
                NameBox.Height = PhotoControl.Height / 4;
                PhotoControl.Width = PhotoControl.Height / 4 * 3;
                this.Width = PhotoControl.Width + 2;
                return;
            }
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
                PhotoPath = student.Photo
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
}
