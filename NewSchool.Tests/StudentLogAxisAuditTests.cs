using System.Collections.Generic;
using System.ComponentModel;
using NewSchool.Models;
using NewSchool.ViewModels;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 누가기록·학생부 축 전수 감사(2026-09-02)에서 드러난 결함을 못박는다.
///
/// <para>여기 모인 것들은 전부 <b>조용히 틀리는</b> 종류였다 — 예외도 오류 메시지도 없이
/// 칸이 비어 나가거나, 넣은 값이 아무 데도 도착하지 않았다. 그래서 규칙을 코드 주석이
/// 아니라 테스트로 남긴다.</para>
/// </summary>
public class StudentLogAxisAuditTests
{
    /// <summary><paramref name="mutate"/> 를 실행하는 동안 올라온 PropertyChanged 이름들.</summary>
    private static List<string> Capture(INotifyPropertyChanged model, System.Action mutate)
    {
        var seen = new List<string>();
        void Handler(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null) seen.Add(e.PropertyName);
        }

        model.PropertyChanged += Handler;
        try { mutate(); }
        finally { model.PropertyChanged -= Handler; }
        return seen;
    }

    /// <summary>
    /// <b>규칙: 동아리명은 <c>ClubName</c> 한 칸에만 산다.</b>
    ///
    /// <para>예전에는 넣는 곳이 경로마다 달랐다 — 단일 입력은 <c>ClubName</c>, 일괄 입력은
    /// <c>ActivityName</c>. 그런데 읽는 곳(목록·인쇄·엑셀)은 또 <c>SubjectName</c> 이라
    /// 화면의 "동아리" 칸과 엑셀의 "동아리" 열이 언제나 비어 있었다.</para>
    /// </summary>
    [Fact]
    public void 동아리활동은_동아리명을_과목칸_대신_보여준다()
    {
        var club = new StudentLog
        {
            Category = LogCategory.동아리활동,
            ClubNo = 7,
            ClubName = "천체관측반"
        };

        Assert.Equal("천체관측반", club.SubjectOrClubDisplay);
    }

    [Fact]
    public void 동아리활동이_아니면_과목명을_보여준다()
    {
        var lesson = new StudentLog
        {
            Category = LogCategory.교과활동,
            SubjectName = "지구과학",
            ClubName = "천체관측반"   // 남아 있어도 교과에서는 쓰지 않는다
        };

        Assert.Equal("지구과학", lesson.SubjectOrClubDisplay);
    }

    /// <summary>
    /// 규칙이 세워지기 전에 저장된 동아리 기록은 <c>ClubName</c> 이 비어 있다 —
    /// 그때는 <c>SubjectName</c> 으로 물러서야 옛 기록이 빈칸으로 나가지 않는다.
    /// </summary>
    [Fact]
    public void 동아리명이_비면_과목명으로_물러선다()
    {
        var old = new StudentLog
        {
            Category = LogCategory.동아리활동,
            SubjectName = "방송반",
            ClubName = string.Empty
        };

        Assert.Equal("방송반", old.SubjectOrClubDisplay);
    }

    /// <summary>
    /// 표시값은 계산 속성이라 자기 입력이 바뀌면 알려야 한다
    /// (<see cref="ComputedPropertyNotifyTests"/> 와 같은 규칙 — <c>Mode=OneWay</c> 로
    /// 걸었을 때 조용히 안 도는 것을 막는다).
    /// </summary>
    [Fact]
    public void 표시값은_자기_입력이_바뀌면_알린다()
    {
        var log = new StudentLog { Category = LogCategory.동아리활동 };

        Assert.Contains(nameof(StudentLog.SubjectOrClubDisplay),
            Capture(log, () => log.ClubName = "천체관측반"));
        Assert.Contains(nameof(StudentLog.SubjectOrClubDisplay),
            Capture(log, () => log.SubjectName = "지구과학"));
        Assert.Contains(nameof(StudentLog.SubjectOrClubDisplay),
            Capture(log, () => log.Category = LogCategory.교과활동));
    }

    /// <summary>ViewModel 은 판단을 다시 하지 않고 모델의 한 벌을 그대로 통과시킨다.</summary>
    [Fact]
    public void ViewModel_은_모델의_표시값을_그대로_쓴다()
    {
        var log = new StudentLog
        {
            Category = LogCategory.동아리활동,
            ClubName = "천체관측반",
            SubjectName = "지구과학"
        };

        var vm = new StudentLogViewModel(log);

        Assert.Equal(log.SubjectOrClubDisplay, vm.SubjectOrClubDisplay);
        Assert.Equal("천체관측반", vm.SubjectOrClubDisplay);
    }

    /// <summary>
    /// <b>규칙: <see cref="StudentLog"/> 의 문자열 칸은 null 이 되지 않는다.</b>
    ///
    /// <para>이것을 못박는 이유는 <c>a ?? b</c> 로 물러서는 코드가 실제로 있었기 때문이다.
    /// 내보내기의 <c>Description ?? Log</c>·<c>Log ?? Description</c> 은 초기값이
    /// <c>string.Empty</c> 라 <b>한 번도 물러서지 않았고</b>, 상담 내용을 "기록" 칸에만
    /// 적은 기록은 엑셀에서 통째로 빈칸이 됐다. 빈 칸을 가려내려면 널이 아니라
    /// <c>IsNullOrWhiteSpace</c> 를 봐야 한다.</para>
    /// </summary>
    [Fact]
    public void 문자열_칸은_기본값이_빈_문자열이라_널병합이_물러서지_않는다()
    {
        var log = new StudentLog();

        Assert.NotNull(log.Log);
        Assert.NotNull(log.Description);
        Assert.NotNull(log.SubjectName);
        Assert.NotNull(log.ClubName);
        Assert.NotNull(log.ActivityName);
        Assert.NotNull(log.Topic);

        // 그래서 이 표현은 "비었을 때 물러서기"가 되지 않는다 — 늘 앞엣것을 돌려준다.
        Assert.Equal(string.Empty, log.Description ?? log.Log);
    }

    /// <summary>
    /// <b>규칙: 진로활동의 <c>Title</c>(희망분야)은 자동으로 채우지 않는다.</b>
    ///
    /// <para><c>NeisHelper</c> 정의표에서 진로활동만 <c>TitleCountsInBytes</c> 라,
    /// Title 에 넣은 글자가 특기사항과 합쳐 한도를 먹는다. 예전에는 누가기록 화면이
    /// 새 학생부 칸을 만들 때 "{학생이름} {영역}" 을 Title 에 넣어, 희망분야 칸에
    /// "홍길동 진로활동"이 뜨고 그만큼 분량이 깎였다.</para>
    /// </summary>
    [Fact]
    public void 진로활동은_희망분야가_분량에_합산된다()
    {
        Assert.True(Helpers.NeisHelper.TitleCountsInBytes("진로활동"));
        Assert.False(Helpers.NeisHelper.TitleCountsInBytes("교과활동"));

        int withTitle = Helpers.NeisHelper.CountSpecBytes("진로활동", "홍길동 진로활동", "내용");
        int withoutTitle = Helpers.NeisHelper.CountSpecBytes("진로활동", string.Empty, "내용");

        // 자동으로 채워 넣은 제목만큼 쓸 수 있는 분량이 줄어든다
        Assert.True(withTitle > withoutTitle);
    }
}
