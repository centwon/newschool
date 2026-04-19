// ─────────────────────────────────────────────────────────────
// 비밀 상수 템플릿. 아래 절차로 로컬 파일 생성:
//   1. 이 파일을 Settings.Secrets.cs 로 복사
//   2. 각 상수에 본인 키 입력
//   3. Settings.Secrets.cs 는 .gitignore 로 커밋에서 제외됨
//
// 나이스 오픈 API 키 발급: https://open.neis.go.kr
// ─────────────────────────────────────────────────────────────

#if SECRETS_TEMPLATE // 빌드에서 제외 — 실제 값은 Settings.Secrets.cs 에 존재

namespace NewSchool;

internal static class SecretKeys
{
    /// <summary>나이스 데이터포털 Open API 인증키</summary>
    public const string NeisApiKey = "";
}

#endif
