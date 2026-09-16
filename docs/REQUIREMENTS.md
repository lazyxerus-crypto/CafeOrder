# 제품 범위와 고정 환경

CafeOrder는 여러 카페 식자재 쇼핑몰의 상품을 한 프로그램에서 관리하고 주문하는 Windows 데스크톱 프로그램이다. 재고관리/POS 프로그램이 아니다.

## 환경

| 항목 | 기준 |
|---|---|
| 앱 | .NET 10 LTS, WinForms, x64 only |
| 데이터 | SQLite |
| 자동화 | Playwright만 사용, 설치된 Microsoft Edge 사용을 기본 방향으로 함 |
| 개발 | Windows 11 |
| 실사용 목표 | Windows 10 Enterprise LTSC 2019 x64 |

실제 POS 호환성은 **미검증**이다. 실제 POS 검증 전에는 호환성이 확인되었다고 기재하지 않는다. x86 및 Selenium/WebView2 자동화 fallback은 지원하지 않는다.

## 공급처 및 책임 문서

| 공급처 | 유형 | 상세 |
|---|---|---|
| MegaCoffee | AUTO | [MEGACOFFEE](suppliers/MEGACOFFEE.md) |
| PieceCake / 파미유 | AUTO | [PIECECAKE](suppliers/PIECECAKE.md) |
| FoodRain | AUTO | [FOODRAIN](suppliers/FOODRAIN.md) |
| WYM / 우양 | AUTO | [WYM](suppliers/WYM.md) |
| Nuldam Partners / 늘담 | AUTO | [NULDAM](suppliers/NULDAM.md) |
| Coupang, Naver SmartStore | MANUAL_BROWSER | [COUPANG_NAVER](suppliers/COUPANG_NAVER.md) |

## 범위 제한

재고 예측, 자동 재고 발주, 수요 예측, 추천상품, 최근 본 상품, 임의의 AI 기능, 요구사항에 없는 편의기능을 만들지 않는다. 명시되지 않은 기능을 임의로 추가하지 않는다.

첫 작업은 프로젝트 규칙과 문서/Git 구조만 만든다. 실제 UI, SQLite schema, DB/주문/로그인 기능, 사이트 자동화/접속, 상품 scraping, Playwright 설치, 불필요한 NuGet 설치, 대규모 architecture 구현은 하지 않는다.

현재는 [UI 5차 목업](UI.md#5차-목업-범위) 단계다. 실제 DB/로그인/브라우저/주문/결제는 여전히 구현하지 않는다. 상품 우클릭 복사·Draft URL 기반 샘플 등록·로컬 UI/상품 상태 저장을 지원하고 내부 공급처 유형은 사용자 화면에 표시하지 않는다.

쿠팡/네이버의 수동 완료 기록 버튼을 제거한다. 향후 주문 생성 여부를 가능한 범위에서 자동 확인하되 확실한 성공만 완료하고, 불확실하면 카드를 유지한다. 자동 수량/옵션/결제 및 인증 우회 금지는 유지한다. 구체적인 경계는 [COUPANG_NAVER](suppliers/COUPANG_NAVER.md)를 따른다.

## 문서 책임

- [UI](UI.md): 탭, 화면, 카테고리, 정렬.
- [DATABASE](DATABASE.md): 상품 데이터, 로컬 저장, 백업, 보안.
- [ORDER_FLOW](ORDER_FLOW.md): 장바구니, 주문 안전, 상태, 로그인 유지, 무료배송 공통 정책.
- [SEARCH](SEARCH.md): 로컬 검색 방식.
- 공급처 문서: 공급처별 설정과 MANUAL 예외. 사이트의 실제 동작은 아직 검증하지 않았다.

## Git 완료 기준

과도한 branch 구조 없이 정상 상태를 저장한다. 작업 단위 완료 시 필요한 빌드/검증 → commit → 이미 remote가 있으면 push 순서다. 실패 상태를 정상 버전처럼 저장하지 않는다. remote 부재/인증 실패 시 설정을 강행하지 않고 로컬 commit까지만 수행한 사실을 보고한다.

첫 commit: `chore: establish CafeOrder project rules`. 첫 준비 단계에서는 문서/링크/충돌/ignore를 검증했다. UI 작업은 x64 빌드와 주요 화면 검증이 정상 완료된 경우에만 commit한다.
