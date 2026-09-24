# 데이터와 저장 기준 (SQLite 1단계)

CafeOrder는 상품 데이터 수집 프로그램이 아니다. 사이트에 표시된 상품명 문자열을 그대로 기본 상품명으로 사용하고 필요한 최소 정보만 저장한다.

예: `포모나 코코렛 파우더 800g 2개세트 + 회원 구매시 560원 할인` / 가격 표시: `14,500원 (메가회원가)`.

## 상품 정보의 기준 범위

`ProductId`, `SupplierId`, `ProductName`, `Price`, `PriceText`, `ProductUrl`, `ResolvedProductUrl`, `ImageUrl`, `ImageCachePath`, `Category`, `AvailabilityStatus`, `LastCheckedAt`, `LastSuccessfulCheckAt`, `IsActive` 및 실제 주문 식별에 필요한 최소 외부 상품 ID.

상품별 마지막 성공 조회 시각 `LastSuccessfulCheckAtUtc`는 SQLite에 UTC로 저장한다. 실패한 조회는 이 값을 바꾸지 않는다. 외부 상품 ID는 사이트 장바구니 대상 스냅샷에 저장한다. 브랜드, 맛, 중량, 용량, 지름, 재질, 상품종류 등은 별도 열로 나누지 않는다.

## SQLite 1단계

DB는 `%LOCALAPPDATA%\CafeOrder\Data\CafeOrder.db`에 둔다. `SchemaMigrations`는 적용한 버전을 기록한다. `Suppliers`는 안정적인 판매처 ID·이름·수동 여부를, `Products`는 안정적인 상품 ID·판매처 ID·원본 상품명·가격/표시가격/가격 주석·카테고리·상품 URL·이미지 URL/캐시/수동 경로·품절/삭제 여부·데이터 출처를 저장한다. `CartItems`는 상품 ID·수량·최근 추가 순서를 저장한다. 외래 키를 검사하며 여러 레코드가 함께 바뀌는 저장은 트랜잭션을 사용한다.

기본 24개 예시 상품은 화면에만 생성한다. 사용자가 수정하거나 장바구니에 담은 예시만 DB에 기록한다. URL로 등록한 상품은 기존 `UserMock` 출처값을 사용하고, 수정된 예시는 `Sample`로 구분한다. 실제 메가커피·파미유·널담 조회 상품은 URL·이미지 캐시 경로로 목업 등록 상품과 구별하며 스키마는 변경하지 않는다. 저장된 적 없는 예시 장바구니는 DB에 넣지 않는다. 미완성 Draft도 저장하지 않는다. 장바구니 추가/수량 변경/제거와 상품 등록/카테고리/삭제·되돌리기/수동 이미지 경로/샘플 품절 재확인은 DB에 즉시 반영한다. AUTO 수량 변경은 웹 요청을 하지 않는다.

첫 실행에서는 기존 `Mockup/catalog-state.json`의 수정된 예시와 등록 상품, 기존 `manual-images/{ProductId}.webp`를 한 번만 가져온다. 기존 JSON·WebP·`ui-state.json`은 읽기 전용으로 보존한다. DB가 이미 전환을 마쳤으면 JSON을 재적용하지 않는다. 손상된 JSON이나 알 수 없는 DB 버전은 전환을 멈추고 원본을 유지한다. 기존 DB 스키마 변경 전과 JSON 전환 전에는 `Data/backups`에 SQLite 백업을 만든다. 수동 이미지 신규 파일은 고유 이름을 사용하여 기존 파일을 덮어쓰지 않는다.

## 저장과 백업

