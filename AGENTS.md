# CafeOrder 작업 규칙

- 요구사항의 유일한 범위 기준은 [REQUIREMENTS](docs/REQUIREMENTS.md)다. 명시되지 않은 기능을 추가하거나 요구사항을 임의 변경하지 않는다.
- .NET 10 LTS / WinForms / x64 only / SQLite. 자동화는 Playwright와 설치된 Microsoft Edge 방향만 사용한다. x86, Selenium/WebView2 fallback 금지.
- Windows 11에서 개발한다. Windows 10 Enterprise LTSC 2019 x64 POS 호환성은 실제 POS 검증 전까지 **미확인**이다.
- AUTO와 MANUAL_BROWSER를 분리한다. AUTO 수량 변경은 로컬 DB만 수정하며 네트워크/브라우저 작업을 실행하지 않는다.
- 원격 장바구니 변경 전 로컬 주문 스냅샷 저장 성공 필수. 결제 제출 가능성이 있으면 무조건 재결제 금지. FAILED/UNKNOWN을 완료 처리하거나 제거하지 않는다.
- 평문 비밀번호 저장 및 비밀번호/쿠키/token 로그 기록 금지. 실사용 DB, 백업, 자격정보, 브라우저 프로필/세션은 Git에 넣지 않는다.
- 기존 파일을 먼저 확인하고 필요한 부분만 수정한다. 첫 준비 단계에는 문서/Git만 작성하며 UI, DB schema, 자동화 구현, 사이트 접속, Playwright/NuGet 설치를 하지 않는다.
- 현재 UI 목업 단계의 허용 범위는 [UI 목업](docs/UI.md#1차-목업-범위)다. 이 단계의 수량/설정/완료 변경은 샘플 메모리만 사용하며 실제 DB 및 사이트 작업을 하지 않는다.
- 작업 완료 시 필요한 빌드/검증과 문서 충돌 확인 후 정상 상태만 commit한다. remote가 있으면 push하며 remote/인증을 억지로 설정하지 않는다.

## 작업별 읽을 문서

범위/환경: REQUIREMENTS · 화면: [UI](docs/UI.md) · 저장/보안: [DATABASE](docs/DATABASE.md) · 주문/로그인: [ORDER_FLOW](docs/ORDER_FLOW.md) · 검색: [SEARCH](docs/SEARCH.md). 공급처 작업에는 docs/suppliers의 해당 문서를 추가로 읽는다. 아래 문서 경로는 모두 docs 기준이다.
