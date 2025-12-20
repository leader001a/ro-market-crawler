namespace RoMarketCrawler;

/// <summary>
/// Help guide form showing comprehensive usage instructions
/// </summary>
using System.Runtime.InteropServices;

using RoMarketCrawler.Controls;
using RoMarketCrawler.Models;

public class HelpGuideForm : Form
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int PreferredAppMode_AllowDark = 1;

    /// <summary>
    /// Apply dark or light scrollbar theme to a control and all its children recursively (static version)
    /// </summary>
    private static void ApplyScrollBarThemeStatic(Control control, bool isDark)
    {
        string themeName = isDark ? "DarkMode_Explorer" : "Explorer";

        if (control.IsHandleCreated)
        {
            SetWindowTheme(control.Handle, themeName, null);
        }
        else
        {
            string capturedTheme = themeName;
            control.HandleCreated += (s, e) =>
            {
                if (s is Control c)
                    SetWindowTheme(c.Handle, capturedTheme, null);
            };
        }

        foreach (Control child in control.Controls)
        {
            ApplyScrollBarThemeStatic(child, isDark);
        }

        if (control.IsHandleCreated)
        {
            control.Invalidate(true);
        }
    }

    /// <summary>
    /// Apply dark or light scrollbar theme to a control and all its children recursively
    /// </summary>
    private void ApplyScrollBarTheme(Control control, bool isDark)
    {
        string themeName = isDark ? "DarkMode_Explorer" : "Explorer";

        // For the Form itself, enable dark mode at the window level
        if (control is Form form && isDark)
        {
            SetPreferredAppMode(PreferredAppMode_AllowDark);
            if (form.IsHandleCreated)
            {
                AllowDarkModeForWindow(form.Handle, true);
                int darkMode = 1;
                DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }
        }

        // Apply to the control itself
        if (control.IsHandleCreated)
        {
            SetWindowTheme(control.Handle, themeName, null);
        }
        else
        {
            string capturedTheme = themeName;
            control.HandleCreated += (s, e) =>
            {
                if (s is Control c)
                    SetWindowTheme(c.Handle, capturedTheme, null);
            };
        }

        // Recursively apply to all existing child controls
        foreach (Control child in control.Controls)
        {
            ApplyScrollBarTheme(child, isDark);
        }

        // Invalidate the control to force redraw
        if (control.IsHandleCreated)
        {
            control.Invalidate(true);
        }
    }

    private BorderlessTabControl _tabControl = null!;
    private readonly Color _bgColor;
    private readonly Color _textColor;
    private readonly Color _accentColor;
    private readonly Color _panelColor;
    private readonly float _fontSize;
    private readonly bool _isDarkTheme;

    public enum HelpSection
    {
        Overview,
        DealSearch,
        ItemSearch,
        Monitoring
    }

    public HelpGuideForm(ThemeType theme, float fontSize = 10f, HelpSection initialSection = HelpSection.Overview)
    {
        _fontSize = fontSize;
        _isDarkTheme = theme == ThemeType.Dark;

        // Set theme colors
        if (_isDarkTheme)
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

        // Apply dark theme scrollbar to all controls
        if (_isDarkTheme)
        {
            ApplyScrollBarTheme(this, true);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_isDarkTheme)
        {
            // Enable dark mode for the window
            SetPreferredAppMode(PreferredAppMode_AllowDark);
            AllowDarkModeForWindow(this.Handle, true);
            int darkMode = 1;
            DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            // Apply dark scrollbar theme to all controls
            ApplyDarkScrollbarsToAllControls(this);
        }
    }

    private void ApplyDarkScrollbarsToAllControls(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control.IsHandleCreated)
            {
                SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            }
            ApplyDarkScrollbarsToAllControls(control);
        }
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

        // Tab control - use BorderlessTabControl like main form
        _tabControl = new BorderlessTabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Malgun Gothic", _fontSize - 2),
            Padding = new Point(12, 5),  // Same as main form
            ItemSize = new Size(120, 30) // Adjusted for 5 tabs (main form uses 180 for 3 tabs)
        };

        // Apply owner-draw styling (same as main form's ApplyTabControlStyle)
        _tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
        _tabControl.DrawItem += TabControl_DrawItem;
        _tabControl.Paint += TabControl_Paint;

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
            Font = new Font("Malgun Gothic", _fontSize - 3, FontStyle.Bold),
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

