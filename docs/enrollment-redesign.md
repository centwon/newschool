# NewSchool — 학적(Enrollment) 재설계 노트

| 항목 | 값 |
|---|---|
| 대상 | `school.db` 의 `Enrollment` 및 학적 축 |
| 작성일 | 2026-08-28 |
| 조사 기준 | `main` 브랜치, 커밋 `f7155ef`, 실제 DB(`Data\school.db`) 실측 |
| 상태 | 설계 확정. **1~3a 완료(2026-08-30, v1.0.0 게시분에 포함)** · 남은 것은 동아리 배정 필터·스크롤바와 좌석 축(3b) |

---

## 1. 왜 손대는가

`Enrollment` 는 23열이고 세 가지를 겸하고 있었다.

1. **배정** — 학년·반·번호
2. **상태** — 재적 여부와 학적 변동
3. **`Student` 의 사본** — `Name` · `Sex` · `Photo`

3번 때문에 정본이 둘이 되어, `StudentRepository.UpdateAsync` 가 `Enrollment` 를 따라 고쳐 줘야 한다.
2026-08-28 에 그 동기화를 빠뜨려 한 번 물렸다(이름만 바뀌고 학적은 옛 이름).

그리고 원설계의 야심 중 상당수가 **실현되지 않은 채 컬럼만 남아 있었다**(3장).

---

## 2. 원설계 의도 (추정 아님 — 흔적이 남아 있다)

`Models/Base.cs` 의 `#region Core Models` 에 **구설계 모델이 그대로 있다.**

```csharp
class ClassAssignment  { No, Year, Grade, Class, Number, Student, Name }   // 7열
class CourseAssignment { No, Student, Course, Remark }
class Subject          { No, Curriculum, Name, Unit, Remark }
```

`Enrollment.cs:10` 이 스스로 밝힌다 — **"기존 `ClassAssignment` 대체 — A안의 핵심 테이블!"**
(`EnrollmentRepository.cs:12` 도 "A안의 핵심!"). 즉 A안/B안을 놓고 고른 결과가 `Enrollment` 다.
이 세 클래스는 지금 전부 죽은 코드다(참조 0건, `Subject` 테이블은 2026-08-28 에 DROP).

`ClassAssignment` 7열 → `Enrollment` 23열. **더한 16열이 의도를 드러낸다.**

| 더한 것 | 의도 |
|---|---|
| `SchoolCode` · `TeacherID` | 다학교 · 다교사 |
| `Semester` | 학기 단위 학적 |
| `Status` · 날짜 4열 · 학교명 2열 | NEIS 식 학적 이력(입학~졸업 전 과정) |
| `Name` · `Sex` · `Photo` | 명렬표 성능(주석이 "denormalize" 라고 명시) |
| `Memo` · `CreatedAt` · `UpdatedAt` · `IsDeleted` | 감사 · 논리삭제 |

`Enrollment.cs:5` 의 **"학적 정보 (NEIS 표준)"** 이 열쇠다 — NEIS 학적 테이블을 옮겨오려 했다.

---

## 3. 실현되지 않은 의도 (실측)

| 의도 | 실제 | 판단 |
|---|---|---|
| 학기 단위 | `Semester` 173행 전부 `1`. **2학기 행을 만드는 경로가 앱에 없다** | 제거 |
| 학적 이력 | 날짜 6열이 전부 빈 채. 값을 넣을 화면이 2026-08-28 까지 없었다 | 2열로 접음 |
| 명렬표 denormalize | 동작하나 동기화 부담만 남음. 173행에 JOIN 은 공짜 | 제거 |
| 감사 | `CreatedAt`·`UpdatedAt` 을 **읽는 코드가 0건**(쓰기만 함) | 제거 |
| 논리삭제 | `IsDeleted=1` 인 행 **0건**, 복원 경로 없음. 그런데 삭제 확인 문구는 "되돌릴 수 없습니다" | 제거 |
| 다학교 | `School` 1행이지만 `TeacherSchoolHistory`(전근 이력)가 실재 | **유지** |
| 다교사 | `Teacher` 1행이지만 다중 사용자 대비 | **유지** |

`SchoolCode` 와 `TeacherID` 만 살아남는다. 전근과 다중 사용자는 실제로 올 수 있는 미래다.

---

## 4. 확정 스키마 — 23열 → 12열

```
No · StudentID · SchoolCode · Year · Grade · Class · Number
IsActive · ChangeType · ChangeDate · Memo · TeacherID
```

