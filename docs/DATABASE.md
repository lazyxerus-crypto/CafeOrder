# 데이터와 저장 기준 (SQLite 1단계)

CafeOrder는 상품 데이터 수집 프로그램이 아니다. 사이트에 표시된 상품명 문자열을 그대로 기본 상품명으로 사용하고 필요한 최소 정보만 저장한다.

예: `포모나 코코렛 파우더 800g 2개세트 + 회원 구매시 560원 할인` / 가격 표시: `14,500원 (메가회원가)`.

## 상품 정보의 기준 범위

`ProductId`, `SupplierId`, `ProductName`, `Price`, `PriceText`, `ProductUrl`, `ImageUrl`, `ImageCachePath`, `Category`, `AvailabilityStatus`, `LastCheckedAt`, `LastSuccessfulCheckAt`, `IsActive` 및 실제 주문 식별에 필요한 최소 외부 상품 ID.

이는 장기 설계 기준이다. 1단계는 상품/장바구니에 실제 필요한 필드만 구현한다. 검사 시각·외부 상품 ID는 실제 조회/주문 단계에 추가한다. 브랜드, 맛, 중량, 용량, 지름, 재질, 상품종류 등은 별도 열로 나누지 않는다.

## SQLite 1단계

DB는 `%LOCALAPPDATA%\CafeOrder\Data\CafeOrder.db`에 둔다. `SchemaMigrations`는 적용한 버전을 기록한다. `Suppliers`는 안정적인 판매처 ID·이름·수동 여부를, `Products`는 안정적인 상품 ID·판매처 ID·원본 상품명·가격/표시가격/가격 주석·카테고리·상품 URL·이미지 URL/캐시/수동 경로·품절/삭제 여부·데이터 출처를 저장한다. `CartItems`는 상품 ID·수량·최근 추가 순서를 저장한다. 외래 키를 검사하며 여러 레코드가 함께 바뀌는 저장은 트랜잭션을 사용한다.

기본 24개 예시 상품은 화면에만 생성한다. 사용자가 수정하거나 장바구니에 담은 예시만 DB에 기록한다. URL로 등록한 상품은 `UserMock`, 수정된 예시는 `Sample`로 구분한다. 저장된 적 없는 예시 장바구니는 DB에 넣지 않는다. 미완성 Draft도 저장하지 않는다. 장바구니 추가/수량 변경/제거와 상품 등록/카테고리/삭제·되돌리기/수동 이미지 경로/샘플 품절 재확인은 DB에 즉시 반영한다. AUTO 수량 변경은 웹 요청을 하지 않는다.

첫 실행에서는 기존 `Mockup/catalog-state.json`의 수정된 예시와 등록 상품, 기존 `manual-images/{ProductId}.webp`를 한 번만 가져온다. 기존 JSON·WebP·`ui-state.json`은 읽기 전용으로 보존한다. DB가 이미 전환을 마쳤으면 JSON을 재적용하지 않는다. 손상된 JSON이나 알 수 없는 DB 버전은 전환을 멈추고 원본을 유지한다. 기존 DB 스키마 변경 전과 JSON 전환 전에는 `Data/backups`에 SQLite 백업을 만든다. 수동 이미지 신규 파일은 고유 이름을 사용하여 기존 파일을 덮어쓰지 않는다.

## 저장과 백업

- 장바구니는 SQLite에 즉시 저장되는 로컬 작업목록이다. 중요한 쓰기는 직렬화하고 트랜잭션은 짧게 유지한다.
- 주문 스냅샷/체크포인트 및 완료 기록은 [ORDER_FLOW](ORDER_FLOW.md)의 안전 순서를 따른다.
- 자동 백업 장기 방향: 하루 1회, 프로그램 업데이트 전. 현재 구현: DB schema migration 전과 기존 데이터 import 전.
- 로그인 자격정보와 브라우저 세션은 일반 데이터 백업/내보내기에 그대로 포함하지 않는다.

## 보안 경계

ID/PW가 필요하면 Windows 보안 저장소를 사용한다. SQLite/settings.json 평문 비밀번호 저장 금지. 로그에 비밀번호, 쿠키, token을 기록하지 않는다.

실사용 SQLite DB 및 부속 파일, DB 백업, 로그, diagnostic zip, 브라우저 프로필, 쿠키/세션/cache, ID/PW, token, secret, 민감한 로컬 설정을 Git/GitHub에 올리지 않는다. `.gitignore`는 예방 장치이므로 commit 전 staged 파일도 확인한다. 브라우저 로그인 상태의 로컬 보관 방식은 ORDER_FLOW를 따른다.

## 남은 목업 상태

`ui-state.json`은 창/열/글자 크기를 계속 저장한다. `catalog-state.json`은 이전 자료로만 읽고 새로 쓰지 않는다. 주문기록·배송/주문 진행·로그인/자격정보·사이트 자동화는 이 단계에서 저장/구현하지 않는다.

기존 `manual-images/{ProductId}.webp`는 원본대로 두고, 새 수동 이미지는 고유 파일명 WebP(중심 정사각 Crop/Quality 80)로 저장한다. DB에는 파일 경로만 보관한다.

## XLSX 상품 입출력

상품 탭의 기존 버튼은 SQLite `Products`에 저장된 상품만 `ProductId`, `Supplier`, `Name`, `Price`, `DisplayPrice`, `Category`, `ProductUrl`, `IsActive` 순서로 내보낸다. 삭제 상품은 `IsActive=false`로 포함하고, 미저장 샘플·Draft·장바구니·수동 이미지/내부 경로는 제외한다. 라이브러리는 ClosedXML이다.

가져오기는 전체 행의 값·판매처·카테고리·URL·ID 중복/존재 여부를 먼저 검증한다. 빈 ID는 새 ID를 발급하고 기존 ID는 그대로 수정한다. 파일에 없는 상품은 삭제하지 않는다. 사용자 확인 후 `Data/backups`에 SQLite 백업을 만들고 모든 변경을 한 트랜잭션으로 반영한다. 실패 시 전체 롤백하며, 기존 상품의 수동 이미지 경로와 XLSX에 없는 내부 값은 유지한다. XLSX는 실행 DB가 아니다.
