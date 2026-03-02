using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using System;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using WinRT.NewSchoolGenericHelpers;

namespace NewSchool.Controls
{
    /// <summary>
    /// StudentLog 상세 편집/생성 컨트롤
    /// 
    /// 주요 기능:
    /// 1. StudentLog 모델 편집 (기존 기록 수정)
    /// 2. 새 StudentLog 생성
    /// 3. 구조화된 활동 기록 입력
    /// 4. 활동 요약/학생부 초안 자동 생성
    /// 5. 바이트 카운터 (NEIS 기준)
    /// 6. 클립보드 복사
    /// </summary>
    public sealed partial class StudentLogBox : UserControl
    {
        #region Fields

        private StudentLog? _currentLog;
        private bool _isEditMode = false;
        private string _generatedText = string.Empty;

        #endregion

        #region Events

        /// <summary>저장 버튼 클릭 이벤트</summary>
        public event EventHandler<StudentLog>? LogSaved;

        /// <summary>취소 버튼 클릭 이벤트</summary>
        public event EventHandler? LogCancelled;

        #endregion

        #region Properties

        /// <summary>현재 편집 중인 StudentLog</summary>
        public StudentLog? CurrentLog => _currentLog;

        /// <summary>편집 모드 여부</summary>
        public bool IsEditMode => _isEditMode;

        #endregion

        #region Constructor

        public StudentLogBox()
        {
            this.InitializeComponent();
            InitializeDefaultValues();
        }

        #endregion

        #region Initialization

        /// <summary>기본값 초기화</summary>
        private void InitializeDefaultValues()
        {
            NumYear.Value = DateTime.Today.Year;
            CBoxSemester.SelectedIndex = DateTime.Today.Month <= 6 ? 0 : 1;
            DatePickerLog.Date = DateTimeOffset.Now;
            CBoxCategory.SelectedIndex = 0;
        }

        #endregion

        #region Public Methods - Load/Create

        /// <summary>
        /// 기존 StudentLog 로드 (편집 모드)
        /// </summary>
        public void LoadLog(StudentLog log)
        {
            _currentLog = log;
            _isEditMode = true;

            // UI에 데이터 바인딩
            NumYear.Value = log.Year;
            CBoxSemester.SelectedIndex = log.Semester - 1;

            // string Date → DateTimeOffset 변환
            DatePickerLog.Date = new DateTimeOffset(log.Date);


            CBoxCategory.SelectedIndex = (int)log.Category;
            TxtSubjectName.Text = log.SubjectName ?? string.Empty;
            ChkIsImportant.IsChecked = log.IsImportant;

            // 구조화된 필드
            TxtActivityName.Text = log.ActivityName ?? string.Empty;
            TxtTopic.Text = log.Topic ?? string.Empty;
            TxtDescription.Text = log.Description ?? string.Empty;
            TxtRole.Text = log.Role ?? string.Empty;
            TxtSkillDeveloped.Text = log.SkillDeveloped ?? string.Empty;
            TxtStrengthShown.Text = log.StrengthShown ?? string.Empty;
            TxtResultOrOutcome.Text = log.ResultOrOutcome ?? string.Empty;

            // 기존 방식
            TxtLog.Text = log.Log ?? string.Empty;
            TxtTag.Text = log.Tag ?? string.Empty;

            // 학생 정보 표시
            TxtStudentInfo.Text = $"학생 ID: {log.StudentID}";

            UpdateLogByteInfo();
        }

        /// <summary>
        /// 새 StudentLog 생성 (생성 모드)
        /// </summary>
        public void CreateNew(string studentId, string teacherId, int year, int semester)
        {
            _currentLog = new StudentLog
            {
                StudentID = studentId,
                TeacherID = teacherId,
                Year = year,
                Semester = semester,
                Date = DateTime.Now,
                Category = LogCategory.전체
            };
            _isEditMode = false;

            // UI 초기화
            ClearFields();

            NumYear.Value = year;
            CBoxSemester.SelectedIndex = semester - 1;
            DatePickerLog.Date = DateTimeOffset.Now;

            TxtStudentInfo.Text = $"학생 ID: {studentId}";
        }

        /// <summary>
        /// 카테고리 설정 및 잠금 (일괄 입력 모드용)
        /// </summary>
        public void SetCategory(LogCategory category, bool locked = false)
        {
            // ComboBoxItem의 Tag 값으로 찾기
            for (int i = 0; i < CBoxCategory.Items.Count; i++)
            {
                if (CBoxCategory.Items[i] is ComboBoxItem item &&
                    item.Tag is string tag &&
                    int.TryParse(tag, out int tagVal) &&
                    tagVal == (int)category)
                {
                    CBoxCategory.SelectedIndex = i;
                    break;
                }
            }
            CBoxCategory.IsEnabled = !locked;
        }

