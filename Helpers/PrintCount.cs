using System;

namespace NewSchool.Helpers;

/// <summary>
/// 인쇄 옵션의 <b>개수 입력</b>을 실제로 쓸 수 있는 수로 바꾼다 — 53차(인쇄 축).
///
/// <para>⚠ <c>NumberBox</c> 는 칸을 비우면 <c>Value</c> 가 <see cref="double.NaN"/> 이 된다(47차).
/// 그 값을 그대로 <c>(int)</c> 로 자르면 0 이나 <c>int.MinValue</c> 가 나오고, 그것을
/// <c>Take(...)</c> 에 넣으면 <b>한 건도 실리지 않는다</b>. 실제로 학생카드 인쇄에서
/// "전체 기록" 을 골라 놓고 최대 개수 칸을 비웠더니 누가기록이 통째로 빠졌다 — 그리고
/// 아무 말도 없었다(53차 실측).</para>
///
/// <para>판정만 갈라 두어 창 없이 시험한다(47차 <c>Interpret</c>·48차 <c>LooksLikeSettingsDb</c>
/// 와 같은 수법).</para>
/// </summary>
public static class PrintCount
{
    /// <summary>
    /// 입력 칸의 값으로 실제 출력 개수를 정한다.
    /// </summary>
    /// <param name="value">입력 칸의 값. 비어 있으면 <see cref="double.NaN"/>.</param>
    /// <param name="fallback">비었거나 읽을 수 없을 때 쓸 값(= 화면의 기본값).</param>
    /// <param name="min">허용 최솟값.</param>
    /// <param name="max">허용 최댓값.</param>
    public static int Resolve(double value, int fallback, int min = 1, int max = 500)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return Clamp(fallback, min, max);

        // 소수점을 적을 수 있는 칸이라 내림한다 — 2.9 를 3 으로 올리면 "최대" 라는 말과 어긋난다.
        double floored = Math.Floor(value);

        if (floored < min) return min;
        if (floored > max) return max;

        return (int)floored;
    }

    private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
}
