namespace RoMarketCrawler;

/// <summary>
/// Help guide form showing comprehensive usage instructions
/// </summary>
public class HelpGuideForm : Form
{
    private TabControl _tabControl = null!;
    private readonly Color _bgColor;
    private readonly Color _textColor;
    private readonly Color _accentColor;
    private readonly Color _panelColor;

    public enum HelpSection
    {
        Overview,
        DealSearch,
        ItemSearch,
        Monitoring
    }

    public HelpGuideForm(ThemeType theme, HelpSection initialSection = HelpSection.Overview)
    {
        // Set theme colors
        if (theme == ThemeType.Dark)
        {
            _bgColor = Color.FromArgb(30, 30, 30);
            _textColor = Color.FromArgb(220, 220, 220);
            _accentColor = Color.FromArgb(0, 150, 200);
            _panelColor = Color.FromArgb(45, 45, 45);
        }
        else
        {
            _bgColor = Color.FromArgb(250, 250, 250);
            _textColor = Color.FromArgb(30, 30, 30);
            _accentColor = Color.FromArgb(0, 120, 180);
            _panelColor = Color.FromArgb(240, 240, 240);
        }

        InitializeComponents();
        SelectSection(initialSection);
    }

    private void InitializeComponents()
    {
        Text = "RO Market Crawler - 사용 가이드";
        Size = new Size(750, 600);
        MinimumSize = new Size(600, 500);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = _bgColor;
        ForeColor = _textColor;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        // Main layout
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(10),
            BackColor = _bgColor
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        // Tab control
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Malgun Gothic", 10f),
            Padding = new Point(15, 8)
        };

        // Add tabs
        _tabControl.TabPages.Add(CreateOverviewTab());
        _tabControl.TabPages.Add(CreateDealSearchTab());
        _tabControl.TabPages.Add(CreateItemSearchTab());
        _tabControl.TabPages.Add(CreateMonitoringTab());
        _tabControl.TabPages.Add(CreateTipsTab());

        mainPanel.Controls.Add(_tabControl, 0, 0);

