# CafeOrder · WinForms UI 목업 7차

.NET 10 / WinForms / x64. 상품 조회·로그인·주문·결제는 샘플 동작이며 자동으로 실제 판매처에 접속하지 않는다. 상품 링크 메뉴와 홈페이지가 설정된 판매처 아이콘만 Windows 기본 브라우저에 URL을 전달한다.

## 실행

`Run-CafeOrder.cmd`를 더블클릭한다. 작업 폴더의 로컬 SDK/패키지 캐시 또는 PATH의 .NET 10 SDK를 사용한다.

```powershell
dotnet build CafeOrder.slnx -c Release -p:Platform=x64
dotnet run --project src/CafeOrder -c Release -p:Platform=x64
```

## 이번 변경

- 열 개수 변경 시 카드 전체를 즉시 다시 그려 축소 후 남는 이전 이미지/글자를 제거한다. 기존 카드를 재사용한다.
- 상단 왼쪽은 카테고리/판매처·검색, 오른쪽은 관리 버튼 4개·격자 버튼 3개다. 본문과 같은 66:34 경계/간격을 사용한다.
- 현재 카테고리 하나만 체크한다. 한 줄 영역은 말줄임/원문 Tooltip, 상품명은 최대 3줄, 주문기록 상품명은 전체 자동 높이를 유지한다.
- 모든 판매처 아이콘은 공통 홈페이지 설정을 사용한다. `SellerLinks.cs`의 `Homepages`는 확정 주소 미제공으로 모두 미설정이다. 네이버 상품은 정확한 상품 URL에서 식별되는 스토어 홈만 연결한다. 아이콘 클릭은 장바구니 추가와 분리한다.
- 기존 Draft/목록 갱신/이미지/복사/로컬 설정 동작을 유지하며 실제 사이트 조회·로그인·주문은 구현하지 않는다.

## 로컬 상태

`%LOCALAPPDATA%\CafeOrder\Mockup`:

- `ui-state.json`: 열/글자 크기 즉시 저장, 창 위치·크기·최대화는 종료 시 저장. 화면 밖/최소 크기 미만 값은 기본 위치로 복원한다.
- `catalog-state.json`: 등록 샘플 상품·카테고리·IsActive. 빈 Draft/주문기록/장바구니/ID/PW는 저장하지 않는다.
- `manual-images/{ProductId}.webp`: 사용자가 지정한 수동 이미지. 기존 샘플 이미지는 변경하지 않는다.

SQLite 본 구현은 없다. 임시 JSON은 향후 저장소 연결 전 목업 상태만 유지한다. 자격정보는 저장·전송하지 않는다.

## 검증

```powershell
dotnet run --project tools/CafeOrder.UiChecks -c Release -p:Platform=x64 -- artifacts/ui-checks
```

검증기는 별도 임시 상태 경로에서 열 변경 직후 Win32 다시 그리기 영역·두 열 경계·카테고리 체크·홈페이지 실행 경계/미설정·아이콘 클릭 수량 유지, 카드 클릭/우클릭/복사/삭제/복구, Draft 성공·실패/폐기, 실제 WebP 중심 Crop, 이미지 교체 시 위치 유지, 3/4/5열과 9~24pt 범위 레이아웃, 모델리스 설정/복사, 재실행 저장·복원, 주문기록 줄바꿈/X 네 면 렌더 픽셀, 장바구니 누락 재현·복구, URL 등록 위치 유지, 로컬 갱신/스크롤바 폭, Tooltip 팝업 발생, 글자크기 기본값 복원, 판매처 컨트롤 재사용과 전환 시간을 검사한다. 기존 클립보드는 복원한다. 결과/WinForms 렌더 캡처/성능 수치를 지정 폴더에 저장한다.

실제 Windows 배율 전환 및 Windows 10 Enterprise LTSC 2019 x64 POS 호환성은 별도 검증 대상이다.

이미지 처리는 [Magick.NET-Q8-x64 14.16.0](https://www.nuget.org/packages/Magick.NET-Q8-x64/14.16.0) 하나를 사용한다(동일 라이브러리의 Core 종속성 포함). 판매처 아이콘은 사용자 제공 원본이며 [매핑/교체 방법](src/CafeOrder/Assets/Sellers/README.md)을 따른다.
