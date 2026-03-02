# PageSeats - 좌석 배치 페이지 가이드 (Updated)

## 📋 개요

**PageSeats**는 학급 좌석 배치를 위한 WinUI 3 Page입니다.
- **StudentService** 및 **EnrollmentService** 사용
- 실제 DB 데이터 연동
- 드래그 앤 드롭 좌석 배치
- 자동 배정 및 애니메이션

---

## 🔄 주요 변경 사항

### **1. Service 계층 사용 ✅**
```csharp
// 기존 (Repository 직접 접근)
var enrollments = await enrollmentRepo.GetByClassAsync(...);

// 신규 (Service 사용)
var roster = await enrollmentService.GetClassRosterAsync(...);
```

### **2. StudentFullInfo 활용**
```csharp
// EnrollmentService가 Student + Enrollment를 조합하여 반환
public class StudentFullInfo
{
    public string StudentID { get; set; }
    public string Name { get; set; }
    public int Grade { get; set; }
    public int Class { get; set; }
    public int Number { get; set; }
    public string Status { get; set; }
    // ...
}
```

### **3. DB 경로 설정**
```csharp
private void InitializeServices()
{
    dbPath = Path.Combine(AppContext.BaseDirectory, "Data", "school.db");
    enrollmentService = new EnrollmentService(dbPath);
    studentService = new StudentService(dbPath);
}
```

---

## 🎯 사용법

### **1. MainWindow에서 네비게이션**
```csharp
private void NavigateToSeats()
{
    ContentFrame.Navigate(typeof(NewSchool.Pages.PageSeats));
}
```

### **2. 학급 선택**
- 학년 선택 → 자동으로 해당 학년의 학급 목록 로드
- 학급 선택 → `EnrollmentService.GetClassRosterAsync()` 호출하여 학생 목록 로드

### **3. 좌석 배치**
1. **초기화**: "줄"과 "짝" 설정 후 초기화 버튼 클릭
2. **수동 배치**: 학생 목록에서 좌석으로 드래그 앤 드롭
3. **자동 배정**: 자동배정 버튼 클릭 → 애니메이션과 함께 랜덤 배치

---

## 📊 데이터 흐름

```
┌─────────────────────┐
│  CBoxRooms_Changed  │
└──────────┬──────────┘
           │
           v
┌─────────────────────────────────────────┐
│ enrollmentService.GetClassRosterAsync() │
│ - Student + Enrollment JOIN             │
│ - StudentFullInfo[] 반환                │
└──────────┬──────────────────────────────┘
           │
           v
┌───────────────────────────────────┐
│ studentService.GetBasicInfoAsync() │
│ - Photo 경로 조회                  │
└──────────┬────────────────────────┘
           │
           v
┌────────────────────────┐
│  StudentCardData 생성  │
│  - StudentList 표시    │
└────────────────────────┘
```

---

## 🔌 Service 메서드 사용

### **학급 목록 조회**
```csharp
var classList = await enrollmentService.GetClassListAsync(
    schoolCode: "7001234",
    year: 2025,
    grade: 2
);
// 반환: [1, 2, 3, 4, 5] (해당 학년의 반 번호들)
```

### **학급 명부 조회**
```csharp
var roster = await enrollmentService.GetClassRosterAsync(
    schoolCode: "7001234",
    year: 2025,
    grade: 2,
    classNo: 3
);
// 반환: StudentFullInfo[] (2학년 3반 학생 목록)
```

### **학생 사진 조회**
```csharp
var student = await studentService.GetBasicInfoAsync(studentId);
string photoPath = student?.Photo ?? "";
```

---

## ⚙️ Settings 통합 (TODO)

### **현재 (하드코딩)**
```csharp
var classList = await enrollmentService.GetClassListAsync(
    "7001234",  // 하드코딩된 학교 코드
    DateTime.Now.Year,  // 현재 연도
    Grade);
```

