# CafeOrder · WinForms UI 목업 6차

.NET 10 / WinForms / x64. 상품 조회·로그인·주문·결제는 샘플 동작이며 자동으로 실제 판매처에 접속하지 않는다. 상품 링크 메뉴만 Windows 기본 브라우저에 URL을 전달한다.

## 실행

`Run-CafeOrder.cmd`를 더블클릭한다. 작업 폴더의 로컬 SDK/패키지 캐시 또는 PATH의 .NET 10 SDK를 사용한다.

```powershell
dotnet build CafeOrder.slnx -c Release -p:Platform=x64
dotnet run --project src/CafeOrder -c Release -p:Platform=x64
```

## 이번 변경

- 상품 상단을 왼쪽 열에 제한하고 로컬 새로고침·3/4/5열 격자 버튼을 배치했다. 가격은 오른쪽 한 줄 말줄임, 카테고리는 Bold다.
- 수동 판매처 카드의 0 높이 배치를 고쳐 장바구니 누락을 해결했다. 주문창 X와 가격 행의 겹침을 제거했다.
- Draft/이미지를 포함한 공통 우클릭 메뉴에 이미지 삭제·상품 링크를 제공한다. URL 등록은 같은 카드 위치를 유지하고 명시적인 필터/검색/새로고침만 정렬한다.
- 품절 붉은 카드·클릭 후 Tooltip·스크롤바 예약 폭·글자크기 기본값 복원을 적용했다.
- 실제 DB/로그인/주문/상품 조회와 xlsx 파일 입출력은 이번 범위에 포함하지 않는다.

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

검증기는 별도 임시 상태 경로에서 카드 클릭/우클릭/복사/삭제/복구, Draft 성공·실패/폐기, 실제 WebP 중심 Crop, 이미지 교체 시 위치 유지, 3/4/5열과 9~24pt 범위 레이아웃, 모델리스 설정/복사, 재실행 저장·복원, 주문기록 줄바꿈/X 네 면 렌더 픽셀, 장바구니 누락 재현·복구, URL 등록 위치 유지, 로컬 갱신/스크롤바 폭, Tooltip 팝업 발생, 글자크기 기본값 복원, 판매처 컨트롤 재사용과 전환 시간을 검사한다. 기존 클립보드는 복원한다. 결과/WinForms 렌더 캡처/성능 수치를 지정 폴더에 저장한다.

실제 Windows 배율 전환 및 Windows 10 Enterprise LTSC 2019 x64 POS 호환성은 별도 검증 대상이다.

이미지 처리는 [Magick.NET-Q8-x64 14.16.0](https://www.nuget.org/packages/Magick.NET-Q8-x64/14.16.0) 하나를 사용한다(동일 라이브러리의 Core 종속성 포함). 판매처 아이콘은 사용자 제공 원본이며 [매핑/교체 방법](src/CafeOrder/Assets/Sellers/README.md)을 따른다.
