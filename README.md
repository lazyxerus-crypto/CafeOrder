# CafeOrder · WinForms UI 목업 5차

.NET 10 / WinForms / x64. 상품 조회·로그인·주문·결제는 샘플 동작이며 실제 판매처에 접속하지 않는다.

## 실행

`Run-CafeOrder.cmd`를 더블클릭한다. 작업 폴더의 로컬 SDK/패키지 캐시 또는 PATH의 .NET 10 SDK를 사용한다.

```powershell
dotnet build CafeOrder.slnx -c Release -p:Platform=x64
dotnet run --project src/CafeOrder -c Release -p:Platform=x64
```

## 이번 변경

- 토스트/예약 공간/상품추가 탭/담기 버튼 제거. 상품 카드 좌클릭으로 담고 우클릭으로 삭제·카테고리 변경·복사한다.
- 상단 상품 추가는 여러 Draft를 만든다. URL Enter는 지원 판매처의 **샘플** 데이터로 같은 카드를 전환한다. 실패는 Draft를 유지하며 미등록 Draft는 종료 시 폐기한다.
- 삭제 카드의 되돌리기는 현재 실행에서 제공한다. 삭제 상태는 저장하고 다음 실행의 목록에서 숨긴다. 주문기록은 유지한다.
- 이미지 Drop은 중심 정사각 Crop 후 WebP Quality 80으로 저장한다. M 배지와 이미지 메뉴에서 수동 이미지 여부/삭제를 확인한다.
- 설정에서 3/4/5열 및 모델리스 글자 크기 상세 설정을 즉시 적용한다. 같은 역할의 글꼴은 공통 Typography Key를 사용한다.
- xlsx 버튼은 **저장소 연결 전 안내만** 표시한다. 전달 레코드와 IProductWorkbook 연결 경계만 있으며 실제 파일 입출력은 아직 없다.

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

검증기는 별도 임시 상태 경로에서 카드 클릭/우클릭/복사/삭제/복구, Draft 성공·실패/폐기, 실제 WebP 중심 Crop, 이미지 교체 시 위치 유지, 3/4/5열과 9~24pt 범위 레이아웃, 모델리스 설정/복사, 재실행 저장·복원, 주문기록 줄바꿈/X 여백, 판매처 컨트롤 재사용과 전환 시간을 검사한다. 기존 클립보드는 복원한다. 결과/WinForms 렌더 캡처/성능 수치를 지정 폴더에 저장한다.

실제 Windows 배율 전환 및 Windows 10 Enterprise LTSC 2019 x64 POS 호환성은 별도 검증 대상이다.

이미지 처리는 [Magick.NET-Q8-x64 14.16.0](https://www.nuget.org/packages/Magick.NET-Q8-x64/14.16.0) 하나를 사용한다(동일 라이브러리의 Core 종속성 포함). 판매처 아이콘은 사용자 제공 원본이며 [매핑/교체 방법](src/CafeOrder/Assets/Sellers/README.md)을 따른다.