`UNIQUE(StudentID, SchoolCode, Year)` — 학기가 빠진다.

### 4.1 학적 변동(`ChangeType`)

| `IsActive` | 값 |
|---|---|
| `1` | 입학 · 진급 · 전입 |
| `0` | 전출 · 졸업 · 휴학 · 유예 · 정원외 · 자퇴 · 퇴학 |

기본값은 학년이 정한다 — **1학년 → 입학, 그 외 → 진급.** 전입은 손으로 고른다.
`유예`(취학유예)는 1학년, `정원외`(정원외 관리)는 학년 무관.

### 4.2 `IsActive` 를 저장하는 이유

파생 가능한 값이지만 **컬럼으로 둔다.** 이유는 계산 비용이 아니라(173행 문자열 비교는
측정 불가 수준) **SQL 에서 거를 수 있기 때문**이다.

```sql
WHERE SchoolCode=? AND Year=? AND Grade=? AND Class=? AND IsActive=1
```

상태 목록을 `WHERE` 절에 적으면 값이 늘 때마다 두 곳이 어긋나지만, 불리언 하나는 안정적이고
인덱스도 탄다.

**어긋남은 쓰는 곳을 하나로 묶어 막는다.**

```csharp
// 저장 경로에서만 계산해 넣는다. IsActive 를 인자로 받는 함수는 만들지 않는다.
enrollment.IsActive = EnrollmentChange.IsActive(enrollment.ChangeType);
```

불변식 `IsActive == IsActive(ChangeType)` 은 테스트로 고정한다.

### 4.3 `ChangeDate` 의 소비자

날짜 6열이 그동안 빈 채였던 것은 **읽는 코드가 없어서**였다. 이제 생겼다 —
**"전출한 뒤에는 그 학생 기록을 중단한다."**

```
경고 조건 = IsActive = 0 AND ChangeType = 전출 AND 기록일 > ChangeDate
```

거는 자리는 `Date` 를 가진 학생별 기록 둘 — `StudentLog`(누가기록) · `StudentSpecial`(학생부).
`ClassDiary` 는 학급 단위라 해당 없다.

**막지 않고 경고만 한다.** 막으면 전출일을 뒤늦게 입력했을 때 이미 적어 둔 기록을 못 고친다.

---

## 5. 알려진 한계

**같은 학년도에 변동이 두 번 일어나면 앞의 것이 덮어써진다.**

```
3월 전입 → 9월 전출     ChangeType 은 하나뿐이라 전입 기록이 사라진다
```

"전출 이후 기록 경고"에는 지장이 없다(전출이 마지막 변동이므로). 잃는 것은 "전입 이전에는
기록 못 하게" 와 전입 사실 자체이며, 전입 정보는 `Memo` 에 적을 수 있다.

정확히 담으려면 이력 테이블(`EnrollmentChange`)을 따로 두거나 시작/종료를 두 쌍으로
가져야 하는데, **둘 다 지금 줄이려는 방향과 반대**라 택하지 않았다. 필요해지면 그때 붙인다.

---

## 6. 배정 축 (다음 단계)

`CourseEnrollment` · `ClubEnrollment` · `SeatAssignment` · `SeatPosHistory` 가 전부
`StudentID` 를 직접 가리켜, **"학적에 있는 학생만"이 구조적으로 보장되지 않는다.**
지금은 조회 필터로만 막고 있다.

원칙: **"그 해의 배정"은 `Enrollment` 를, "사람의 기록"은 `Student` 를 가리킨다.**

| 테이블 | 성격 | 가리킬 곳 |
|---|---|---|
| `StudentDetail` · `StudentLog` · `StudentSpecial` | 사람의 속성 · 이력 | `StudentID` 유지 |
| `CourseEnrollment` · `ClubEnrollment` · `SeatAssignment` · `SeatPosHistory` | 그 해 배정 | **`EnrollmentNo`** |

**지금이 가장 싼 시점이다.** `Year` 가 2026 하나뿐이고 `Student : Enrollment` 가 173:173 이라
백필 대응이 유일하다. 내년에 2학년 학적이 생기면 "이 동아리 배정은 어느 해 것인가" 가
추측이 되고, 그때는 되돌릴 수 없다.

---

## 7. 진행 순서

각 단계마다 빌드·테스트가 통과하는 상태로 멈출 수 있게 쪼갠다.