F1                사용 가이드 열기
Ctrl + (+)        글꼴 크게
Ctrl + (-)        글꼴 작게
Ctrl + 0          글꼴 기본 크기
Ctrl + R          아이템 정보 수집
Ctrl + Shift + W  전체 팝업 닫기
ESC               현재 창 닫기


[개인정보 보호]

본 프로그램은 사용자의 개인정보, 게임 계정 정보,
게임 내 활동 정보 등 어떠한 정보도 수집하거나
외부로 전송하지 않습니다.
모든 데이터는 사용자의 로컬 PC에만 저장됩니다.
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

3. 고급 검색 (등급+제련+아이템명 순서)
   - 등급: ""%UNIQUE%딤"" 형식으로 등급 지정
   - 제련: ""11딤"" 형식으로 제련 수치 지정
   - 조합: ""%UNIQUE%11딤"" 형식 가능


[검색 결과]

- 서버: 아이템이 있는 서버
- 유형: 판매/구매
- 아이템: 아이템명 (등급, 제련 포함)
- 카드/인챈트/랜덤옵션: 장착된 카드와 인챈트, 랜덤옵션
- 수량, 가격, 상점명


[상세 조회]

- 행 더블클릭: 상세 정보 팝업
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

- 등급/제련: 아이템 상세
- 아이템명, 서버
- 매물수: 현재 판매 중인 수량
- 최저가, 어제평균, 주간평균
- 차이: 최저가와 평균 비교
- 상태: 득템!/저렴!/양호/정상


[버튼 기능]

- 수동조회: 모니터링 항목 즉시 갱신
- 자동갱신: 설정한 주기마다 자동 갱신 시작/중지
- 알람설정: 득템 발견 시 알람 주기 및 소리 설정
- 음소거: 알람 소리 끄기/켜기


[자동 갱신 사용법]

1. 자동갱신 버튼 클릭
2. 갱신 주기 설정 (1-60분)
3. 적용 버튼 클릭
4. 설정한 주기마다 자동 갱신
5. 노점조회 탭 이동 시 자동 갱신 중지


[알림]

- 목표가 이하 매물 발견 시 알림음 재생
- 알람설정에서 알람 주기 및 소리 종류 선택 가능
- 음소거 버튼으로 소리 끄기/켜기


[주의사항]

!! 아이템명을 최대한 자세하게 입력하세요 !!

- 검색 결과는 최대 30개의 노점만 취합됩니다
- 예: ""빙화 마석""처럼 일반적인 이름 사용 시
  → 매물이 너무 많아 일부만 조회될 수 있습니다
- 예: ""빙화 마석 V"" 또는 ""빙화 마석 VI""처럼
  → 구체적으로 입력하면 정확한 결과를 얻습니다

- 노점조회에서 우클릭 > 모니터링 추가를 권장합니다
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

- 등급과 제련을 함께 검색하세요
  예: ""%UNIQUE%11딤""


[모니터링 활용]

- 목표가를 설정하면 저렴한 매물 알림
- 여러 서버를 동시에 모니터링 가능
- 자동갱신으로 실시간 시세 파악


[데이터 관리]

- 아이템 인덱스는 1주일에 1번 정도 갱신
- 설정은 자동 저장됩니다
- 모니터링 목록도 자동 저장


[성능 최적화]

- 자동갱신 주기는 1분 이상 권장
- 모니터링 항목이 많으면 갱신 시간 증가
- API 요청 제한으로 동시 요청 수 제한됨


[단축키 모음]

