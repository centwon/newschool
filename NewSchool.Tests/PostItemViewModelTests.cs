using NewSchool.Board.ViewModels;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 게시판 목록 한 줄의 표시 규칙. 제목의 가운데 줄은 <b>읽기 완료한 메모</b>에만 긋는다 —
/// 예전 자료에는 일반 글에도 완료 값이 켜진 것이 있어, 아카이브에서 중요 글이 줄이 그어진 채 보였다.
/// </summary>
public class PostItemViewModelTests
{
    [Theory]
    [InlineData("메모", true, true)]
    [InlineData("메모", false, false)]
    [InlineData("업무", true, false)]    // 일반 글은 완료 값과 상관없이 글자만
    [InlineData("", true, false)]
    public void 읽기_완료한_메모만_가운데_줄(string subject, bool isCompleted, bool expected)
    {
        var post = TestData.NewPost(subject: subject);
        post.IsCompleted = isCompleted;

        Assert.Equal(expected, new PostItemViewModel(post).IsReadMemo);
    }
}
