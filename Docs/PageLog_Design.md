# PageLog WinUI3 전환 설계 문서

## 개요
MySchool의 PageLog (학생 활동 기록 관리 페이지)를 WinUI3로 전환한 설계입니다.

## 주요 변경사항

### 1. UI (XAML) 변경사항

#### 네임스페이스
- WPF: `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"`
- WinUI3: 동일하지만 컨트롤 동작이 다름

#### 컨트롤 변경
- **Popup → Flyout**: 글자 크기 조절 팝업을 Flyout으로 변경
- **SymbolThemeFontFamily → FontIcon**: Segoe Fluent Icons 사용
- **Label → TextBlock**: WinUI3에는 Label이 없음
- **MouseWheel → PointerWheelChanged**: 이벤트명 변경

#### 스타일 개선
- `AppBarButtonStyle`: 버튼들을 AppBar 스타일로 통일
- `AccentButtonStyle`: 일괄입력 버튼을 강조
- `ThemeResource`: 테마 리소스 사용으로 다크모드 지원

#### 레이아웃
- `Spacing` 속성: StackPanel에 간격 자동 적용
- `Padding`: 일관된 여백 적용

### 2. Code-Behind (C#) 변경사항

#### 네임스페이스
```csharp
// WPF
using System.Windows;
using System.Windows.Controls;

// WinUI3
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
```

#### 주요 패턴 변경

##### 메시지 박스 → ContentDialog
```csharp
// WPF
MessageBox.Show("메시지", "제목", MessageBoxButton.YesNo);

// WinUI3
var dialog = new ContentDialog
{
    Title = "제목",
    Content = "메시지",
    PrimaryButtonText = "예",
    CloseButtonText = "아니오",
    XamlRoot = this.XamlRoot
};
await dialog.ShowAsync();
```

##### Window 소유자 → XamlRoot
```csharp
// WPF
dialog.Owner = Window.GetWindow(this);

// WinUI3
dialog.XamlRoot = this.XamlRoot;
```

##### 동기 → 비동기
```csharp
// WPF
void LoadLogs() { ... }

// WinUI3
async void LoadLogsAsync() { ... }
```

#### 아키텍처 개선

##### Repository 패턴 도입
```csharp
// 직접 DB 접근 (기존)
DB.GetStudentLog(...)

// Repository 사용 (신규)
await _logRepository.GetStudentLogsAsync(...)
```

##### Dependency Injection 준비
```csharp
private readonly StudentLogRepository _logRepository;
private readonly StudentSpecRepository _specRepository;
private readonly SettingsService _settings;
```

### 3. 인쇄 기능

WinUI3에서는 FlowDocument가 없으므로 다음 방법 중 선택:

1. **PrintHelper 사용** (권장)
   - Windows Community Toolkit의 PrintHelper
   - 또는 커스텀 PrintHelper 구현

2. **PDF 생성 후 인쇄**
   - iTextSharp 또는 QuestPDF 사용

3. **HTML → PDF 변환**
   - DinkToPdf 등 사용

## 필요한 추가 파일

### Models (NewSchool.Models)

#### LogCategory (Enum)
```csharp
public enum LogCategory
{
    전체,
    교과활동,
    자율활동,
    동아리활동,
    진로활동,
    봉사활동,
    종합의견,
    개인별세특,
    상담기록,
    기타
}
```

#### StudentLog (Class)
```csharp
public class StudentLog
{
    public int No { get; set; }
    public string Category { get; set; }
    public int User { get; set; }
    public int Year { get; set; }
    public int Semester { get; set; }
    public int Student { get; set; }
    public DateTime Date { get; set; }
    public string Topic { get; set; }
    public string Subject { get; set; }
    public int Subject_No { get; set; }
    public string Log { get; set; }
    public bool IsModified { get; set; } // UI에서 수정 여부 추적
}
```

#### StudentSpecial (Class)
```csharp
public class StudentSpecial
{
    public int No { get; set; }
    public int Year { get; set; }
    public int Semester { get; set; }
    public string Category { get; set; }
    public int Student { get; set; }
    public string Spec { get; set; }
}
```