        // Close button panel
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _bgColor
        };

        var btnClose = new Button
        {
            Text = "닫기",
            Size = new Size(100, 35),
            FlatStyle = FlatStyle.Flat,
            BackColor = _panelColor,
            ForeColor = _textColor,
            Font = new Font("Malgun Gothic", 10f),
            Cursor = Cursors.Hand
        };
        btnClose.FlatAppearance.BorderColor = _accentColor;
        btnClose.Click += (s, e) => Close();
        btnClose.Anchor = AnchorStyles.Right;
        btnClose.Location = new Point(buttonPanel.Width - btnClose.Width - 10, 7);

        buttonPanel.Controls.Add(btnClose);
        buttonPanel.Resize += (s, e) => btnClose.Location = new Point(buttonPanel.Width - btnClose.Width - 10, 7);

        mainPanel.Controls.Add(buttonPanel, 0, 1);

        Controls.Add(mainPanel);
    }

    private TabPage CreateOverviewTab()
    {
        var tab = new TabPage("개요") { BackColor = _bgColor };
        tab.Controls.Add(CreateRichTextBox(@"
[RO Market Crawler 소개]

라그나로크 온라인 거래 정보를 쉽게 검색하고 모니터링할 수 있는 프로그램입니다.
kafra.kr API를 통해 실시간 노점 정보를 조회합니다.


[주요 기능]

1. 노점조회
   - 아이템 이름으로 현재 판매/구매 중인 노점 검색
   - 서버별, 거래유형별 필터링
   - 상세 정보 조회 (더블클릭)

2. 아이템 정보 수집/조회
   - 22,000+ 아이템 데이터베이스 검색
   - 타입별 필터링 (무기, 방어구, 카드 등)
   - 세부 필터 지원 (무기 종류, 장착 위치 등)

3. 노점 모니터링
   - 관심 아이템 실시간 가격 모니터링
   - 목표가 설정 및 알림
   - 자동 갱신 기능


[단축키]

Ctrl + (+)  글꼴 크게
Ctrl + (-)  글꼴 작게
Ctrl + 0    글꼴 기본 크기
Ctrl + R    아이템 정보 수집
Ctrl + Shift + W  전체 팝업 닫기
ESC         현재 창 닫기
"));
        return tab;
    }

    private TabPage CreateDealSearchTab()
    {
        var tab = new TabPage("노점조회") { BackColor = _bgColor };
        tab.Controls.Add(CreateRichTextBox(@"
[노점조회 사용법]

실시간으로 노점에서 판매/구매 중인 아이템을 검색합니다.


[검색 방법]

1. 서버 선택
   - 전체: 모든 서버 검색
   - 개별 서버 선택 가능

2. 아이템 이름 입력
   - 아이템명 일부만 입력해도 검색 가능
   - 자동완성 지원 (아이템 인덱스 로드 후)

3. 고급 검색
   - 제련: ""11 딤"" 형식으로 제련 수치 지정
   - 등급: ""%UNIQUE% 딤"" 형식으로 등급 지정
   - 조합: ""11%UNIQUE% 딤"" 형식 가능


[검색 결과]

- 서버: 아이템이 있는 서버
- 유형: 판매/구매
- 아이템: 아이템명 (제련, 등급 포함)
- 카드/인챈트: 장착된 카드와 인챈트
- 수량, 가격, 상점명


[상세 조회]

- 행 더블클릭: 상세 정보 팝업
- 노점 내 다른 아이템 확인 가능
- 가격 히스토리 조회


[최근 검색]

- 검색어 자동 저장 (최대 10개)
- 클릭으로 빠른 재검색
"));
        return tab;
    }

    private TabPage CreateItemSearchTab()
    {
        var tab = new TabPage("아이템 정보") { BackColor = _bgColor };
        tab.Controls.Add(CreateRichTextBox(@"
[아이템 정보 수집/조회 사용법]

22,000개 이상의 아이템 데이터를 검색합니다.


[데이터 수집]

첫 실행 시 또는 갱신 필요 시:
- 메뉴 > 도구 > 아이템정보 수집 (Ctrl+R)
- 약 2-3분 소요
- 진행률 표시됨


[검색 방법]

1. 타입 선택
   - 전체, 무기, 방어구, 카드, 쉐도우, 의상 등

2. 세부 필터 (타입별로 다름)
   - 무기: 한손검, 양손검, 단검, 활 등
   - 방어구: 투구, 갑옷, 방패, 신발 등
   - 카드: 장착 위치별
   - 의상: 상단, 중단, 하단 등

3. 아이템명 검색
   - 이름 일부 입력 후 검색
   - ""설명 포함 검색"" 체크 시 아이템 설명도 검색


[검색 결과]

- ID: 아이템 고유 번호
- 아이템명: 이름
- 슬롯: 카드 슬롯 수
- 무게, NPC구매가
- 장착 가능: 장착 가능 직업


[상세 정보]

- 행 클릭: 우측에 아이템 이미지와 설명 표시
- 행 더블클릭: 해당 아이템 노점 검색
"));
        return tab;
    }

    private TabPage CreateMonitoringTab()
    {
        var tab = new TabPage("노점 모니터링") { BackColor = _bgColor };
        tab.Controls.Add(CreateRichTextBox(@"
[노점 모니터링 사용법]

관심 아이템의 가격을 실시간으로 모니터링합니다.


[모니터링 항목 추가]

1. 노점조회 결과에서 추가
   - 우클릭 > ""모니터링 추가""
   - 자동으로 아이템명과 서버 설정

2. 직접 추가
   - 서버 선택
   - 아이템명 직접 입력
   - 목표가 설정 (선택사항)


[모니터링 목록 (좌측)]

- 서버: 모니터링 서버
- 아이템: 모니터링 중인 아이템
- 목표가: 설정한 목표 가격 (더블클릭으로 수정)
- 상태: 자동갱신 시 남은 시간 표시


[조회 결과 (우측)]

- 제련/등급: 아이템 상세
- 아이템명, 서버
- 매물수: 현재 판매 중인 수량
- 최저가, 어제평균, 주간평균
- 차이: 최저가와 평균 비교
- 상태: 득템!/저렴!/양호/정상


[자동 갱신]

1. 자동조회 버튼 클릭
2. 갱신 주기 설정 (1-60분)
3. 자동갱신 버튼 클릭
4. 설정한 주기마다 자동 갱신


[알림]

- 목표가 이하 매물 발견 시 알림음 재생
- 소리 끄기/켜기 버튼으로 제어
"));
        return tab;
    }

    private TabPage CreateTipsTab()
    {
        var tab = new TabPage("유용한 팁") { BackColor = _bgColor };
        tab.Controls.Add(CreateRichTextBox(@"
[유용한 팁]


[효율적인 검색]

- 아이템명은 일부만 입력해도 됩니다
  예: ""글레이시아"" 대신 ""글레""

- 자동완성을 활용하세요
  입력 후 잠시 기다리면 추천 목록 표시

- 제련과 등급을 함께 검색하세요
  예: ""11%UNIQUE% 딤""


[모니터링 활용]

- 목표가를 설정하면 저렴한 매물 알림
- 여러 서버를 동시에 모니터링 가능
- 자동갱신으로 실시간 시세 파악


[데이터 관리]

- 아이템 인덱스는 1주일에 1번 정도 갱신
- 설정은 자동 저장됩니다
- 모니터링 목록도 자동 저장


[성능 최적화]

- 자동갱신 주기는 1분 이상 (최소값)
- 모니터링 항목이 많으면 갱신 시간 증가


[문의 및 피드백]

- GitHub: PM-KiWoong/ro-market-crawler
- 버그 리포트 및 기능 제안 환영합니다
"));
        return tab;
    }

    private RichTextBox CreateRichTextBox(string text)
    {
        var rtb = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = _bgColor,
            ForeColor = _textColor,
            Font = new Font("Malgun Gothic", 10f),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Text = text.TrimStart('\r', '\n')
        };

        // Apply formatting
        FormatRichTextBox(rtb);

        return rtb;
    }

    private void FormatRichTextBox(RichTextBox rtb)
    {
        var text = rtb.Text;
        var lines = text.Split('\n');
        int position = 0;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            // Section headers [...]
            if (trimmed.StartsWith('[') && trimmed.Contains(']'))
            {
                var start = text.IndexOf('[', position);
                var end = text.IndexOf(']', start);
                if (start >= 0 && end > start)
                {
                    rtb.Select(start, end - start + 1);
                    rtb.SelectionFont = new Font("Malgun Gothic", 12f, FontStyle.Bold);
                    rtb.SelectionColor = _accentColor;
                }
            }
            // Numbered items (1., 2., etc.)
            else if (trimmed.Length > 0 && char.IsDigit(trimmed[0]) && trimmed.Contains('.'))
            {
                var idx = text.IndexOf(trimmed, position);
                if (idx >= 0)
                {
                    var dotIdx = trimmed.IndexOf('.');
                    rtb.Select(idx, dotIdx + 1);
                    rtb.SelectionFont = new Font("Malgun Gothic", 10f, FontStyle.Bold);
                    rtb.SelectionColor = _accentColor;
                }
            }
            // Bullet points with dash
            else if (trimmed.StartsWith('-'))
            {
                var idx = text.IndexOf(trimmed, position);
                if (idx >= 0)
                {
                    rtb.Select(idx, 1);
                    rtb.SelectionColor = _accentColor;
                }
            }

            position += line.Length + 1;
        }

        // Reset selection
        rtb.Select(0, 0);
        rtb.ScrollToCaret();
    }

    public void SelectSection(HelpSection section)
    {
        _tabControl.SelectedIndex = section switch
        {
            HelpSection.Overview => 0,
            HelpSection.DealSearch => 1,
            HelpSection.ItemSearch => 2,
            HelpSection.Monitoring => 3,
            _ => 0
        };
    }

    /// <summary>
    /// Show quick help popup for a specific tab
    /// </summary>
    public static void ShowQuickHelp(IWin32Window owner, ThemeType theme, HelpSection section)
    {
        var content = section switch
        {
            HelpSection.DealSearch => GetDealSearchQuickHelp(),
            HelpSection.ItemSearch => GetItemSearchQuickHelp(),
            HelpSection.Monitoring => GetMonitoringQuickHelp(),
            _ => "도움말 정보가 없습니다."
        };

        var title = section switch
        {
            HelpSection.DealSearch => "노점조회 - 빠른 사용법",
            HelpSection.ItemSearch => "아이템 정보 - 빠른 사용법",
            HelpSection.Monitoring => "노점 모니터링 - 빠른 사용법",
            _ => "빠른 사용법"
        };

        ShowQuickHelpDialog(owner, theme, title, content);
    }

    private static string GetDealSearchQuickHelp()
    {
        return @"[기본 검색]
1. 서버 선택 (전체 또는 개별)
2. 아이템명 입력
3. 검색 버튼 클릭 또는 Enter

[고급 검색]
- 제련 지정: ""11 딤 글레이시아""
- 등급 지정: ""%UNIQUE% 딤""
- 조합: ""11%UNIQUE% 딤""

[결과 활용]
- 더블클릭: 상세 정보 보기
- 우클릭: 모니터링 추가

[단축키]
- Enter: 검색 실행
- ESC: 자동완성 닫기";
    }

    private static string GetItemSearchQuickHelp()
    {
        return @"[검색 방법]
1. 타입 선택 (무기/방어구/카드 등)
2. 세부 필터 선택 (선택사항)
3. 아이템명 입력 후 검색

[세부 필터 예시]
- 무기: 한손검, 양손검, 활 등
- 방어구: 투구상단, 갑옷, 방패 등
- 카드: 무기카드, 갑옷카드 등

[결과 활용]
- 클릭: 아이템 상세정보 표시
- 더블클릭: 노점에서 검색

[데이터 수집]
- 메뉴 > 도구 > 아이템정보 수집
- 또는 Ctrl + R";
    }

    private static string GetMonitoringQuickHelp()
    {
        return @"[모니터링 추가]
- 노점조회에서 우클릭 > 모니터링 추가
- 또는 직접 서버/아이템명 입력

[목표가 설정]
- 목표가 셀 더블클릭하여 수정
- 목표가 이하 매물 발견 시 알림

[자동 갱신]
1. 자동조회 버튼 클릭
2. 주기 설정 (초 단위)
3. 적용 클릭

[상태 표시]
- 득템!: 목표가 이하 매물 존재
- 저렴!: 평균보다 20% 이상 저렴
- 양호: 평균보다 10% 이상 저렴
- 정상: 평균 수준

[기타]
- 수동조회: 즉시 모든 항목 갱신
- 소리: 알림음 켜기/끄기";
    }

    private static void ShowQuickHelpDialog(IWin32Window owner, ThemeType theme, string title, string content)
    {
        Color bgColor, textColor, accentColor, panelColor;

        if (theme == ThemeType.Dark)
        {
            bgColor = Color.FromArgb(30, 30, 30);
            textColor = Color.FromArgb(220, 220, 220);
            accentColor = Color.FromArgb(0, 150, 200);
            panelColor = Color.FromArgb(45, 45, 45);
        }
        else
        {
            bgColor = Color.FromArgb(250, 250, 250);
            textColor = Color.FromArgb(30, 30, 30);
            accentColor = Color.FromArgb(0, 120, 180);
            panelColor = Color.FromArgb(240, 240, 240);
        }

        using var form = new Form
        {
            Text = title,
            Size = new Size(450, 420),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = bgColor,
            ForeColor = textColor,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            KeyPreview = true
        };

        form.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) form.Close(); };

        var rtb = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = bgColor,
            ForeColor = textColor,
            Font = new Font("Malgun Gothic", 10f),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            Text = content,
            Padding = new Padding(10)
        };

        // Format section headers
        var text = rtb.Text;
        int position = 0;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('[') && trimmed.Contains(']'))
            {
                var start = text.IndexOf('[', position);
                var end = text.IndexOf(']', start);
                if (start >= 0 && end > start)
                {
                    rtb.Select(start, end - start + 1);
                    rtb.SelectionFont = new Font("Malgun Gothic", 10f, FontStyle.Bold);
                    rtb.SelectionColor = accentColor;
                }
            }
            else if (trimmed.StartsWith('-'))
            {
                var idx = text.IndexOf(trimmed, position);
                if (idx >= 0)
                {
                    rtb.Select(idx, 1);
                    rtb.SelectionColor = accentColor;
                }
            }
            position += line.Length + 1;
        }
        rtb.Select(0, 0);

        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = bgColor
        };

        var btnClose = new Button
        {
            Text = "닫기",
            Size = new Size(80, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = panelColor,
            ForeColor = textColor,
            Cursor = Cursors.Hand
        };
        btnClose.FlatAppearance.BorderColor = accentColor;
        btnClose.Click += (s, e) => form.Close();
        btnClose.Location = new Point((panel.Width - btnClose.Width) / 2, 10);
        panel.Resize += (s, e) => btnClose.Location = new Point((panel.Width - btnClose.Width) / 2, 10);

        panel.Controls.Add(btnClose);
        form.Controls.Add(rtb);
        form.Controls.Add(panel);

        form.ShowDialog(owner);
    }
}
