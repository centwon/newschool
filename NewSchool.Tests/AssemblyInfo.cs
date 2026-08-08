using Xunit;

// 테스트 클래스를 한 컬렉션으로 묶어 **순차 실행**한다.
//
// xUnit 기본값은 클래스마다 별도 컬렉션이라 클래스끼리 병렬로 돈다. 그런데
// SqliteTestFixture.DisposeAsync 는 임시 DB 파일을 지우기 위해
// SqliteConnection.ClearAllPools() 를 부르는데, 이건 **프로세스 전역**이라
// 마침 다른 테스트 클래스가 쓰고 있던 풀 연결까지 끊어 버린다.
// (전수 조사 34차 중 전체 실행에서 1회 간헐 실패를 봤고, 이후 9회 연속 실행에서는
//  재현되지 않았다 — 타이밍에 달린 문제라 재현이 어렵다. 원인 자체를 없앤다.)
//
// DB 를 쓰는 클래스가 대부분이라 부분 격리보다 전체 순차가 단순하다.
// 대가는 전체 실행 시간 5초 -> 22초. 결과를 믿을 수 있는 편이 낫다고 봤다.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