### **개선 (Settings 사용)**
```csharp
// Settings.cs 추가
public class Settings
{
    public string SchoolCode { get; set; } = "7001234";
    public int WorkYear { get; set; } = DateTime.Now.Year;
    public int UserGrade { get; set; } = 1;
    public int UserClass { get; set; } = 1;
}

// PageSeats.xaml.cs에서 사용
var classList = await enrollmentService.GetClassListAsync(
    Settings.SchoolCode,
    Settings.WorkYear,
    Grade);
```

---

## 🎨 UI 커스터마이징

### **좌석 간격 조정**
```csharp
SpaceSide = 10;   // 좌우 여백
SpaceJul = 20;    // 줄 간격
SpaceJjak = 10;   // 짝 간격
SpaceRow = 20;    // 행 간격
```

### **애니메이션 속도**
```csharp
// SeatAnimationAsync()
int repeat = 2;      // 반복 횟수
int speed = 30;      // 애니메이션 속도 (ms)

// SeatAssignAsync()
await Task.Delay(150); // 정위치 이동 속도 (ms)
```

---

## 🐛 트러블슈팅

### **1. 학생 목록이 로드되지 않음**
**원인**: DB 경로가 잘못되었거나 DB 파일이 없음
```csharp
// 확인
System.Diagnostics.Debug.WriteLine($"DB Path: {dbPath}");
System.Diagnostics.Debug.WriteLine($"File Exists: {File.Exists(dbPath)}");
```

### **2. 사진이 표시되지 않음**
**원인**: Photo 경로가 null이거나 파일이 없음
```csharp
// Student.Photo 확인
var student = await studentService.GetBasicInfoAsync(studentId);
System.Diagnostics.Debug.WriteLine($"Photo: {student?.Photo}");
```

### **3. 드래그 앤 드롭이 작동하지 않음**
**원인**: `AllowDrop` 또는 `CanDragItems` 설정 누락
```xml
<!-- 확인 -->
<ListView CanDragItems="True" ... />
<Canvas AllowDrop="True" ... />
```

---

## 📝 다음 단계

### **완료 항목 ✅**
- [x] StudentService 통합
- [x] EnrollmentService 통합
- [x] 실제 DB 데이터 로드
- [x] Photo 경로 지원

### **TODO 항목 ⚠️**
- [ ] Settings 클래스 구현 및 통합
- [ ] 학교 코드 자동 인식
- [ ] 좌석 배치 저장 기능
- [ ] 인쇄 기능 (PrintManager)
- [ ] 드래그 비주얼 피드백
- [ ] 반 변경 시 저장 확인 다이얼로그

---

## 🔗 연관 파일

- **Controls/PhotoCard.xaml** - 좌석 카드 UI
- **Controls/PhotoCard.xaml.cs** - 좌석 카드 로직
- **Services/EnrollmentService.cs** - 학적 관리 서비스
- **Services/StudentService.cs** - 학생 정보 서비스
- **Models/Enrollment.cs** - 학적 모델
- **Models/Student.cs** - 학생 모델

---

## 💡 개발 팁

### **1. 비동기 패턴**
```csharp
// ❌ 나쁜 예 (UI 차단)
var students = enrollmentService.GetClassRosterAsync(...).Result;

// ✅ 좋은 예 (비동기)
var students = await enrollmentService.GetClassRosterAsync(...);
```

### **2. Dispose 패턴**
```csharp
// Page_Unloaded에서 Service Dispose
private void Page_Unloaded(object sender, RoutedEventArgs e)
{
    enrollmentService?.Dispose();
    studentService?.Dispose();
}
```

### **3. 에러 핸들링**
```csharp
try
{
    var roster = await enrollmentService.GetClassRosterAsync(...);
}
catch (Exception ex)
{
    await ShowErrorAsync("데이터 로딩 오류", ex.Message);
}
```

---

**작성일:** 2025-01-01  
**버전:** 2.0.0  
**위치:** `Pages/PageSeats.xaml`