#### AsignClass (Class)
```csharp
public class AsignClass
{
    public int Student { get; set; }
    public int Grade { get; set; }
    public int Class { get; set; }
    public int Number { get; set; }
    public string Name { get; set; }
}
```

### Repositories (NewSchool.Repositories)

#### StudentLogRepository
```csharp
public class StudentLogRepository
{
    public Task<List<int>> GetYearsAsync();
    public Task<List<StudentLog>> GetStudentLogsAsync(int studentId, int year, string category);
    public Task<List<int>> GetClassesAsync(int year, int grade);
    public Task<List<AsignClass>> GetClassStudentListAsync(int year, int grade, int classroom);
    public Task<int> UpdateLogAsync(StudentLog log);
    public Task DeleteLogAsync(int logNo);
}
```

#### StudentSpecRepository
```csharp
public class StudentSpecRepository
{
    public Task<List<StudentSpecial>> GetStudentSpecsAsync(int studentId, int year, string category);
    public Task<int> UpdateSpecAsync(StudentSpecial spec);
}
```

### Services (NewSchool.Services)

#### SettingsService (Singleton)
```csharp
public class SettingsService
{
    private static SettingsService? _instance;
    public static SettingsService Instance => _instance ??= new SettingsService();
    
    public int WorkYear { get; set; }
    public int WorkSemester { get; set; }
    public int UserHomeGrade { get; set; }
    public int UserHomeClass { get; set; }
    public int CurrentUser { get; set; }
    public string DatabasePath { get; set; }
}
```

### Dialogs (NewSchool.Dialogs)

#### StudentLogDialog
```csharp
public sealed partial class StudentLogDialog : ContentDialog
{
    public StudentLogDialog(LogCategory category, int grade, int classroom)
    {
        // 일괄입력 다이얼로그
    }
}
```

### Controls (NewSchool.Controls)

이미 존재한다고 가정:
- `ListStudent`: 학생 목록 컨트롤
- `LogListViewer`: 로그 목록 뷰어 (DataGrid 포함)
- `StudentSpecBox`: 학생부 특기사항 입력 박스

## TODO 항목

### 우선순위 높음

1. **LogListViewer 체크박스 처리**
   - `GetCheckedLogs()` 메서드 구현
   - 체크된 항목 추적 메커니즘 필요

2. **인쇄 기능 구현**
   - PrintHelper 또는 PDF 생성 방식 선택
   - 레이아웃 템플릿 작성

3. **Repository 구현**
   - StudentLogRepository
   - StudentSpecRepository
   - SQLite 연동 코드 작성

4. **SettingsService 구현**
   - ApplicationData.LocalSettings 사용
   - 또는 JSON 파일 기반 설정

### 우선순위 중간

5. **StudentLogDialog 구현**
   - 일괄입력 UI 및 로직

6. **오류 처리 개선**
   - 로깅 시스템 통합
   - 사용자 친화적 오류 메시지

7. **성능 최적화**
   - 대량 데이터 처리 시 가상화
   - 비동기 작업 최적화

### 우선순위 낮음

8. **테마 지원**
   - 라이트/다크 모드 전환
   - 사용자 정의 색상

9. **접근성**
   - 키보드 네비게이션
   - 스크린 리더 지원

10. **단위 테스트**
    - Repository 테스트
    - ViewModel 테스트 (MVVM 패턴 적용 시)

## Native AOT 고려사항

### 현재 구현에서 주의할 점

1. **Reflection 최소화**
   - Repository에서 동적 쿼리 생성 피하기
   - Source Generator 사용 검토

2. **SQLite 라이브러리**
   - Microsoft.Data.Sqlite (권장)
   - Native AOT 호환 확인

3. **JSON Serialization**
   - System.Text.Json 사용 (Source Generator 포함)
   - Newtonsoft.Json 피하기

4. **Dependency Injection**
   - 수동 DI 또는 AOT 호환 DI 컨테이너 사용

## 다음 단계

1. 필요한 Model, Repository, Service 파일 생성
2. Controls (ListStudent, LogListViewer, StudentSpecBox) 확인/구현
3. 인쇄 기능 구현 방식 결정
4. 데이터베이스 스키마 확인 및 Repository 구현
5. 통합 테스트
