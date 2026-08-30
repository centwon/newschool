using System;
using System.Collections.Generic;

namespace NewSchool.Board.Models;

/// <summary>
/// 페이저 한 칸. <see cref="Number"/> 가 0 이면 생략표(…)다.
/// </summary>
public readonly record struct PageToken(int Number)
{
    /// <summary>생략표 칸(…). 누를 수 없다.</summary>
    public static readonly PageToken Ellipsis = new(0);

    public bool IsEllipsis => Number == 0;
}

/// <summary>
/// 페이저에 그릴 페이지 번호를 고른다 — <c>1 … 3 4 [5] 6 7 … 42</c> 꼴.
///
/// <para>첫 장과 마지막 장은 <b>언제나</b> 들어간다(그 둘이 "처음"·"끝" 역할을 한다).
/// 그 사이에 현재 페이지를 가운데 둔 <paramref name="windowSize"/> 칸을 놓고,
/// 끊기는 자리에만 생략표를 넣는다.</para>
///
/// <para>계산만 하는 순수 함수로 떼어 둔다 — 경계(첫 장·끝 장·생략표가 한쪽만 생기는 구간)가
/// 눈으로는 잘 안 보이는 자리라 테스트로 고정하는 편이 싸다.</para>
/// </summary>
public static class PageWindow
{
    /// <summary>기본 창 크기 — 현재 페이지 좌우로 두 칸씩.</summary>
    public const int DefaultWindowSize = 5;

    /// <summary>
    /// 그릴 칸을 왼쪽부터 차례로 낸다.
    /// </summary>
    /// <param name="current">현재 페이지(1-based). 범위를 벗어나면 안쪽으로 끌어당긴다.</param>
    /// <param name="total">전체 페이지 수. 0 이하면 빈 목록을 낸다(그릴 페이저가 없다).</param>
    /// <param name="windowSize">가운데에 놓을 연속 번호의 개수. 홀수를 준다.</param>
    public static IReadOnlyList<PageToken> Build(
        int current, int total, int windowSize = DefaultWindowSize)
    {
        if (total <= 0) return Array.Empty<PageToken>();

        if (windowSize < 1) windowSize = 1;
        current = Math.Clamp(current, 1, total);

        // 생략표를 넣어 봐야 줄지 않는 구간(첫 장 + 창 + 끝 장)에서는 전부 편다.
        if (total <= windowSize + 2)
        {
            var all = new List<PageToken>(total);
            for (int i = 1; i <= total; i++) all.Add(new PageToken(i));
            return all;
        }

        // 첫 장·끝 장은 따로 붙이므로 창은 2 ~ total-1 안에서만 움직인다.
        int half = windowSize / 2;
        int start = current - half;
        int end = current + half;

        if (start < 2)
        {
            start = 2;
            end = start + windowSize - 1;
        }
        else if (end > total - 1)
        {
            end = total - 1;
            start = end - windowSize + 1;
        }

        var tokens = new List<PageToken>(windowSize + 4) { new(1) };

        if (start > 2) tokens.Add(PageToken.Ellipsis);
        for (int i = start; i <= end; i++) tokens.Add(new PageToken(i));
        if (end < total - 1) tokens.Add(PageToken.Ellipsis);

        tokens.Add(new PageToken(total));
        return tokens;
    }
}