| 단계 | 내용 | 상태 |
|---|---|---|
| **1a** | `Semester`·`CreatedAt`·`UpdatedAt`·`IsDeleted` 제거, `Status`+날짜 6열 → `IsActive`·`ChangeType`·`ChangeDate` | ✅ 완료 (2026-08-28) |
| **1b** | `Name`·`Sex`·`Photo` 제거 → `Student` JOIN 으로 전환 | ✅ 완료 (2026-08-28, 1a 와 함께) |
| **2** | 전출 이후 기록 경고(`StudentLog`·`StudentSpecial`) | ✅ 완료 (2026-08-28) |
| **3a** | `CourseEnrollment` · `ClubEnrollment` 를 `EnrollmentNo` 로 | ✅ 완료 (2026-08-28) |
| **3b** | `SeatAssignment` · `SeatPosHistory` | 보류 — 아래 참고 |

### 7.4 배정이 학적을 가리킨다 (3a)

`CourseEnrollment`·`ClubEnrollment` 의 `StudentID` 를 `EnrollmentNo` 로 바꿨다. 백필은
`Course.Year`·`Club.Year` 로 학년도를 잡아 풀었고, **짝을 못 찾는 행이 0** 이었다
(344 + 9 행 전부 유일하게 대응).

읽기는 `CourseEnrollmentFull`·`ClubEnrollmentFull` 뷰가 맡는다. 학적을 거쳐 `StudentID`·
`Name` 과 함께 **`IsActive` 를 함께 내주므로**, "전출한 학생을 명단에서 뺀다" 가 조회
한 줄(`AND IsActive = 1`)이 됐다. `GetByCourseAsync`·`GetByClubAsync` 의 기본값이 그것이고,
`includeInactive: true` 로 그 해에 실제로 들었던 기록까지 볼 수 있다.

`ON DELETE CASCADE` 라 학적을 지우면 그 해 배정도 함께 사라진다 — 예전에는 고아가 됐다.

**⚠ 조용히 깨질 뻔한 곳** — `StudentID` 는 모델에 속성으로 남아 있어
`new ClubEnrollment { StudentID = ... }` 가 **컴파일은 된다.** 저장은 안 되고 FK 위반이
날 뿐이다. 두 배정 다이얼로그가 그렇게 되어 있어 고쳤다. 배정을 만드는 코드를 새로
쓸 때는 반드시 `EnrollmentNo` 를 넣을 것.

### 7.5 좌석은 왜 미뤘나 → **하지 않기로 했다 (3b 종결, 2026-08-29)**

`SeatAssignment` 는 `ArrangementNo` 로 `SeatArrangement` 를 가리키고, 그 표가 이미
`SchoolCode`·`Year`·`Grade`·`Class` 를 들고 있다. **학년도 범위가 이미 잡혀 있어서**
수업·동아리와 사정이 다르다. `SeatPosHistory` 도 자기 안에 그 넷을 다 들고 있다.

즉 3b 는 "고쳐야 할 문제" 가 아니라 "일관성 정리" 다.

**2026-08-29 에 닫았다.** 미루는 대신 그만두기로 한 근거는 둘이다.

1. **고칠 증상이 없다.** 수업·동아리를 바꾼 이유는 *전출한 학생이 명단에 계속 남아서* 였다.
   좌석은 그렇지 않다 — 저장된 배치를 복원할 때 현재 명단과 대조해 짝이 없으면 빈 자리로 둔다.

   ```csharp
   var student = students.FirstOrDefault(s => s.StudentID == a.StudentID);
   if (student != null) card.StudentData = student;   // 없으면 빈 자리
   ```

   게다가 그 상태에서 한 번 저장하면 배치가 현재 카드에서 다시 쓰이므로 **스스로 아문다.**

2. **대가가 얻는 것보다 크다.** 표 넷 + `SeatService`(560줄) + `PageSeats`(1,300줄) +
   짝 제외·자리 제외 로직 + 기존 데이터 이관을 건드려야 하는데, 얻는 것은 일관성뿐이다.
   좌석은 담임이 가장 자주 쓰는 화면이라 위험 대비 이득이 맞지 않는다.

**대신 진짜로 남아 있던 문제 하나는 처리했다.** 좌석 표 셋은 `StudentID` 를 들고 있으면서
**FK 가 없어**, 학생을 지워도 아무것도 따라 지워지지 않았다. 초기화기의 고아 정리에 넣었다 —
좌석은 배치 상태(미사용·숨김·고정)를 함께 들고 있어 **`StudentID` 만 `NULL` 로 비우고**,
이력 둘은 남길 것이 없어 지운다. 회귀 테스트 3건(`SeatOrphanCleanupTests`).

