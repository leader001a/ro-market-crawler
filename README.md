# RO Market Crawler

GNJOY/kafra.kr 라그나로크 오리진 아이템 시세 조회 도구 (C# WinForms)

## 다운로드

📦 **[최신 버전 다운로드](../../releases/latest)**

- Windows 10/11 64비트 전용
- .NET 런타임 내장 (별도 설치 불필요)
- 단일 실행 파일 (약 71MB)

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
- 최근 검색 기록 (최대 10개)
- 한글 IME 지원 자동완성

### 2. 아이템 정보 수집/조회 (kafra.kr)
- kafra.kr API를 통한 아이템 데이터베이스 검색
- 로컬 인덱스 생성으로 빠른 오프라인 검색
- 아이템 타입별 필터링 (무기, 방어구, 카드, 소비품 등)
- 서브카테고리 및 직업별 상세 필터링
- 아이템 이미지 및 상세 정보 표시
- 인챈트 효과 데이터베이스 내장

### 3. 노점 모니터링
- 관심 아이템 등록 및 실시간 가격 추적
- 감시가격 설정 및 알림
- 자동 갱신 기능 (1~60분 간격)
- 알람 소리 선택 (시스템, 차임벨, 딩동, 상승음, 알림음)
- 음소거 기능
- 어제/주간 평균가 대비 분석

### 4. UI/UX
- 다크/클래식 테마 지원
- 모던 UI (둥근 버튼, 그라데이션 툴바)
- 글꼴 크기 조절 (소/중/대)
- 시작화면 스플래시
- 도움말 가이드

## 요구사항

- Windows 10/11 (64비트)
- .NET 8.0 Runtime 내장 (별도 설치 불필요)

## 프로젝트 구조

```
ro-market-crawler/
└── winforms/
    └── RoMarketCrawler/
        ├── Program.cs                # 엔트리 포인트
        ├── Form1.cs                  # 메인 폼 (초기화, 메뉴, 설정)
        ├── Form1.Theme.cs            # 테마 및 스타일링
        ├── Form1.DealTab.cs          # 노점조회 탭
        ├── Form1.ItemTab.cs          # 아이템 정보 탭
        ├── Form1.MonitorTab.cs       # 모니터링 탭
        ├── ItemDetailForm.cs         # 아이템 상세 폼
        ├── ItemInfoForm.cs           # 아이템 정보 폼
        ├── HelpGuideForm.cs          # 도움말 폼
        ├── IndexProgressDialog.cs    # 인덱스 진행 다이얼로그
        ├── StartupSplashForm.cs      # 시작 스플래시
        │
        ├── Controls/                 # 커스텀 컨트롤
        │   ├── AutoCompleteDropdown.cs    # 한글 IME 자동완성
        │   ├── BorderlessTabControl.cs    # 테두리 없는 탭
        │   ├── RoundedButton.cs           # 둥근 버튼
        │   ├── RoundedTextBox.cs          # 둥근 텍스트박스
        │   ├── RoundedComboBox.cs         # 둥근 콤보박스
        │   ├── RoundedPanel.cs            # 둥근 패널
        │   └── ModernToolStripRenderer.cs # 모던 툴스트립 렌더러
        │
        ├── Models/                   # 데이터 모델
        │   ├── DealItem.cs           # 노점 거래 아이템
        │   ├── Server.cs             # 서버 정보
        │   ├── MonitorConfig.cs      # 모니터링 설정
        │   ├── ItemFilters.cs        # 아이템 필터
        │   ├── ParsedItemDetails.cs  # 파싱된 아이템 상세
        │   ├── PriceHistory.cs       # 가격 이력
        │   ├── AppSettings.cs        # 앱 설정
        │   ├── ThemeType.cs          # 테마 타입
        │   └── AlarmSoundType.cs     # 알람 소리 타입
        │
        ├── Services/                 # API 클라이언트 및 서비스
        │   ├── GnjoyClient.cs        # GNJOY API 클라이언트
        │   ├── KafraClient.cs        # kafra.kr API 클라이언트
        │   ├── ItemIndexService.cs   # 아이템 인덱스 서비스
        │   ├── MonitoringService.cs  # 모니터링 서비스
        │   ├── AlarmSoundService.cs  # 알람 소리 서비스
        │   ├── EnchantDatabase.cs    # 인챈트 DB 서비스
        │   ├── ItemDealParser.cs     # 거래 아이템 파서
        │   ├── ItemDetailParser.cs   # 아이템 상세 파서
        │   ├── ItemTextParser.cs     # 아이템 텍스트 파서
        │   ├── PriceQuoteParser.cs   # 가격 시세 파서
        │   └── StartupValidator.cs   # 시작 검증 서비스
        │
        ├── Exceptions/               # 예외 클래스
        │   └── RateLimitException.cs # API 제한 예외
        │
        └── Data/                     # 데이터 파일
            ├── EnchantEffects.json   # 인챈트 효과 데이터
            └── logo.png              # 로고 이미지
```

## 서버 ID

| ID | 서버명 |
|----|--------|
| -1 | 전체 |
| 1 | 바포메트 |
| 2 | 이그드라실 |
| 3 | 다크로드 |
| 4 | 이프리트 |

## 스크린샷

### 다크 테마
노점조회, 아이템 정보, 모니터링 탭을 다크 테마로 사용할 수 있습니다.

### 클래식 테마
Windows 기본 스타일의 클래식 테마도 지원합니다.

## 라이선스

MIT License