- 장바구니는 SQLite에 즉시 저장되는 로컬 작업목록이다. 중요한 쓰기는 직렬화하고 트랜잭션은 짧게 유지한다.
- 주문 스냅샷/체크포인트 및 완료 기록은 [ORDER_FLOW](ORDER_FLOW.md)의 안전 순서를 따른다.
- 스키마 3은 `Products.LastSuccessfulCheckAtUtc`, `SiteCartAttempts`, `SiteCartAttemptItems`를 추가한다. 원격 장바구니를 바꾸기 전에 판매처·ProductId·외부 상품 ID·URL·옵션·수량·가격을 한 트랜잭션으로 저장한다. 준비/대기/검증/실패/불명 상태도 DB에 남기며 사이트 장바구니 준비를 주문 완료로 기록하지 않는다.
- 스키마 4는 쿠팡 단축 링크의 원본 `ProductUrl`을 유지하면서 확인된 이동 상품 주소를 `ResolvedProductUrl`에 별도로 보관한다. 변경 전 DB를 백업한다.
- 스키마 5는 `Products.PriceKnown`을 추가한다. 쿠팡·네이버 직접입력에서는 정상 상품 URL만으로 이름·가격·사진이 빈 상품을 저장할 수 있다. `PriceKnown=false`와 실제 `0원`은 다르며, 금액 미입력 항목이 포함된 합계·무료배송 금액은 미확정으로 표시한다. 변경 전 DB를 백업한다.
- 자동 백업 장기 방향: 하루 1회, 프로그램 업데이트 전. 현재 구현: DB schema migration 전과 기존 데이터 import 전.
- 로그인 자격정보와 브라우저 세션은 일반 데이터 백업/내보내기에 그대로 포함하지 않는다.

## 보안 경계

ID/PW가 필요하면 Windows 보안 저장소를 사용한다. SQLite/settings.json 평문 비밀번호 저장 금지. 로그에 비밀번호, 쿠키, token을 기록하지 않는다.

실사용 SQLite DB 및 부속 파일, DB 백업, 로그, diagnostic zip, 브라우저 프로필, 쿠키/세션/cache, ID/PW, token, secret, 민감한 로컬 설정을 Git/GitHub에 올리지 않는다. `.gitignore`는 예방 장치이므로 commit 전 staged 파일도 확인한다. 브라우저 로그인 상태의 로컬 보관 방식은 ORDER_FLOW를 따른다.

## 남은 목업 상태

`ui-state.json`은 창/열/글자 크기를 계속 저장한다. `catalog-state.json`은 이전 자료로만 읽고 새로 쓰지 않는다. 주문 완료·결제·배송 연동은 구현하지 않는다. MegaCoffee·PieceCake·Nuldam 로그인 세션은 판매처별 브라우저 프로필에 저장하며 SQLite에는 넣지 않는다.

기존 `manual-images/{ProductId}.webp`는 원본대로 두고, 새 수동·웹·XLSX 이미지는 EXIF 방향 보정 → 짧은 변 기준 중앙 정사각 Crop → 800px 초과 시에만 800×800 축소 → 메타데이터 제거 → 품질 80 WebP로 저장한다. 작은 이미지는 확대하지 않고 투명도는 유지한다. DB에는 파일 경로만 보관한다.
실제 조회 상품 이미지는 `Mockup/web-images`에 같은 WebP 규칙으로 저장하며 수동 이미지가 우선한다.
상품 카드·장바구니·주문 진행·목업 주문기록은 수동 이미지 → 저장된 웹 이미지 → 카테고리 placeholder 순서로 로컬 파일만 표시한다.

## XLSX 상품 입출력

상품 탭의 기존 버튼은 SQLite `Products`에 저장된 상품만 `ProductId`, `Supplier`, `Name`, `Price`, `DisplayPrice`, `Category`, `ProductUrl`, `IsActive` 순서로 내보낸다. 삭제 상품은 `IsActive=false`로 포함하고, 미저장 샘플·Draft·장바구니·수동 이미지/내부 경로는 제외한다. 라이브러리는 ClosedXML이다.

