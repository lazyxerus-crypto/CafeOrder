# 데이터와 저장 기준 (schema 구현 전)

CafeOrder는 상품 데이터 수집 프로그램이 아니다. 사이트에 표시된 상품명 문자열을 그대로 기본 상품명으로 사용하고 필요한 최소 정보만 저장한다.

예: `포모나 코코렛 파우더 800g 2개세트 + 회원 구매시 560원 할인` / 가격 표시: `14,500원 (메가회원가)`.

## 상품 정보의 기준 범위

`ProductId`, `SupplierId`, `ProductName`, `Price`, `PriceText`, `ProductUrl`, `ImageUrl`, `ImageCachePath`, `Category`, `AvailabilityStatus`, `LastCheckedAt`, `LastSuccessfulCheckAt`, `IsActive` 및 실제 주문 식별에 필요한 최소 외부 상품 ID.

이는 설계 기준이며 실제 테이블/타입/schema는 이번에 구현하지 않는다. 외부 ID는 주문에 실제로 필요한 경우에만 추가한다. 브랜드, 맛, 중량, 용량, 지름, 재질, 상품종류, 기타 상세 사양을 상품명에서 분석해 과도한 별도 DB 필드로 나누지 않는다. 검색 시 정규화는 [SEARCH](SEARCH.md), 카테고리는 [UI](UI.md)를 따른다.

## 저장과 백업

- 장바구니는 SQLite에 즉시 저장되는 로컬 작업목록이다. 중요한 쓰기는 직렬화하고 트랜잭션은 짧게 유지한다.
- 주문 스냅샷/체크포인트 및 완료 기록은 [ORDER_FLOW](ORDER_FLOW.md)의 안전 순서를 따른다.
- 자동 백업 기본 방향: 하루 1회, 프로그램 업데이트 전, DB schema migration 전, 데이터 import 전.
- 로그인 자격정보와 브라우저 세션은 일반 데이터 백업/내보내기에 그대로 포함하지 않는다.

## 보안 경계

ID/PW가 필요하면 Windows 보안 저장소를 사용한다. SQLite/settings.json 평문 비밀번호 저장 금지. 로그에 비밀번호, 쿠키, token을 기록하지 않는다.

실사용 SQLite DB 및 부속 파일, DB 백업, 로그, diagnostic zip, 브라우저 프로필, 쿠키/세션/cache, ID/PW, token, secret, 민감한 로컬 설정을 Git/GitHub에 올리지 않는다. `.gitignore`는 예방 장치이므로 commit 전 staged 파일도 확인한다. 브라우저 로그인 상태의 로컬 보관 방식은 ORDER_FLOW를 따른다.