        /// <summary>
        /// 과목명 설정 및 잠금
        /// </summary>
        public void SetSubjectName(string subjectName, bool locked = false)
        {
            TxtSubjectName.Text = subjectName;
            TxtSubjectName.IsReadOnly = locked;
        }

        /// <summary>
        /// 학년도/학기 잠금 (일괄 입력 시 변경 불필요)
        /// </summary>
        public void LockYearSemester(bool locked = true)
        {
            NumYear.IsEnabled = !locked;
            CBoxSemester.IsEnabled = !locked;
        }

        /// <summary>
        /// 학생 정보 표시 숨김 (일괄 입력 시 학생 ID 불필요)
        /// </summary>
        public void HideStudentInfo()
        {
            TxtStudentInfo.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 입력 필드 초기화
        /// </summary>
        public void ClearFields()
        {
            TxtSubjectName.Text = string.Empty;
            ChkIsImportant.IsChecked = false;

            TxtActivityName.Text = string.Empty;
            TxtTopic.Text = string.Empty;
            TxtDescription.Text = string.Empty;
            TxtRole.Text = string.Empty;
            TxtSkillDeveloped.Text = string.Empty;
            TxtStrengthShown.Text = string.Empty;
            TxtResultOrOutcome.Text = string.Empty;

            TxtLog.Text = string.Empty;
            TxtTag.Text = string.Empty;

            TxtGeneratedText.Text = "여기에 생성된 요약 또는 초안이 표시됩니다.";
            _generatedText = string.Empty;
            BtnCopyToClipboard.IsEnabled = false;
        }

        #endregion

        #region UI Event Handlers

        /// <summary>카테고리 변경 시</summary>
        private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            // 교과활동/개인별세특이 아니면 과목명 비활성화
            var selectedIndex = CBoxCategory.SelectedIndex;
            bool showSubject = selectedIndex == 1 || selectedIndex == 2; // 교과활동, 개인별세특
            TxtSubjectName.IsEnabled = showSubject;

            if (!showSubject)
            {
                TxtSubjectName.Text = string.Empty;
            }
        }

        /// <summary>구조화된 필드 변경 시</summary>
        private void OnStructuredFieldChanged(object sender, TextChangedEventArgs e)
        {
            // 구조화된 필드가 하나라도 입력되면 생성 버튼 활성화
            bool hasStructuredData = !string.IsNullOrWhiteSpace(TxtActivityName.Text) ||
                                    !string.IsNullOrWhiteSpace(TxtTopic.Text) ||
                                    !string.IsNullOrWhiteSpace(TxtDescription.Text);

            BtnGenerateSummary.IsEnabled = hasStructuredData;
            BtnGenerateDraft.IsEnabled = hasStructuredData;
        }

        /// <summary>Log 텍스트 변경 시</summary>
        private void OnLogChanged(object sender, TextChangedEventArgs e)
        {
            UpdateLogByteInfo();
        }

        /// <summary>활동 요약 생성</summary>
        private void OnGenerateSummaryClick(object sender, RoutedEventArgs e)
        {
            var tempLog = CreateTempLogFromUI();
            
            if (tempLog.HasStructuredData())
            {
                _generatedText = tempLog.Summary;
                TxtGeneratedText.Text = _generatedText;
                BtnCopyToClipboard.IsEnabled = true;
            }
            else
            {
                ShowMessage("구조화된 데이터가 없습니다", "활동명, 주제, 활동 내용 중 하나 이상을 입력해주세요.");
            }
        }

        /// <summary>학생부 초안 생성</summary>
        private void OnGenerateDraftClick(object sender, RoutedEventArgs e)
        {
            var tempLog = CreateTempLogFromUI();
            
            if (tempLog.HasStructuredData())
            {
                _generatedText = tempLog.DraftSummary;
                TxtGeneratedText.Text = _generatedText;
                BtnCopyToClipboard.IsEnabled = true;
            }
            else
            {
                ShowMessage("구조화된 데이터가 없습니다", "활동명, 주제, 활동 내용 중 하나 이상을 입력해주세요.");
            }
        }

        /// <summary>클립보드에 복사</summary>
        private void OnCopyToClipboardClick(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_generatedText))
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(_generatedText);
                Clipboard.SetContent(dataPackage);