직접입력한 쿠팡·네이버 상품의 미입력 이름·가격은 XLSX에서도 빈 셀로 보존한다. 판매처·분류·활성 상태가 있는 완성형 행은 조회를 강제하지 않으며, URL만 있는 행은 기존 상품조회 경로를 따른다. 빈 가격을 `0원`으로 확정하지 않는다.

가져오기는 전체 행의 값·판매처·카테고리·URL·ID 중복/존재 여부를 먼저 검증한다. 빈 ID는 새 ID를 발급하고 기존 ID는 그대로 수정한다. 파일에 없는 상품은 삭제하지 않는다. 사용자 확인 후 `Data/backups`에 SQLite 백업을 만들고 모든 변경을 한 트랜잭션으로 반영한다. 실패 시 전체 롤백하며, 기존 상품의 수동 이미지 경로와 XLSX에 없는 내부 값은 유지한다. XLSX는 실행 DB가 아니다.

필수 칸이 비고 `ProductUrl`이 있는 행은 지원되는 MegaCoffee·PieceCake·Nuldam·쿠팡·네이버 상품 URL을 조회한다. 검증된 상품명·화면 표시 가격·이미지·품절 상태를 사용하고, 카테고리는 분명한 명칭만 자동 분류하며 모호하면 `기타`로 둔다. 쿠팡·네이버는 접근 제한·불확실한 가격·이미지 실패 시 행별 오류로 중단하고 더미 상품을 만들지 않는다. 모든 칸이 채워진 일반 행은 기존 방식대로 웹 조회 없이 처리한다. 중복은 판매처와 실제 상품 코드(`goodsNo`/`prodNo`/`product_no`), 쿠팡 `vendorItemId`, 네이버 스토어명+상품 ID로 조회 전에 판별한다. DB의 다른 ProductId 또는 파일 앞 유효 행과 같은 상품이면 건너뛰며 기존 소유 ID를 결과·로그에 표시한다. 같은 ProductId/상품 URL의 실제 정보 변경은 수정하고 변경이 없으면 건너뛴다. 기존 ProductId 행에 다른 신규 상품 URL을 넣으면 기존 상품을 보존하고 조회 후 새 ID로 등록한다. 기존 상품 수정의 수동 이미지는 보존한다. 조회 이미지는 확인 전 임시 저장 후 취소/오류 시 정리하며, 확인 후 백업과 단일 트랜잭션으로 반영한다. 지원하지 않는 조회 URL·실제 오류가 있으면 DB를 바꾸지 않는다.

실제 MegaCoffee·PieceCake·Nuldam URL 상품을 카드에서 담으면 로컬 행을 먼저 저장·표시한다. 마지막 성공 조회가 없거나 24시간 이상 지났으면 로그인 세션으로 해당 상품만 비동기 재조회한다. URL 등록·XLSX 조회·장바구니 재조회 성공도 시각을 갱신한다. 조회 중 같은 카드 재클릭은 조회를 중복 실행하지 않으며 장바구니 +/-는 언제나 웹을 호출하지 않는다. 조회 성공 시 상품명·가격·표시가격·저장 웹 이미지·품절 상태를 SQLite와 상품/장바구니 화면에 반영하되 수동 이미지를 우선한다. 조회 실패 또는 담은 행 제거 시 기존 저장값·수량·성공 시각을 유지하며 제거된 행을 되살리지 않는다. 조회 중·실패 또는 품절 상품이 포함되면 장바구니의 주문 버튼은 비활성화한다. 상품 탭 새로고침은 계속 로컬 필터·정렬만 수행한다.

## 운영 로그

실제 앱·DB·상품·장바구니·XLSX·로그인·조회·이미지 실패 이벤트는 `%LOCALAPPDATA%\CafeOrder\Logs\CafeOrder.log`에 비동기로 기록한다. 1 MB 파일과 보관본 3개를 순환하며 로그 탭은 최근 기록을 다시 읽고 새 이벤트를 표시한다. URL·비밀번호·쿠키·토큰·인증 내용을 기록하지 않는다.
