# CafeOrder · WinForms UI 목업 2차

.NET 10 / WinForms / x64. 기존 1차 목업에서 화면 밀도와 갱신 방식을 개선했다. 24개 상품/가격/주문기록은 샘플이며 실제 사이트에서 가져온 정보가 아니다. DB·사이트 호출·로그인·주문·결제·저장을 하지 않는다. 종료 시 임시 변경은 초기화된다.

## 실행

Windows에서 `Run-CafeOrder.cmd`를 더블클릭한다. 작업 폴더의 로컬 SDK를 우선 사용하며, 없으면 PATH의 .NET 10 SDK를 사용한다.

```powershell
dotnet build CafeOrder.slnx -c Release -p:Platform=x64
dotnet run --project src/CafeOrder -c Release -p:Platform=x64
```

Visual Studio에서는 CafeOrder.slnx의 src/CafeOrder를 시작 프로젝트로 선택한다. MainForm.Designer.cs는 기본 폼, ProductsView/OtherPages/OrderForm은 화면, ProductCard/CartCards는 재사용 컨트롤, SampleData는 가상 데이터다.

## 확인할 동작

- 한 줄 필터와 3열 상품 카드. 검색에 `포모나`를 입력해 상품명 3줄을 확인한다. 상품 텍스트와 로그를 선택해 Ctrl+C로 복사할 수 있다.
- 메가커피 파우더 수량을 2에서 1로 줄이면 `11,500원 부족`으로 주문이 비활성화되고, 2로 되돌리면 주문하기로 바뀐다. 수량 변경은 기존 행/스크롤을 유지한다.
- 장바구니 전체 주문하기는 확인창만 연다. 확인창 전체 주문하기는 주문 가능한 공급처를 순차 진행하며, 사이트 직접 주문 카드에서는 사용자 클릭을 기다린다.
- 네이버/쿠팡 사이트 주문 버튼은 실제 브라우저 없이 주문 확인 중 → 샘플 성공/확인필요로 전환한다. 네이버 빨대는 확인불가 사례이며 제거되지 않는다. 완료 기록 버튼은 없다.
- 사이트관리의 ID/PW는 저장·전송되지 않는다. 메가커피의 네이버 연동 체크박스는 예시이며 실제 지원 확인을 뜻하지 않는다. 무료배송 기준은 숫자로 직접 입력한다. 빈 입력 중에는 마지막 유효값을 유지한다.
- 상품추가 URL 예: `https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000002613`. 사용자 제공 도메인만 문자열로 판별하며 접속하지 않는다. 로그인 필요 여부/가격 공개 여부와 무관하게 결과에는 샘플을 쓴다. 우양은 실제 주소 미제공으로 `https://wym.example.invalid/product/1` 예시를 사용한다.

## 검증

```powershell
dotnet run --project tools/CafeOrder.UiChecks -c Release -p:Platform=x64 -- artifacts/ui-checks
```

검증기는 필터/정렬/3열/정사각형, 컨트롤 재사용, 수량/비활성 주문, URL 경계, 텍스트 복사, 주문 확인/확인불가 유지와 주요 화면을 검사한다. 클립보드 검사 후 기존 내용을 복원한다. 성능 측정만 수행하려면 마지막에 `--perf`를 추가한다.

1280×720 및 125%/150% 확대 레이아웃을 캡처한다. 이는 Windows 실제 배율 전환이나 POS 호환성 검증을 대체하지 않는다. Windows 10 Enterprise LTSC 2019 x64 POS 호환성은 미검증이다.
