# PhotoCard - WinUI 3 사용 가이드

## 📋 개요

**PhotoCard**는 학생 좌석 배치를 위한 WinUI 3 UserControl입니다.
- 학생 사진 및 정보 표시
- 좌석 상태 관리 (미사용, 고정)
- 드래그 앤 드롭 지원 (향후 구현)

---

## 🔄 WPF에서 변경된 사항

### 1. **데이터 모델 변경**
```csharp
// WPF (기존)
AsignClass student;

// WinUI 3 (신규)
StudentCardData studentData = StudentCardData.FromEnrollment(enrollment, student);
```

### 2. **이미지 로딩 방식**
```csharp
// WPF (동기)
Photo.ImageSource = new BitmapImage(new Uri(path));

// WinUI 3 (비동기)
await LoadPhotoAsync(photoPath);
```

### 3. **컨텍스트 메뉴**
```xml
<!-- WPF -->
<ContextMenu>
    <MenuItem Header="미사용 좌석" IsCheckable="True"/>
</ContextMenu>

<!-- WinUI 3 -->
<MenuFlyout>
    <ToggleMenuFlyoutItem Text="미사용 좌석"/>
</MenuFlyout>
```

### 4. **DB 접근 제거**
- 기존: 컨트롤 내부에서 직접 DB 조회
- 신규: 외부에서 PhotoPath를 주입받음

---

## 🎯 기본 사용법

### **XAML에서 선언**
```xml
<Page xmlns:controls="using:NewSchool.Controls">
    <Grid x:Name="SeatGrid">
        <controls:PhotoCard 
            x:Name="Card1"
            CardWidth="80"
            IsShowPhoto="True"
            Row="0" 
            Col="0"
            StudentChanged="Card_StudentChanged"
            UnUsedChanged="Card_UnUsedChanged"
            FixedChanged="Card_FixedChanged"/>
    </Grid>
</Page>
```

### **C# 코드에서 사용**
```csharp
// 1. PhotoCard 생성
var card = new PhotoCard
{
    No = 0,
    Row = 0,
    Col = 0,
    CardWidth = 80,
    IsShowPhoto = true
};

// 2. 이벤트 연결
card.StudentChanged += Card_StudentChanged;
card.UnUsedChanged += Card_UnUsedChanged;
card.FixedChanged += Card_FixedChanged;

// 3. 학생 데이터 설정
var enrollment = await enrollmentRepo.GetByIdAsync(enrollmentId);
var student = await studentRepo.GetByIdAsync(enrollment.StudentID);
var studentData = StudentCardData.FromEnrollment(enrollment, student);

card.StudentData = studentData;

// 4. Grid에 추가
SeatGrid.Children.Add(card);
```

---

## 📊 주요 속성

| 속성 | 타입 | 설명 |
|------|------|------|
| `No` | int | 카드 번호 |
| `Row` | int | 행 위치 |
| `Col` | int | 열 위치 |
| `StudentData` | StudentCardData? | 학생 정보 |
| `IsShowPhoto` | bool | 사진 표시 여부 |
| `IsUnUsed` | bool | 미사용 좌석 여부 |
| `IsFixed` | bool | 고정 좌석 여부 |
| `CardWidth` | double | 카드 너비 |
| `CardHeight` | double | 카드 높이 |

---

## 🎪 이벤트

### **StudentChanged**
학생 정보가 변경되었을 때 발생
```csharp
private void Card_StudentChanged(object sender, StudentCardEventArgs e)
{
    if (e.StudentData != null)
    {
        Debug.WriteLine($"학생 배정: {e.StudentData.Name} at ({e.Row}, {e.Col})");
        
        // 중복 체크 로직
        CheckDuplicateStudents();
    }
}
```

### **UnUsedChanged**
미사용 좌석 상태가 변경되었을 때 발생
```csharp
private void Card_UnUsedChanged(object sender, EventArgs e)
{
    var card = (PhotoCard)sender;
    if (card.IsUnUsed)
    {
        TotalSeats--;
    }
    else
    {
        TotalSeats++;
    }
    CheckSeatBalance();
}
```

### **FixedChanged**
고정 좌석 상태가 변경되었을 때 발생
```csharp
private void Card_FixedChanged(object sender, EventArgs e)
{
    var card = (PhotoCard)sender;
    Debug.WriteLine($"좌석 {card.Row},{card.Col} 고정: {card.IsFixed}");
}
```

---

## 🔧 고급 사용법

### **1. 학생 정보 교체 (이벤트 없이)**
```csharp
// ReplaceStudent는 이벤트를 발생시키지 않음
card.ReplaceStudent(newStudentData);
```

### **2. 사진 동적 표시/숨김**
```csharp
// 사진 표시
card.IsShowPhoto = true;

// 사진 숨김
card.IsShowPhoto = false;
```

### **3. 카드 크기 조정**
```csharp
// 너비 기준으로 조정 (높이는 3:4 비율 자동 계산)
card.CardWidth = 100;

// 높이 기준으로 조정 (너비는 3:4 비율 자동 계산)
card.CardHeight = 150;
```

### **4. 미사용 좌석 설정**
```csharp
// 미사용으로 설정 (학생 정보 초기화)
card.IsUnUsed = true;

// 다시 사용 가능하게
card.IsUnUsed = false;
```

---

## 🎨 스타일 커스터마이징

### **테두리 색상 변경**
```csharp
// 일반 좌석 (파란색)
card.IsUnUsed = false;

// 미사용 좌석 (엔틱 화이트)
card.IsUnUsed = true;
```

### **Corner Radius 조정**
XAML에서 직접 수정:
```xml
<Border CornerRadius="8">
```

---

## ⚠️ 주의사항

### **1. 사진 경로**
- 절대 경로 또는 상대 경로 모두 지원
- 상대 경로는 `AppContext.BaseDirectory` 기준

```csharp
// 절대 경로
student.Photo = @"C:\Photos\student.jpg";

// 상대 경로
student.Photo = @"Data\Photos\student.jpg";
```

### **2. 비동기 로딩**
- 사진 로딩은 비동기로 처리됨
- UI 스레드를 차단하지 않음

### **3. 메모리 관리**
```csharp
// 사용 후 이벤트 구독 해제
card.StudentChanged -= Card_StudentChanged;
card.UnUsedChanged -= Card_UnUsedChanged;
card.FixedChanged -= Card_FixedChanged;
```

---

## 🔗 연관 클래스

### **StudentCardData**
```csharp
public class StudentCardData
{
    public string StudentID { get; set; }
    public string Name { get; set; }
    public int Number { get; set; }
    public int Grade { get; set; }
    public int Class { get; set; }
    public string PhotoPath { get; set; }
    
    public static StudentCardData FromEnrollment(Enrollment e, Student s);
}
```

### **StudentCardEventArgs**
```csharp
public class StudentCardEventArgs : EventArgs
{
    public int Row { get; }
    public int Col { get; }
    public StudentCardData? StudentData { get; }
}
```

---

## 📝 TODO

- [ ] 드래그 앤 드롭 구현
- [ ] 애니메이션 효과
- [ ] 기본 사진 (placeholder) 추가
- [ ] 테마 지원 (Light/Dark)

---

## 🐛 알려진 이슈

없음

---

**작성일:** 2025-01-01  
**버전:** 1.0.0