되살리려면 위 두 근거부터 뒤집어야 한다.

1a 와 1b 는 나눠 낼 수 없었다 — 표를 다시 만드는 이관 한 번에 열이 함께 사라지므로,
사본 3열만 남겨 두려면 이관을 두 번 해야 했다.

### 7.1 `EnrollmentFull` 뷰

조회가 열 곳이라 JOIN 을 되풀이하지 않도록 뷰를 하나 두었다.

```sql
CREATE VIEW EnrollmentFull AS
SELECT e.No, e.StudentID, e.SchoolCode, e.Year, e.Grade, e.Class, e.Number,
       e.IsActive, e.ChangeType, e.ChangeDate, e.Memo, e.TeacherID,
       s.Name, s.Sex, s.Photo
FROM Enrollment e JOIN Student s ON s.StudentID = e.StudentID;
```

**읽기는 뷰, 쓰기는 표.** `INSERT`·`UPDATE`·`DELETE` 는 `Enrollment` 를 그대로 겨냥한다.
모델의 `Name`·`Sex`·`Photo` 속성은 남아 있지만 **컬럼이 아니라 뷰가 채워 주는 값**이라,
거기에 값을 넣어도 저장되지 않는다(테스트로 고정).

### 7.2 이관 결과 (실측)

| | 값 |
|---|---|
| `Enrollment` 열 | 23 → **12** |
| 행 | 173 유지 |
| `ChangeType` | 입학 172 · 전입 1 |
| `IsActive` 와 `ChangeType` 이 어긋난 행 | **0** |
| `ChangeDate` 가 빈 행 | **0** |
| `integrity_check` · 외래키 위반 | ok · 0 |

옛 `Status` 는 `재학` 172 · `전학` 1 이었다. `재학`+1학년은 `입학` 으로, `전학` 은 전출 흔적이
없어 `전입` 으로 옮겼다(둘 다 활성이라 명단에서 사라지는 학생은 없다).

### 7.3 전출 이후 기록 경고 (2단계)

판정은 `Services/EnrollmentGuard.cs` 한 곳에 있다. 부르는 화면이 여럿이라 조건을 화면마다
적으면 값이 늘 때 어긋난다.

**"떠났다" 로 보는 변동은 넷이다** — 전출·졸업·자퇴·퇴학. 비활성 전부가 아니다:
휴학·유예·정원외는 학적이 살아 있어 그 사이에도 기록할 일이 있다.

두 방향으로 본다.

| 방향 | 언제 | 무엇을 |
|---|---|---|
| 앞 | 기록을 저장할 때 | 떠난 날 **뒤** 날짜면 묻는다 — 계속 / 취소 |
| 뒤 | 학적 변동을 저장할 때 | 그 날 **뒤**에 이미 남은 기록을 센다 (지우지 않고 알리기만) |

실제로는 **뒤 방향이 더 자주 걸린다** — 전출은 늦게 입력되는 쪽이 흔하다.

**거는 자리와 덮는 범위:**

| 자리 | 덮는 화면 |
|---|---|
| `Controls/LogListViewer.SaveChangedLogsAsync` | 학급일지 · 동아리활동 · 수업활동 · 학생정보 · 누가기록 (5) |
| `Pages/StudentSpecPage` 저장 | 학생부 |
| `Dialogs/StudentEditDialog` 저장 | 뒤 방향(변동 저장 시) |

**⚠ 덮지 않는 곳** — `Dialogs/StudentLogDialog`(일괄 입력)과 `Pages/CourseSpecPage`.
둘 다 학생을 **명부에서 고르는데 명부는 이미 재적만 준다**. 그래서 떠난 학생이 애초에
목록에 없고, 경고가 울릴 일이 사실상 없다. 필요해지면 같은 함수를 부르면 된다.

날짜 기준은 **떠난 당일까지는 우리 학생**이다(`recordDate > changeDate` 일 때만 운다).
변동일자를 모르면 조용하다 — 근거가 없는데 경고를 띄우면 사람이 경고를 무시하는 법을 배운다.

마이그레이션은 별도 기전을 두지 않고 **기존 DB 를 직접 고친다** — 사용자가 한 명이고
배포본 다운로드가 0회다. 손대기 전 `Backups\` 에 사본을 뜬다.
