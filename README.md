# RO Market Crawler

GNJOY Ragnarok Online 아이템 시세 크롤러 - **이벤트 기반 실시간 아키텍처**

## 핵심 특징

- **온디맨드 크롤링**: 요청 시에만 GNJOY에서 데이터 수집 (스케줄러 없음)
- **스마트 캐싱**: 검색 60초, Top5 5분 TTL 캐시
- **WebSocket 실시간**: 아이템 구독 및 실시간 업데이트
- **자동 히스토리**: 모든 검색 결과 자동 저장

## 아키텍처

```
[클라이언트 A] ─────────────────────────────────────┐
     │                                              │
     │ GET /items/search?name=엘더윌로우카드          │
     ▼                                              │
[서버] ─── 캐시 확인 ─── Miss ─── GNJOY 크롤링      │
     │         │                      │             │
     │       Hit                      ▼             │
     │         │              캐시 저장 (60s)        │
     │         │                      │             │
     │         ▼                      ▼             │
     │    즉시 반환              DB 저장 (히스토리)   │
     │                                │             │
     │                                ▼             │
     │                    WebSocket broadcast ──────┘
     │                                │
     │                                ▼
     │                         [구독자 B, C, D]
     │                              실시간 수신
     ▼
[응답 반환]
```

## 장점

| 기존 방식 (스케줄러) | 새로운 방식 (온디맨드) |
|---------------------|----------------------|
| 5분마다 무조건 크롤링 | 요청 시에만 크롤링 |
| 서버 리소스 낭비 | 사용량에 비례한 리소스 |
| 고정된 지연 시간 | 캐시 내 최신 데이터 |
| 단방향 | WebSocket 양방향 실시간 |

## 설치

```bash
cd C:\Git\ro-market-crawler

# 가상환경
python -m venv venv
venv\Scripts\activate

# 의존성
pip install -r requirements.txt

# 환경 설정
copy .env.example .env
```

## 실행

```bash
python -m src.main
```

- API 문서: http://localhost:8000/docs
- WebSocket: ws://localhost:8000/ws

## API 엔드포인트

### 아이템 검색 (실시간)
```bash
GET /api/v1/items/search?name=엘더윌로우카드&server_id=-1
```
- 캐시 히트: 즉시 반환 (<10ms)
- 캐시 미스: GNJOY 크롤링 후 반환 + 캐시 저장
- `force_refresh=true`: 캐시 무시하고 새로 크롤링

### Top5 인기 아이템
```bash
GET /api/v1/items/top5?category=W
```
- 카테고리: W(무기), D(방어구), C(소비), E(기타)
- 5분 캐시 TTL

### 가격 히스토리
```bash
GET /api/v1/items/history?name=엘더윌로우카드
GET /api/v1/items/average?name=엘더윌로우카드&days=7
```

### 캐시 관리
```bash
GET /api/v1/cache/stats    # 캐시 통계
POST /api/v1/cache/clear   # 캐시 초기화
```

## WebSocket 사용법

### 연결
```javascript
const ws = new WebSocket('ws://localhost:8000/ws');

ws.onmessage = (event) => {
    const data = JSON.parse(event.data);
    console.log('Received:', data);
};
```

### 아이템 구독
```javascript
// 특정 아이템 구독
ws.send(JSON.stringify({
    action: "subscribe",
    item_name: "엘더윌로우카드",
    server_id: -1  // -1: 전체 서버
}));

// 구독 해제
ws.send(JSON.stringify({
    action: "unsubscribe",
    item_name: "엘더윌로우카드",
    server_id: -1
}));

// 연결 상태 확인
ws.send(JSON.stringify({ action: "ping" }));
ws.send(JSON.stringify({ action: "status" }));
```

### 서버 메시지 타입
```javascript
// 연결 성공
{ "type": "connected", "client_id": "abc123" }

// 구독 확인
{ "type": "subscribed", "item_name": "...", "server_id": -1 }

// 아이템 업데이트 (다른 사용자가 검색 시)
{
    "type": "item_update",
    "item_name": "엘더윌로우카드",
    "server_id": -1,
    "data": { "items": [...], "count": 5 }
}

// Top5 업데이트
{ "type": "top5_update", "data": {...} }
```

## 서버 ID

| ID | 서버명 |
|----|--------|
| -1 | 전체 |
| 1 | 바포메트 |
| 2 | 이그드라실 |
| 3 | 다크로드 |
| 4 | 이프리트 |

## 프로젝트 구조

```
ro-market-crawler/
├── src/
│   ├── main.py              # FastAPI + WebSocket
│   ├── config.py            # 설정
│   ├── cache.py             # TTL 캐시
│   ├── crawler/
│   │   ├── gnjoy_client.py  # GNJOY API 클라이언트
│   │   └── parser.py        # HTML 파서
│   ├── models/
│   │   └── item.py          # 데이터 모델
│   ├── database/
│   │   └── repository.py    # SQLite
│   ├── api/
│   │   └── routes.py        # REST API (온디맨드)
│   ├── websocket/
│   │   └── manager.py       # WebSocket 관리
│   └── scheduler/           # (선택적, 미사용)
│       └── jobs.py
├── data/                    # SQLite DB
├── requirements.txt
└── README.md
```

## 데이터 흐름

1. **클라이언트 A**가 "엘더윌로우카드" 검색
2. **서버**: 캐시 확인 → Miss → GNJOY 크롤링
3. **서버**: 결과 반환 + 캐시 저장 + DB 저장 + WebSocket broadcast
4. **구독자 B, C**: WebSocket으로 실시간 수신
5. **클라이언트 D**가 같은 아이템 검색 (30초 후)
6. **서버**: 캐시 확인 → Hit → 즉시 반환 (크롤링 없음)

## 라이선스

MIT License
