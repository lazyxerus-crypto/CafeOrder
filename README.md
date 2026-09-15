# CafeOrder · WinForms UI 목업 1차

.NET 10 / WinForms / x64. 6개 탭, 24개 샘플 상품, 통합 장바구니 및 주문 진행창을 확인하는 프로토타입이다. 실제 사이트 호출이나 DB 저장은 없다. 모든 변경은 종료 시 초기화된다.

## 실행

Windows에서 `Run-CafeOrder.cmd`를 더블클릭한다. 이 작업 폴더의 로컬 SDK를 우선 사용하고, 없으면 PATH의 .NET 10 SDK를 사용한다.

SDK가 설치된 개발 환경에서는 프로젝트 루트에서:

```powershell
dotnet build CafeOrder.slnx -c Release -p:Platform=x64
dotnet run --project src/CafeOrder -c Release -p:Platform=x64
```

Visual Studio에서는 CafeOrder.slnx를 열고 src/CafeOrder를 시작 프로젝트로 선택한다. MainForm.Designer.cs는 폼의 기본 틀, ProductsView/OtherPages/OrderForm은 코드 기반 화면 구성, SampleData는 임시 데이터다. 디자이너에서 동적 상품 데이터까지 편집하지 않는다.

## 화면 검토

- 상품 검색에 `포모나` 입력: 긴 이름과 회원가 표시 확인.
- AUTO `+ / −`: 임시 수량 및 소계 변경. MANUAL: 수량 표시/조작 없이 사이트 주문 안내.
- 전체 주문하기: 첫 AUTO 카드 샘플 성공 → 완료 표시 → 카드 제거. 파미유 FAILED / 늘담 UNKNOWN은 유지. 이후 MANUAL은 URL 한 건씩 완료 기록.
- 상품추가에 `https://example.invalid/sample` 입력 후 상품 확인: 샘플 미리보기.
- 사이트관리 무료배송 기준 변경: 현재 실행에서만 장바구니에 반영.

## 검증

패키지 추가 없이 별도 WinForms 검증 실행기로 실제 컨트롤 클릭과 창 렌더링을 검사한다.

```powershell
dotnet run --project tools/CafeOrder.UiChecks -c Release -p:Platform=x64 -- artifacts/ui-checks
```

1280×720 창과 125%/150% 확대 레이아웃을 캡처한다. 확대 검사는 레이아웃 스트레스 검사이며 Windows 배율 변경/실제 다중 모니터 DPI 검증을 대신하지 않는다. 실제 Windows 10 Enterprise LTSC 2019 x64 POS 호환성도 미검증이다.