- F1: 사용 가이드
- Ctrl + R: 아이템 정보 수집
- Ctrl + (+/-): 글꼴 크기 조절
- Ctrl + 0: 글꼴 기본 크기
- Ctrl + Shift + W: 전체 팝업 닫기
- ESC: 현재 창 닫기


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
            Font = new Font("Malgun Gothic", _fontSize - 2),
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
        // Save content and clear - AppendText approach is more reliable than Select()
        var content = rtb.Text;
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        rtb.Clear();

        // Font sizes consistent with Form1 (base - 2 for text, base for headers)
        using var normalFont = new Font("Malgun Gothic", _fontSize - 2);
        using var boldFont = new Font("Malgun Gothic", _fontSize - 2, FontStyle.Bold);
        using var headerFont = new Font("Malgun Gothic", _fontSize, FontStyle.Bold);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            FormatAndAppendLine(rtb, line, normalFont, boldFont, headerFont);

            // Add newline except for last line
            if (i < lines.Length - 1)
            {
                rtb.SelectionColor = _textColor;
                rtb.SelectionFont = normalFont;
                rtb.AppendText(Environment.NewLine);
            }
        }

        rtb.SelectionStart = 0;
        rtb.ScrollToCaret();
    }

    private void FormatAndAppendLine(RichTextBox rtb, string line, Font normalFont, Font boldFont, Font headerFont)
    {
        if (string.IsNullOrEmpty(line))
            return;

        var trimmed = line.TrimStart();
        var leadingSpaces = line.Substring(0, line.Length - trimmed.Length);

        // Append leading spaces with normal formatting
        if (leadingSpaces.Length > 0)
        {
            rtb.SelectionColor = _textColor;
            rtb.SelectionFont = normalFont;
            rtb.AppendText(leadingSpaces);
        }

        // Section headers [...] - larger and bold with accent color
        if (trimmed.StartsWith('[') && trimmed.Contains(']'))
        {
            int bracketEnd = trimmed.IndexOf(']');
            var headerPart = trimmed.Substring(0, bracketEnd + 1);
            var restPart = trimmed.Substring(bracketEnd + 1);

            rtb.SelectionColor = _accentColor;
            rtb.SelectionFont = headerFont;
            rtb.AppendText(headerPart);

            if (restPart.Length > 0)
            {
                rtb.SelectionColor = _textColor;
                rtb.SelectionFont = normalFont;
                rtb.AppendText(restPart);
            }
        }
        // Numbered items (1., 2., etc.) - bold number with accent color
        else if (trimmed.Length > 1 && char.IsDigit(trimmed[0]) && trimmed[1] == '.')
        {
            var numberPart = trimmed.Substring(0, 2);
            var restPart = trimmed.Substring(2);

            rtb.SelectionColor = _accentColor;
            rtb.SelectionFont = boldFont;
            rtb.AppendText(numberPart);

            if (restPart.Length > 0)
            {
                rtb.SelectionColor = _textColor;
                rtb.SelectionFont = normalFont;
                rtb.AppendText(restPart);
            }
        }
        // Bullet points with dash - accent color for dash only
        else if (trimmed.StartsWith('-'))
        {
            rtb.SelectionColor = _accentColor;
            rtb.SelectionFont = normalFont;
            rtb.AppendText("-");

            var restPart = trimmed.Substring(1);
            if (restPart.Length > 0)
            {
                rtb.SelectionColor = _textColor;
                rtb.SelectionFont = normalFont;
                rtb.AppendText(restPart);
            }
        }
        // Normal line
        else
        {
            rtb.SelectionColor = _textColor;
            rtb.SelectionFont = normalFont;
            rtb.AppendText(trimmed);
        }
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

    // Exact copy from Form1.Theme.cs TabControl_DrawItem
    private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabControl) return;

        var tab = tabControl.TabPages[e.Index];
        var isSelected = e.Index == tabControl.SelectedIndex;
        var bounds = e.Bounds;
        var stripBgColor = _isDarkTheme ? _bgColor : SystemColors.Control;
        using var stripBgBrush = new SolidBrush(stripBgColor);

        // First, cover the ENTIRE top strip with background color
        e.Graphics.FillRectangle(stripBgBrush, 0, 0, tabControl.Width, 5);

        // Cover area to the LEFT of first tab
        if (e.Index == 0)
        {
            e.Graphics.FillRectangle(stripBgBrush, 0, 0, bounds.X + 5, tabControl.ItemSize.Height + 15);
        }

        // Draw the tab (includes extended border coverage)
        if (_isDarkTheme)
        {
            DrawDarkThemeTab(e.Graphics, tabControl, bounds, tab.Text, isSelected);
        }
        else
        {
            DrawClassicThemeTab(e.Graphics, tabControl, bounds, tab.Text, isSelected);
        }

        // After drawing tab, fill gap to the RIGHT of this tab (more aggressively)
        if (e.Index < tabControl.TabCount - 1)
        {
            var nextBounds = tabControl.GetTabRect(e.Index + 1);
            var gapStart = bounds.Right - 5;
            var gapWidth = nextBounds.X - bounds.Right + 10;
            if (gapWidth > 0)
            {
                e.Graphics.FillRectangle(stripBgBrush, gapStart, 0, gapWidth, tabControl.ItemSize.Height + 15);
            }
        }

        // Fill empty strip area after last tab
        if (e.Index == tabControl.TabCount - 1)
        {
            var emptyAreaX = bounds.Right - 2;
            var emptyAreaWidth = tabControl.Width - bounds.Right + 5;
            if (emptyAreaWidth > 0)
            {
                e.Graphics.FillRectangle(stripBgBrush, emptyAreaX, 0, emptyAreaWidth, tabControl.ItemSize.Height + 15);
            }
        }

        // Cover bottom border of tab strip (line between tabs and content)
        e.Graphics.FillRectangle(stripBgBrush, 0, tabControl.ItemSize.Height, tabControl.Width, 15);
    }

    // Exact copy from Form1.Theme.cs DrawDarkThemeTab
    private void DrawDarkThemeTab(Graphics g, TabControl tabControl, Rectangle bounds, string text, bool isSelected)
    {
        Color tabColor = isSelected ? _accentColor : _bgColor;
        Color textColor = isSelected ? Color.White : Color.FromArgb(160, 160, 170);

        // Step 1: Fill VERY LARGE extended area with background color (aggressive border removal)
        using var borderBrush = new SolidBrush(_bgColor);

        // Cover much larger area to ensure all system borders are hidden (especially left/right edges)
        var extendedArea = new Rectangle(bounds.X - 8, bounds.Y - 5, bounds.Width + 16, bounds.Height + 15);
        g.FillRectangle(borderBrush, extendedArea);

        // Step 2: Fill the actual tab content area with tab color
        using var tabBrush = new SolidBrush(tabColor);
        g.FillRectangle(tabBrush, bounds);

        // Step 3: Draw text
        using var textBrush = new SolidBrush(textColor);
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, tabControl.Font, textBrush, bounds, sf);
    }

    // Exact copy from Form1.Theme.cs DrawClassicThemeTab
    private void DrawClassicThemeTab(Graphics g, TabControl tabControl, Rectangle bounds, string text, bool isSelected)
    {
        Color tabColor = isSelected ? SystemColors.Window : SystemColors.Control;
        Color textColor = SystemColors.ControlText;

        // Step 1: Fill VERY LARGE extended area with background color (aggressive border removal)
        using var borderBrush = new SolidBrush(SystemColors.Control);
        var extendedArea = new Rectangle(bounds.X - 5, bounds.Y - 5, bounds.Width + 10, bounds.Height + 12);
        g.FillRectangle(borderBrush, extendedArea);

        // Step 2: Fill the actual tab content area with tab color
        using var tabBrush = new SolidBrush(tabColor);
        g.FillRectangle(tabBrush, bounds);

        // Step 3: Draw text
        using var textBrush = new SolidBrush(textColor);
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, tabControl.Font, textBrush, bounds, sf);
    }

    // Exact copy from Form1.Theme.cs TabControl_Paint
    private void TabControl_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not TabControl tabControl) return;

        var borderCoverColor = _isDarkTheme ? _bgColor : SystemColors.Control;
        using var coverBrush = new SolidBrush(borderCoverColor);

        var tabStripHeight = tabControl.ItemSize.Height;
        var totalHeight = tabControl.Height;
        var totalWidth = tabControl.Width;

        // Cover all edge areas - entire control height for full border removal

        // Left edge - full height (covers content area border too)
        e.Graphics.FillRectangle(coverBrush, 0, 0, 4, totalHeight);

        // Top edge (full width)
        e.Graphics.FillRectangle(coverBrush, 0, 0, totalWidth, 6);

        // Right edge - full height (covers content area border too)
        e.Graphics.FillRectangle(coverBrush, totalWidth - 4, 0, 4, totalHeight);

        // Bottom edge - full width (covers content area border)
        e.Graphics.FillRectangle(coverBrush, 0, totalHeight - 4, totalWidth, 4);

        // Bottom of tab strip (border between tabs and content)
        e.Graphics.FillRectangle(coverBrush, 0, tabStripHeight - 2, totalWidth, 18);

        // Also fill the area before first tab if there's any gap
        if (tabControl.TabCount > 0)
        {
            var firstTabRect = tabControl.GetTabRect(0);
            if (firstTabRect.X > 0)
            {
                e.Graphics.FillRectangle(coverBrush, 0, 0, firstTabRect.X + 3, tabStripHeight + 15);
            }
        }
    }

    /// <summary>
    /// Show quick help popup for a specific tab
    /// </summary>
    public static void ShowQuickHelp(IWin32Window owner, ThemeType theme, float fontSize, HelpSection section)
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

        ShowQuickHelpDialog(owner, theme, fontSize, title, content);
    }

    private static string GetDealSearchQuickHelp()
    {
        return @"[기본 검색]
1. 서버 선택 (전체 또는 개별)
2. 아이템명 입력
3. 검색 버튼 클릭 또는 Enter

[고급 검색] (등급+제련+아이템명 순서)
- 등급 지정: ""%UNIQUE%딤""
- 제련 지정: ""11딤 글레이시아""
- 조합: ""%UNIQUE%11딤""

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
1. 자동갱신 버튼 클릭
2. 주기 설정 (분 단위)
3. 적용 클릭

[상태 표시]
- 득템!: 목표가 이하 매물 존재
- 저렴!: 평균보다 20% 이상 저렴
- 양호: 평균보다 10% 이상 저렴
- 정상: 평균 수준

[버튼 기능]
- 수동조회: 즉시 모든 항목 갱신
- 알람설정: 알람 주기/소리 설정
- 음소거: 알림음 켜기/끄기";
    }

    private static void ShowQuickHelpDialog(IWin32Window owner, ThemeType theme, float fontSize, string title, string content)
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
            Font = new Font("Malgun Gothic", fontSize - 2),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            Padding = new Padding(10)
        };

        // Use AppendText approach - more reliable than Select() for positioning
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        // Font sizes consistent with Form1 (base - 2 for text, base for headers)
        using var normalFont = new Font("Malgun Gothic", fontSize - 2);
        using var boldFont = new Font("Malgun Gothic", fontSize - 2, FontStyle.Bold);
        using var headerFont = new Font("Malgun Gothic", fontSize, FontStyle.Bold);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (!string.IsNullOrEmpty(line))
            {
                var trimmed = line.TrimStart();
                var leadingSpaces = line.Substring(0, line.Length - trimmed.Length);

                // Append leading spaces
                if (leadingSpaces.Length > 0)
                {
                    rtb.SelectionColor = textColor;
                    rtb.SelectionFont = normalFont;
                    rtb.AppendText(leadingSpaces);
                }

                // Section headers [...]
                if (trimmed.StartsWith('[') && trimmed.Contains(']'))
                {
                    int bracketEnd = trimmed.IndexOf(']');
                    var headerPart = trimmed.Substring(0, bracketEnd + 1);
                    var restPart = trimmed.Substring(bracketEnd + 1);

                    rtb.SelectionColor = accentColor;
                    rtb.SelectionFont = headerFont;
                    rtb.AppendText(headerPart);

                    if (restPart.Length > 0)
                    {
                        rtb.SelectionColor = textColor;
                        rtb.SelectionFont = normalFont;
                        rtb.AppendText(restPart);
                    }
                }
                // Numbered items (1., 2., etc.)
                else if (trimmed.Length > 1 && char.IsDigit(trimmed[0]) && trimmed[1] == '.')
                {
                    var numberPart = trimmed.Substring(0, 2);
                    var restPart = trimmed.Substring(2);

                    rtb.SelectionColor = accentColor;
                    rtb.SelectionFont = boldFont;
                    rtb.AppendText(numberPart);

                    if (restPart.Length > 0)
                    {
                        rtb.SelectionColor = textColor;
                        rtb.SelectionFont = normalFont;
                        rtb.AppendText(restPart);
                    }
                }
                // Bullet points with dash
                else if (trimmed.StartsWith('-'))
                {
                    rtb.SelectionColor = accentColor;
                    rtb.SelectionFont = normalFont;
                    rtb.AppendText("-");

                    var restPart = trimmed.Substring(1);
                    if (restPart.Length > 0)
                    {
                        rtb.SelectionColor = textColor;
                        rtb.SelectionFont = normalFont;
                        rtb.AppendText(restPart);
                    }
                }
                // Normal line
                else
                {
                    rtb.SelectionColor = textColor;
                    rtb.SelectionFont = normalFont;
                    rtb.AppendText(trimmed);
                }
            }

            // Add newline except for last line
            if (i < lines.Length - 1)
            {
                rtb.SelectionColor = textColor;
                rtb.SelectionFont = normalFont;
                rtb.AppendText(Environment.NewLine);
            }
        }

        rtb.SelectionStart = 0;
        rtb.ScrollToCaret();

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

        // Apply dark theme scrollbar
        if (theme == ThemeType.Dark)
        {
            ApplyScrollBarThemeStatic(form, true);
        }

        form.ShowDialog(owner);
    }
}
