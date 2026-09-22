# CafeOrder · WinForms UI 7차 + SQLite + XLSX

.NET 10 / WinForms / x64. 상품 조회·로그인·주문·결제는 샘플 동작이며 자동으로 실제 판매처에 접속하지 않는다. 상품 링크 메뉴와 홈페이지가 설정된 판매처 아이콘만 Windows 기본 브라우저에 URL을 전달한다.

## 실행

`Run-CafeOrder.cmd`를 더블클릭한다. 작업 폴더의 로컬 SDK/패키지 캐시 또는 PATH의 .NET 10 SDK를 사용한다.

```powershell
dotnet build CafeOrder.slnx -c Release -p:Platform=x64
dotnet run --project src/CafeOrder -c Release -p:Platform=x64
```

## 7차 UI 기준

- 열 개수 변경 시 카드 전체를 즉시 다시 그려 축소 후 남는 이전 이미지/글자를 제거한다. 기존 카드를 재사용한다.
- 상단 왼쪽은 카테고리/판매처·검색, 오른쪽은 관리 버튼 4개·격자 버튼 3개다. 본문과 같은 66:34 경계/간격을 사용한다.
- 현재 카테고리 하나만 체크한다. 한 줄 영역은 말줄임/원문 Tooltip, 상품명은 최대 3줄, 주문기록 상품명은 전체 자동 높이를 유지한다.
- 모든 판매처 아이콘은 공통 홈페이지 설정을 사용한다. `SellerLinks.cs`의 `Homepages`는 확정 주소 미제공으로 모두 미설정이다. 네이버 상품은 정확한 상품 URL에서 식별되는 스토어 홈만 연결한다. 아이콘 클릭은 장바구니 추가와 분리한다.
- 기존 Draft/목록 갱신/이미지/복사/로컬 설정 동작을 유지하며 실제 사이트 조회·로그인·주문은 구현하지 않는다.

## 로컬 상태

`%LOCALAPPDATA%\CafeOrder\Mockup`:

- `ui-state.json`: 열/글자 크기 즉시 저장, 창 위치·크기·최대화는 종료 시 저장. 화면 밖/최소 크기 미만 값은 기본 위치로 복원한다.
- `catalog-state.json`: 기존 상품 변경 내용의 1회 이전 자료. 원본은 유지하며 이후 쓰지 않는다.
- `manual-images/`: 사용자 지정 WebP. 기존 파일은 보존하고 새 파일은 고유 이름으로 저장한다.

`%LOCALAPPDATA%\CafeOrder\Data\CafeOrder.db`는 상품/장바구니의 실제 저장소다. 기본 예시 24개는 수정·담기 전까지 DB에 넣지 않는다. 첫 실행 시 수정된 예시/등록 상품/수동 이미지 경로를 한 번만 이전한다. 빈 Draft·주문기록·ID/PW는 저장하지 않는다. 백업/마이그레이션은 [DATABASE](docs/DATABASE.md)를 따른다.

상품 탭의 내보내기/가져오기는 저장된 상품만 XLSX로 편집한다. 가져오기는 파일 전체 검증과 변경 건수 확인 후 백업·일괄 반영하며, 오류가 있으면 DB를 바꾸지 않는다.

## 검증

```powershell
dotnet run --project tools/CafeOrder.UiChecks -c Release -p:Platform=x64 -- artifacts/ui-checks
```

검증기는 별도 임시 상태 경로에서 SQLite 이전·재실행, XLSX 검증·백업·롤백 및 기존 7차 UI 회귀 검사를 수행한다. 기존 클립보드는 복원한다. 결과/WinForms 렌더 캡처/성능 수치를 지정 폴더에 저장한다.

실제 Windows 배율 전환 및 Windows 10 Enterprise LTSC 2019 x64 POS 호환성은 별도 검증 대상이다.

이미지 처리는 [Magick.NET-Q8-x64 14.16.0](https://www.nuget.org/packages/Magick.NET-Q8-x64/14.16.0) 하나를 사용한다(동일 라이브러리의 Core 종속성 포함). 판매처 아이콘은 사용자 제공 원본이며 [매핑/교체 방법](src/CafeOrder/Assets/Sellers/README.md)을 따른다.

상품 XLSX 입출력에는 [ClosedXML 0.105.1](https://www.nuget.org/packages/ClosedXML/0.105.1)을 사용한다.
