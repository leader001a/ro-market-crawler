# Claude Code 프로젝트 규칙

## 버전 관리 규칙

### 버전 형식

`Major.Minor.Patch` (예: 1.1.2)

- **Major**: 전체 구조 변경, 호환되지 않는 대규모 변경
- **Minor**: 새로운 기능 추가, 기존 기능의 큰 변경
- **Patch**: 버그 수정, 문구 수정, 소규모 개선

### 버전 변경 시 수정 대상

`winforms/RoMarketCrawler/RoMarketCrawler.csproj`의 3개 항목을 함께 변경:

```xml
<Version>X.Y.Z</Version>
<AssemblyVersion>X.Y.Z.0</AssemblyVersion>
<FileVersion>X.Y.Z.0</FileVersion>
```

### 릴리스 절차

1. 버전 번호 변경 (csproj)
2. 빌드: `dotnet publish ... -c Release -o ./publish`
3. SHA256 계산: `sha256sum ./publish/RoMarketCrawler.exe`
4. 커밋 및 푸시
5. `gh release create v{버전}` 으로 릴리스 생성 (태그 형식: `v1.1.2`)

### 주의사항

- 바이너리가 변경되면 반드시 버전을 올려야 함 (자동 업데이트가 버전 비교 방식이므로, 같은 버전으로 재배포하면 기존 사용자에게 업데이트가 전달되지 않음)
- 코드 변경 없이 문서(md, txt)만 변경한 경우에는 버전을 올리지 않아도 됨

## 릴리스 노트 작성 규칙

수동으로 `gh release create` 또는 `gh release edit`로 릴리스를 작성할 때 아래 규칙을 따릅니다.

### 필수 포함 항목

1. **사용 가능 기간**: `StartupValidator.ExpirationDateKST` 값에서 날짜 부분을 읽어 표시
2. **다운로드 안내**
3. **주요 변경사항**: 사용자가 체감할 수 있는 변경만 포함
4. **파일 검증 (SHA256)**: `sha256sum` 또는 PowerShell `Get-FileHash`로 계산

### 변경사항 작성 원칙

- **비개발자 사용자** 대상으로 작성 (기술 용어 최소화)
- 시스템/내부 변경 사항은 **제외** (예: CTS 해제, 데드코드 제거, 캐시 내부 동작 변경, 리팩토링 등)
- 사용자가 직접 느끼는 변경만 포함 (새 기능, UI 변경, 체감되는 버그 수정)
- 이번 릴리스에서 새로 추가된 기능의 개발 중 수정사항은 **버그 수정에 포함하지 않음** (사용자가 경험한 적 없는 버그는 버그가 아님)
- 카테고리: `신규 기능`, `개선`, `버그 수정` 중 해당하는 것만 사용

### 릴리스 노트 템플릿

```markdown
## RO Market Crawler v{버전}

### 사용 가능 기간
**~{만료일}** 까지 사용 가능합니다.

### 다운로드
아래 `RoMarketCrawler.exe` 파일을 다운로드하세요.

---

## 주요 변경사항

### 신규 기능
- {사용자가 이해할 수 있는 설명}

### 개선
- {사용자가 이해할 수 있는 설명}

### 버그 수정
- {사용자가 이해할 수 있는 설명}

---

### 파일 검증 (SHA256)
```
{SHA256 해시값 대문자}
```

PowerShell에서 확인:
```powershell
Get-FileHash RoMarketCrawler.exe -Algorithm SHA256
```
```

## MCP 사용 규칙

### sequential-thinking
**분석·판단이 필요한 모든 상황에서 반드시 사용** (단순 액션 실행·코드 수정 제외):
- 코드 분석, 퍼포먼스 검토, 아키텍처 리뷰 등 모든 분석
- 버그 원인을 단계별로 추적할 때
- 복잡한 아키텍처 설계 결정이 필요할 때
- 멀티 스텝 분석이 필요한 문제 (시세 트렌드 분석, 크롤링 파이프라인 디버깅)
- "왜 안 되지?" 류의 근본 원인 분석
- 구현 계획 수립 전 타당성 검토
- 조회·검색 결과를 해석하고 다음 행동을 결정할 때
- 수정 완료 후 결과가 올바른지 검토할 때

**실행 절차 (반드시 준수)**:
1. 분석 요청을 받으면 텍스트 응답을 작성하기 전에 먼저 `ToolSearch`로 sequential-thinking 도구를 fetch한다 (deferred tool이므로 매 세션 fetch 필요)
2. fetch 완료 후 즉시 `mcp__sequential-thinking__sequentialthinking` 도구를 실행한다
3. sequential-thinking 완료 후 그 결과를 바탕으로 최종 응답을 작성한다
4. 분석을 직접 텍스트로만 출력하는 것은 규칙 위반이다

### puppeteer
다음 상황에서 **반드시** 사용:
- 크롤링 타겟 사이트 구조 확인
- 웹 페이지 셀렉터 디버깅
- JavaScript 렌더링이 필요한 페이지 분석

### playwright
다음 상황에서 **반드시** 사용:
- WinForms UI 자동화 테스트 작성
- puppeteer로 안 되는 복잡한 브라우저 상호작용

### time
다음 상황에서 **반드시** 사용:
- 시세 데이터의 시간 기반 분석
- 타임스탬프 계산, 만료일 처리 (StartupValidator)

### context7
다음 상황에서 **반드시** 사용:
- WinForms, .NET 라이브러리 공식 문서가 필요할 때
- API 사용법이 불확실할 때
- 패키지 버전별 차이를 확인할 때