                ShowMessage("복사 완료", "클립보드에 복사되었습니다.");
            }
        }

        /// <summary>저장 버튼 클릭</summary>
        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_currentLog == null)
            {
                ShowMessage("오류", "저장할 로그가 없습니다.");
                return;
            }

            // UI 데이터를 _currentLog에 반영
            UpdateLogFromUI();

            // 유효성 검사
            if (string.IsNullOrWhiteSpace(_currentLog.StudentID))
            {
                ShowMessage("유효성 검사 실패", "학생 ID가 없습니다.");
                return;
            }

            // 기록 내용이나 구조화된 데이터 중 하나는 있어야 함
            bool hasLog = !string.IsNullOrWhiteSpace(_currentLog.Log);
            bool hasStructured = _currentLog.HasStructuredData();

            if (!hasLog && !hasStructured)
            {
                ShowMessage("유효성 검사 실패", "기록 내용 또는 구조화된 활동 기록 중 하나는 입력해야 합니다.");
                return;
            }

            // 이벤트 발생
            LogSaved?.Invoke(this, _currentLog);
        }

        /// <summary>취소 버튼 클릭</summary>
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            LogCancelled?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Helper Methods

        /// <summary>UI에서 임시 StudentLog 객체 생성 (미리보기용)</summary>
        private StudentLog CreateTempLogFromUI()
        {
            return new StudentLog
            {
                Year = (int)NumYear.Value,
                Semester = CBoxSemester.SelectedIndex + 1,
                Date = DatePickerLog.Date.LocalDateTime,
                Category = (LogCategory)CBoxCategory.SelectedIndex,
                SubjectName = TxtSubjectName.Text,
                ActivityName = TxtActivityName.Text,
                Topic = TxtTopic.Text,
                Description = TxtDescription.Text,
                Role = TxtRole.Text,
                SkillDeveloped = TxtSkillDeveloped.Text,
                StrengthShown = TxtStrengthShown.Text,
                ResultOrOutcome = TxtResultOrOutcome.Text,
                Log = TxtLog.Text,
                Tag = TxtTag.Text,
                IsImportant = ChkIsImportant.IsChecked ?? false
            };
        }

        /// <summary>UI 데이터를 _currentLog에 반영</summary>
        private void UpdateLogFromUI()
        {
            if (_currentLog == null) return;

            _currentLog.Year = (int)NumYear.Value;
            _currentLog.Semester = CBoxSemester.SelectedIndex + 1;
            _currentLog.Date = DatePickerLog.Date.LocalDateTime;
            _currentLog.Category = (LogCategory)CBoxCategory.SelectedIndex;
            _currentLog.SubjectName = TxtSubjectName.Text;
            _currentLog.IsImportant = ChkIsImportant.IsChecked ?? false;

            _currentLog.ActivityName = TxtActivityName.Text;
            _currentLog.Topic = TxtTopic.Text;
            _currentLog.Description = TxtDescription.Text;
            _currentLog.Role = TxtRole.Text;
            _currentLog.SkillDeveloped = TxtSkillDeveloped.Text;
            _currentLog.StrengthShown = TxtStrengthShown.Text;
            _currentLog.ResultOrOutcome = TxtResultOrOutcome.Text;

            _currentLog.Log = TxtLog.Text;
            _currentLog.Tag = TxtTag.Text;
        }

        /// <summary>바이트 정보 업데이트</summary>
        private void UpdateLogByteInfo()
        {
            int byteCount = CalculateNeisByte(TxtLog.Text);
            int charCount = TxtLog.Text?.Length ?? 0;
            TxtLogByteInfo.Text = $"{byteCount} Byte / {charCount} 자";
        }

        /// <summary>
        /// NEIS 바이트 계산 (한글 3바이트, 영문/숫자/기호 1바이트)
        /// </summary>
        private int CalculateNeisByte(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int byteCount = 0;
            foreach (char c in text)
            {
                // 한글 범위: AC00-D7A3 (가-힣)
                if (c >= 0xAC00 && c <= 0xD7A3)
                {
                    byteCount += 3;
                }
                // 한자 및 기타 유니코드 문자 (2바이트 이상)
                else if (c >= 0x3000)
                {
                    byteCount += 3;
                }
                // ASCII 범위 (영문, 숫자, 기호)
                else
                {
                    byteCount += 1;
                }
            }
            return byteCount;
        }

        /// <summary>메시지 표시 (간단한 알림)</summary>
        private async void ShowMessage(string title, string message)
        {
            await MessageBox.ShowAsync(message, title);
        }

        #endregion
    }
}
