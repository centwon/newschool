using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using Windows.ApplicationModel.DataTransfer;
using WinRT.NewSchoolGenericHelpers;

namespace NewSchool.Controls;

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
    private string _generatedText = string.Empty;

    /// <summary>연(또는 새로 만든) 그대로의 입력 내용. 여기서 달라지면 저장하지 않은 편집이다.</summary>
    private string _openedSnapshot = string.Empty;

    #endregion

    /// <summary>
    /// 적어 놓고 아직 저장하지 않은 것이 있는가 — 52차(닫을 때 축).
    ///
    /// <para>이 상자를 담은 창은 [저장] 을 눌러야 저장된다. 창을 X 로 닫으면 적은 것이
    /// 그대로 사라지므로, 닫기 전에 물어볼 수 있게 판정을 밖으로 연다.</para>
    /// </summary>
    public bool IsModified => Snapshot() != _openedSnapshot;

    /// <summary>입력칸을 한 줄로 이어 붙인 것. 무엇이 달라졌는지가 아니라 달라졌는지만 본다.</summary>
    private string Snapshot() => string.Join('\u001F',   // 입력에 나올 수 없는 구분자
        TxtLog.Text, TxtTag.Text, TxtSubjectName.Text,
        TxtActivityName.Text, TxtTopic.Text, TxtDescription.Text,
        TxtRole.Text, TxtSkillDeveloped.Text, TxtStrengthShown.Text, TxtResultOrOutcome.Text,
        (CBoxCategory.SelectedIndex).ToString(System.Globalization.CultureInfo.InvariantCulture),
        (ChkIsImportant.IsChecked ?? false).ToString());

    /// <summary>지금 화면 상태를 "저장된 것" 으로 삼는다(연 직후·저장 직후).</summary>
    public void MarkClean() => _openedSnapshot = Snapshot();

    #region Events

    /// <summary>저장 버튼 클릭 이벤트</summary>
    public event EventHandler<StudentLog>? LogSaved;

    /// <summary>취소 버튼 클릭 이벤트</summary>
    public event EventHandler? LogCancelled;

    #endregion

    // 내부 상태를 밖으로 열던 CurrentLog·IsEditMode 는 쓰는 곳이 없어 지웠다(39차) —
    // 편집 결과는 저장 이벤트로만 나간다.

    #region Constructor

    public StudentLogBox()
    {
        this.InitializeComponent();

        // 피커의 비동기 초기화가 끝나는 신호를 받아, 그때 밀린 선택을 맞춘다.
        YearSemPicker.YearSemesterChanged += OnPickerYearSemesterChanged;

        InitializeDefaultValues();
    }

    #endregion

    #region Initialization

    #region 학년도·학기 피커

    /// <summary>피커 초기화가 끝나면 맞춰 줄 값. 맞추고 나면 비운다.</summary>
    private (int Year, int Semester)? _pendingYearSemester;

    /// <summary>
    /// 피커에 학년도·학기를 맞춘다.
    ///
    /// <para>⚠ 그냥 <c>TrySelect</c> 만 부르면 안 된다. <see cref="YearSemesterPicker"/> 는
    /// <c>Loaded</c> 뒤에 <b>비동기로</b> 학년도 목록을 채우고, 그 끝에서 스스로
    /// <c>Settings.WorkYear</c> 를 고른다. 이 컨트롤의 <c>LoadLog</c>·<c>CreateNew</c> 는
    /// 창 생성자에서 불리므로 그보다 <b>먼저</b>다 — 지금 고르면 목록이 비어 있어 아무
    /// 일도 안 일어나고, 잠시 뒤 비동기 초기화가 엉뚱한 해로 덮어쓴다. 그래서 값을
    /// 적어 두었다가 초기화가 끝났다는 신호(첫 <c>YearSemesterChanged</c>)에 맞춘다.</para>
    /// </summary>
    private void RequestYearSemester(int year, int semester)
    {
        _pendingYearSemester = (year, semester);

        // 이미 초기화가 끝난 뒤라면(창을 재사용하는 경로) 지금 바로 먹는다.
        YearSemPicker.TrySelect(year, semester);
        if (YearSemPicker.Year == year && (semester <= 0 || YearSemPicker.Semester == semester))
            _pendingYearSemester = null;
    }

    private void OnPickerYearSemesterChanged(object? sender, YearSemesterChangedEventArgs e)
    {
        if (_pendingYearSemester is not { } want) return;   // 사용자가 바꾼 것이면 건드리지 않는다

        _pendingYearSemester = null;
        YearSemPicker.TrySelect(want.Year, want.Semester);
    }

    /// <summary>
    /// 저장에 쓸 학년도. 피커가 아직·영영 아무것도 못 고른 경우(학교 코드 미설정 등)
    /// 0 을 돌려주는데, 그대로 저장하면 <b>어느 목록에도 안 나오는 기록</b>이 된다.
    /// 그럴 때는 원래 값을 지킨다.
    /// </summary>
    private int EffectiveYear(int fallback)
    {
        int v = YearSemPicker.Year;
        if (v > 0) return v;
        return fallback > 0 ? fallback : Settings.WorkYear.Value;
    }

    /// <inheritdoc cref="EffectiveYear"/>
    private int EffectiveSemester(int fallback)
    {
        int v = YearSemPicker.Semester;
        if (v is 1 or 2) return v;
        return fallback is 1 or 2 ? fallback : DateTimeHelper.SemesterOf(DateTime.Today);
    }

    #endregion

    /// <summary>기본값 초기화</summary>
    private void InitializeDefaultValues()
    {
        // 학기 규칙은 DateTimeHelper 한 곳에서만 정한다(여기 있던 `Month <= 6` 은
        // 7·8월과 1·2월에 학기를 뒤집었다 — 3~8월이 1학기다).
        RequestYearSemester(Settings.WorkYear.Value, DateTimeHelper.SemesterOf(DateTime.Today));
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

        // UI에 데이터 바인딩
        RequestYearSemester(log.Year, log.Semester);

        // string Date → DateTimeOffset 변환
        DatePickerLog.Date = new DateTimeOffset(log.Date);


        CBoxCategory.SelectedIndex = (int)log.Category;
        // 교과활동/개인별세특이면 과목 필드 표시
        bool showSubject = log.Category == LogCategory.교과활동 || log.Category == LogCategory.개인별세특;
        var vis = showSubject ? Visibility.Visible : Visibility.Collapsed;
        TxtSubjectName.Visibility = vis;
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

        // 기록 내용
        TxtLog.Text = log.Log ?? string.Empty;
        TxtTag.Text = log.Tag ?? string.Empty;

        // 구조화된 데이터가 있으면 Expander 자동 펼침
        if (log.HasStructuredData())
        {
            ExpanderStructured.IsExpanded = true;
        }

        UpdateLogByteInfo();

        // 첨부는 저장된 기록에만 있다. 화면을 먼저 비워 두고 뒤늦게 채운다 —
        // 이 메서드는 동기라 여기서 기다릴 수 없다.
        FileList.LoadFiles(System.Array.Empty<StudentLogFile>());
        if (log.No > 0) _ = LoadAttachmentsAsync(log.No);

        MarkClean();   // 여기까지가 "연 그대로" 다(52차)
    }

    /// <summary>
    /// 저장된 첨부를 읽어 목록에 올린다.
    ///
    /// <para>읽기에 실패해도 편집을 막지 않는다 — 첨부는 곁다리이고, 여기서 예외를 올리면
    /// 기록을 고치러 온 사용자가 창을 못 쓰게 된다. 대신 목록을 비워 두고 알린다.</para>
    /// </summary>
    private async Task LoadAttachmentsAsync(int logNo)
    {
        try
        {
            using var repo = new Repositories.StudentLogFileRepository(SchoolDatabase.DbPath);
            FileList.LoadFiles(await repo.GetByLogAsync(logNo));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentLogBox] 첨부 로드 실패: {ex.Message}");
            await UserErrorReporter.ReportAsync("첨부파일 목록 읽기", ex);
        }
    }

    /// <summary>
    /// 저장할 때 반영해야 할 첨부 변경.
    ///
    /// <para>창(<see cref="Dialogs.StudentLogDialog"/>)이 기록을 저장한 <b>뒤</b>,
    /// 그때 생긴 <c>No</c> 로 <see cref="Services.StudentLogAttachments.ApplyAsync"/> 에 넘긴다.</para>
    /// </summary>
    public (IReadOnlyList<StudentLogFile> ToDelete, IReadOnlyList<string> NewPaths) PendingAttachments
        => (FileList.FilesToDelete, FileList.NewFilePaths);

    /// <summary>첨부 반영이 끝났음을 알린다(같은 창에서 계속 편집할 때 두 번 반영되지 않게).</summary>
    public void MarkAttachmentsApplied() => FileList.MarkApplied();

    /// <summary>
    /// 첨부 칸을 감춘다. 학급 일괄 입력에서 쓴다 — 그 창은 같은 내용을 여럿에게 한꺼번에
    /// 넣는 자리라, 첨부를 반 전체에 복제하는 것이 무엇을 뜻하는지부터 정해야 한다.
    /// 정하지 않은 채 칸만 띄우면 사용자는 붙였다고 믿고 아무 일도 일어나지 않는다.
    /// </summary>
    public void HideAttachments()
    {
        FileList.LoadFiles(Array.Empty<StudentLogFile>());
        FileList.Visibility = Visibility.Collapsed;
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

        // UI 초기화
        ClearFields();

        RequestYearSemester(year, semester);
        DatePickerLog.Date = DateTimeOffset.Now;

        // 새 기록은 아직 딸린 첨부가 없다. 앞 기록의 목록이 남지 않게 비운다.
        FileList.LoadFiles(Array.Empty<StudentLogFile>());

        MarkClean();   // 빈 화면이 "연 그대로" 다(52차)
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
        TxtSubjectName.Visibility = Visibility.Visible;
        TxtSubjectName.Text = subjectName;
        TxtSubjectName.IsReadOnly = locked;
    }

    /// <summary>
    /// 학년도/학기 잠금 (일괄 입력 시 변경 불필요)
    /// </summary>
    public void LockYearSemester(bool locked = true)
    {
        YearSemPicker.IsEnabled = !locked;
    }

    // 학생 ID 를 보여 주던 줄(TxtStudentInfo)과 그것을 감추던 HideStudentInfo 는 지웠다.
    // 창 제목 아래에 이미 "학생: 홍길동" 이 있어 중복이었고, 사람이 읽을 일 없는
    // 내부 식별자였다.

    /// <summary>
    /// 입력 필드 초기화
    /// </summary>
    public void ClearFields()
    {
        TxtSubjectName.Text = string.Empty;
        ChkIsImportant.IsChecked = false;

        // 활동 상세 항목 Expander 닫기
        ExpanderStructured.IsExpanded = false;

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
        var selectedIndex = CBoxCategory.SelectedIndex;
        bool showSubject = selectedIndex == 1 || selectedIndex == 2; // 교과활동, 개인별세특
        var vis = showSubject ? Visibility.Visible : Visibility.Collapsed;
        TxtSubjectName.Visibility = vis;

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
            ShowMessage("활동 상세 항목 필요", "활동명, 주제, 활동 내용 중 하나 이상을 입력해주세요.");
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
            ShowMessage("활동 상세 항목 필요", "활동명, 주제, 활동 내용 중 하나 이상을 입력해주세요.");
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
            ShowMessage("유효성 검사 실패", "기록 내용 또는 활동 상세 항목 중 하나는 입력해야 합니다.");
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
            Year = EffectiveYear(_currentLog?.Year ?? 0),
            Semester = EffectiveSemester(_currentLog?.Semester ?? 0),
            Date = (DatePickerLog.Date ?? DateTimeOffset.Now).LocalDateTime,
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

        _currentLog.Year = EffectiveYear(_currentLog.Year);
        _currentLog.Semester = EffectiveSemester(_currentLog.Semester);
        _currentLog.Date = (DatePickerLog.Date ?? DateTimeOffset.Now).LocalDateTime;
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
