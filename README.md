# CafeOrder · WinForms UI 목업 4차

.NET 10 / WinForms / x64. 모든 상품·가격·주문·로그인 상태는 샘플이며 실제 판매처에 접속하지 않는다. 데이터/자격정보를 저장·전송하지 않으며 종료 시 임시 변경은 초기화된다.

## 실행

`Run-CafeOrder.cmd`를 더블클릭한다. 작업 폴더의 로컬 SDK 또는 PATH의 .NET 10 SDK를 사용한다.

```powershell
dotnet build CafeOrder.slnx -c Release -p:Platform=x64
dotnet run --project src/CafeOrder -c Release -p:Platform=x64
```

Visual Studio에서는 CafeOrder.slnx의 src/CafeOrder를 시작 프로젝트로 선택한다.

## 화면 검토

- 카테고리/판매처 필터 아래 넓은 검색창. 정렬은 자동이며 상품명/가격은 드래그 선택과 Ctrl+C를 지원한다. 해당 텍스트 위에서도 목록 휠 스크롤이 동작한다.
- 담기는 판매처와 해당 상품을 맨 위로 이동하고 장바구니 스크롤을 상단으로 맞춘다. 해당 상품 행만 잠깐 강조한다. +/-는 순서와 스크롤·컨트롤을 유지한다. X는 즉시 삭제한다. 상단도 상품/장바구니와 같은 2열이다. 공통 토스트는 오른쪽 전용 영역에서 최대 3개가 8px 간격으로 표시되고 같은 상품 알림은 갱신된다. 입력을 통과시키고 약 2.8초 후 사라진다.
- 품절 휘핑크림은 클릭 후 재입고, 냉동 망고는 계속 품절로 표시하는 샘플이다. 실제 조회나 인위적 지연은 없다.
- 주문 확인창은 원래 장바구니의 수량/X 편집을 공유한다. 주문하기는 해당 대상, 전체 주문하기는 전체 대상의 편집을 잠근다. 샘플 전환은 UI 메시지 큐에서 진행한다. 쿠팡/네이버는 사용자 판매처 주문 클릭 후 성공/확인필요만 모사하며 실제 주문하지 않는다.
- 네이버 연동 로그인은 실제 지원 대상으로 지정된 판매처가 없어 숨긴다. 7개 판매처 모두 200px 영문 ID/PW·상태·로그인 UI를 사용하며, 비밀번호 입력 중 Caps Lock 상태를 즉시 표시한다. 상태색은 샘플이며 실제 로그인 결과가 아니다.
- URL 예: `https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000002613`. 로그인/가격 공개 여부를 조회하지 않는다. 우양은 `https://wym.example.invalid/product/1` 예시만 지원한다.

## 검증

```powershell
dotnet run --project tools/CafeOrder.UiChecks -c Release -p:Platform=x64 -- artifacts/ui-checks
```

검증기는 전체 탭 최초 표시/재진입, 컨트롤 유지, 크기변경/최대화/복원, 스크롤/복사, 담기/삭제/품절 재확인, 주문 편집/잠금, 토스트 클릭/휠/포커스/만료를 검사한다. 로그 복사 검사 후 기존 클립보드를 복원한다. 제공 아이콘/실패 fallback, 상품 행 이동·강조·스크롤, 상하 열 경계, ASCII 입력/붙여넣기, Caps Lock ON/OFF, 주문 카드 내용 높이도 검사한다. 클립보드와 테스트 스레드 키 상태는 복원한다. 결과와 WinForms 렌더 캡처·성능 수치를 지정 폴더에 저장한다. 토스트 전체 레이아웃 이미지는 투명 키/불투명도로 합성한 검토용 이미지이며 실제 데스크톱 합성 캡처는 아니다.

1280×720 Windows 11 검증이며 실제 Windows 배율 전환과 Windows 10 Enterprise LTSC 2019 x64 POS 호환성 검증을 대체하지 않는다.

아이콘 파일 매핑과 교체 방법: [Assets/Sellers](src/CafeOrder/Assets/Sellers/README.md).
