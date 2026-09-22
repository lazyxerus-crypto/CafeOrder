# MegaCoffee

- 유형: AUTO.
- 무료배송 초기 설정값: 50,000원.
- 주문/로그인/무료배송 공통 규칙: [ORDER_FLOW](../ORDER_FLOW.md).
- 로그인 1단계: 공식 홈 `https://www.megacoffee.co.kr/`에 설치된 Edge로 접속한다. `a[href*='logout.php']`가 있으면 로그인 완료, `#formLogin #loginId` 또는 로그인 링크가 있으면 로그인 필요로 판별한다. 둘 다 확인할 수 없으면 사용자 확인 대기이며 URL만으로 성공을 추정하지 않는다. 주문·상품 조회 자동화는 구현하지 않는다.
