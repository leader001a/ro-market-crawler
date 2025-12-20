# RO Market Crawler

GNJOY/kafra.kr 라그나로크 오리진 아이템 시세 조회 도구 (C# WinForms)

## 다운로드

📦 **[최신 버전 다운로드](../../releases/latest)**

- Windows 10/11 64비트 전용
- .NET 런타임 내장 (별도 설치 불필요)
- 단일 실행 파일 (약 69MB)

### 파일 검증

다운로드한 파일이 원본인지 확인하려면 SHA256 해시를 비교하세요.
해시 값은 각 [릴리스 페이지](../../releases)에서 확인할 수 있습니다.

**Windows PowerShell에서 확인:**
```powershell
Get-FileHash RoMarketCrawler.exe -Algorithm SHA256
```

> ⚠️ **주의**: 공식 GitHub 릴리스에서만 다운로드하세요. 다른 경로에서 받은 파일은 변조되었을 수 있습니다.

## 기능

### 1. 노점조회 (GNJOY)
- GNJOY API를 통한 실시간 노점 거래 검색
- 서버별 필터링 (바포메트, 이그드라실, 다크로드, 이프리트)
- 판매/구매 유형 필터링
- 카드/인챈트 상세 정보 자동 로딩

### 2. 아이템 정보 수집/조회 (kafra.kr)
- kafra.kr API를 통한 아이템 데이터베이스 검색
- 로컬 인덱스 생성으로 빠른 오프라인 검색
- 아이템 타입별 필터링
- 아이템 이미지 및 상세 정보 표시

### 3. 노점 모니터링
- 관심 아이템 등록 및 실시간 가격 추적
- 감시가격 설정 및 알림
- 자동 갱신 기능 (5~600초 간격)
- 어제/주간 평균가 대비 분석

## 요구사항

- Windows 10/11 (64비트)
- .NET 8.0 Runtime 내장 (별도 설치 불필요)

## 빌드

```bash
cd winforms/RoMarketCrawler
dotnet build
dotnet run
```

## 프로젝트 구조

```
ro-market-crawler/
└── winforms/
    └── RoMarketCrawler/
        ├── Form1.cs              # 메인 폼 (초기화, 메뉴, 설정)
        ├── Form1.Theme.cs        # 테마 및 스타일링
        ├── Form1.DealTab.cs      # 노점조회 탭
        ├── Form1.ItemTab.cs      # 아이템 정보 탭
        ├── Form1.MonitorTab.cs   # 모니터링 탭
        ├── ItemDetailForm.cs     # 아이템 상세 폼
        ├── Models/               # 데이터 모델
        │   ├── DealItem.cs
        │   ├── KafraItem.cs
        │   ├── MonitorItem.cs
        │   └── Server.cs
        └── Services/             # API 클라이언트 및 서비스
            ├── GnjoyClient.cs
            ├── KafraClient.cs
            ├── ItemIndexService.cs
            └── MonitoringService.cs
```

## 서버 ID

| ID | 서버명 |
|----|--------|
| -1 | 전체 |
| 1 | 바포메트 |
| 2 | 이그드라실 |
| 3 | 다크로드 |
| 4 | 이프리트 |

## 라이선스

MIT License
