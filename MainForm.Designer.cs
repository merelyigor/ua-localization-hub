#nullable enable
namespace BdoClient;

partial class MainForm
{
    private System.ComponentModel.IContainer? components;
    private Panel rootScrollPanel = null!;
    private TableLayoutPanel mainLayoutPanel = null!;
    private Panel headerPanel = null!;
    private Label headerTitleLabel = null!;
    private Label headerSubtitleLabel = null!;
    private Label targetProjectsCaptionLabel = null!;
    private Label bdoTargetProjectLabel = null!;
    private Label bdoTargetStatusLabel = null!;
    private Label wwmTargetProjectLabel = null!;
    private Label wwmTargetStatusLabel = null!;
    private Label gameSelectorLabel = null!;
    private ComboBox gameSelectorComboBox = null!;
    private Panel headerAccentLine = null!;
    private FlowLayoutPanel rightUtilityPanel = null!;
    private Button updateButton = null!;
    private Label versionLabel = null!;
    private LinkLabel uninstallHelpLink = null!;
    private Button logsButton = null!;
    private ToolTip logsToolTip = null!;

    private BdoSurfacePanel gameGroupBox = null!;
    private Label gameSectionCaptionLabel = null!;
    private Label gameStatusLabel = null!;
    private Label gamePathLabel = null!;
    private Button detectGameButton = null!;
    private Button browseGameButton = null!;
    private Button restoreOriginalButton = null!;

    private Panel modeGroupBox = null!;
    private Label modeSectionCaptionLabel = null!;
    private Panel modesFlowPanel = null!;

    private BdoSurfacePanel operationStrip = null!;
    private Label operationMessageLabel = null!;
    private BdoProgressBar progressBar = null!;
    private Label progressLabel = null!;
    private Button cancelButton = null!;


    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1040, 720);
        MinimumSize = new Size(760, 560);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = ApplicationBrand.DisplayName;

        mainLayoutPanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 4, Padding = new Padding(24), BackColor = Color.Transparent };
        mainLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var row = 0; row < 4; row++) mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        headerPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 22), BackColor = Color.Transparent };
        var headerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 5, BackColor = Color.Transparent };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var row = 0; row < 5; row++) headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerTitleLabel = new Label { Text = ApplicationBrand.DisplayName, AutoSize = true, Font = new Font("Segoe UI", 20F, FontStyle.Bold), ForeColor = UiTheme.PrimaryText, Margin = new Padding(0) };
        headerSubtitleLabel = new Label { Text = ApplicationBrand.GenericSubtitle, AutoSize = true, Font = new Font("Segoe UI", 9.5F), ForeColor = UiTheme.SecondaryText, Margin = new Padding(0, 3, 0, 0) };
        targetProjectsCaptionLabel = new Label { Text = ApplicationBrand.TargetProjectsCaption, AutoSize = true, AccessibleName = ApplicationBrand.TargetProjectsCaption, Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = UiTheme.PrimaryText, Margin = new Padding(0, 8, 0, 2) };
        bdoTargetProjectLabel = new Label { Text = ApplicationBrand.BdoTargetProject, AutoSize = true, AccessibleName = ApplicationBrand.BdoTargetProject, ForeColor = UiTheme.SecondaryText, Margin = new Padding(0, 0, 12, 0) };
        bdoTargetStatusLabel = new Label { Text = ApplicationBrand.BdoTargetStatus, AutoSize = true, AccessibleName = $"{ApplicationBrand.BdoTargetProject}: {ApplicationBrand.BdoTargetStatus}", ForeColor = UiTheme.Success, Margin = new Padding(0) };
        wwmTargetProjectLabel = new Label { Text = ApplicationBrand.WwmTargetProject, AutoSize = true, AccessibleName = ApplicationBrand.WwmTargetProject, ForeColor = UiTheme.SecondaryText, Margin = new Padding(0, 2, 12, 0) };
        wwmTargetStatusLabel = new Label { Text = ApplicationBrand.WwmTargetStatus, AutoSize = true, AccessibleName = $"{ApplicationBrand.WwmTargetProject}: {ApplicationBrand.WwmTargetStatus}", ForeColor = UiTheme.Success, Margin = new Padding(0, 2, 0, 0) };
        var bdoTargetRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
        bdoTargetRow.Controls.Add(bdoTargetProjectLabel); bdoTargetRow.Controls.Add(bdoTargetStatusLabel);
        var wwmTargetRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
        wwmTargetRow.Controls.Add(wwmTargetProjectLabel); wwmTargetRow.Controls.Add(wwmTargetStatusLabel);
        var targetProjectsPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
        targetProjectsPanel.Controls.Add(targetProjectsCaptionLabel); targetProjectsPanel.Controls.Add(bdoTargetRow); targetProjectsPanel.Controls.Add(wwmTargetRow);
        gameSelectorLabel = new Label { Text = "Активна гра", AutoSize = true, AccessibleName = "Вибір активної гри", ForeColor = UiTheme.SecondaryText, Margin = new Padding(0, 8, 8, 0) };
        gameSelectorComboBox = new ComboBox { AutoSize = true, DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, AccessibleName = "Вибір гри", Margin = new Padding(0, 4, 0, 0) };
        var gameSelectorPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
        gameSelectorPanel.Controls.Add(gameSelectorLabel); gameSelectorPanel.Controls.Add(gameSelectorComboBox);
        headerAccentLine = new Panel { Height = 2, Dock = DockStyle.Fill, BackColor = UiTheme.Accent, Margin = new Padding(0, 10, 0, 0) };
        rightUtilityPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = new Padding(0, 4, 0, 0), BackColor = Color.Transparent };
        updateButton = new Button { Text = "Оновити до vX.Y.Z", AutoSize = true, Visible = false, Margin = new Padding(0, 0, 10, 0) };
        versionLabel = new Label { AutoSize = true, Font = new Font("Segoe UI", 8.5F), ForeColor = UiTheme.SecondaryText, Margin = new Padding(0, 8, 10, 0) };
        uninstallHelpLink = new LinkLabel
        {
            Text = "Як видалити застосунок?",
            AutoSize = true,
            LinkColor = UiTheme.SecondaryText,
            ActiveLinkColor = UiTheme.PrimaryText,
            VisitedLinkColor = UiTheme.SecondaryText,
            LinkBehavior = LinkBehavior.HoverUnderline,
            AccessibleName = "Як видалити застосунок?",
            Margin = new Padding(0, 8, 10, 0)
        };
        logsButton = new Button { Text = "", AutoSize = false, Size = new Size(32, 32), FlatStyle = FlatStyle.Flat, AccessibleName = "Відкрити папку журналів", Image = BuildLogsIcon(), ImageAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0) };
        logsButton.FlatAppearance.BorderSize = 0;
        rightUtilityPanel.Controls.AddRange(new Control[] { updateButton, versionLabel, uninstallHelpLink, logsButton });
        headerLayout.Controls.Add(headerTitleLabel, 0, 0); headerLayout.Controls.Add(headerSubtitleLabel, 0, 1); headerLayout.Controls.Add(targetProjectsPanel, 0, 2); headerLayout.Controls.Add(gameSelectorPanel, 0, 3); headerLayout.Controls.Add(headerAccentLine, 0, 4); headerLayout.Controls.Add(rightUtilityPanel, 1, 0); headerPanel.Controls.Add(headerLayout);
        components = new System.ComponentModel.Container(); logsToolTip = new ToolTip(components); logsToolTip.SetToolTip(logsButton, "Відкрити папку журналів"); logsToolTip.SetToolTip(uninstallHelpLink, "Застосунок portable. Для видалення повністю вийдіть із програми та видаліть EXE.");

        gameGroupBox = new BdoSurfacePanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20), Margin = new Padding(0, 0, 0, 24), SurfaceColor = UiTheme.SurfaceElevated };
        gameSectionCaptionLabel = CreateSectionCaption(ApplicationBrand.DisplayName);
        gameStatusLabel = new Label { Text = "Гра ще не перевірена", AutoSize = true, Font = new Font("Segoe UI", 11F, FontStyle.Bold), ForeColor = UiTheme.SecondaryText, Margin = new Padding(0, 0, 0, 5) };
        gamePathLabel = new Label { AutoSize = false, Height = 24, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = UiTheme.SecondaryText, Margin = new Padding(0, 0, 14, 0) };
        detectGameButton = new Button { Text = "Знайти автоматично", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        browseGameButton = new Button { Text = "Обрати папку", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        restoreOriginalButton = new Button { Text = "Відновити оригінал", AutoSize = true, Enabled = false, Margin = new Padding(0) };
        var gameActions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Color.Transparent };
        gameActions.Controls.AddRange(new Control[] { detectGameButton, browseGameButton, restoreOriginalButton });
        var gameLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 3, BackColor = Color.Transparent };
        gameLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); gameLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var row = 0; row < 3; row++) gameLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gameLayout.Controls.Add(gameSectionCaptionLabel, 0, 0); gameLayout.SetColumnSpan(gameSectionCaptionLabel, 2); gameLayout.Controls.Add(gameStatusLabel, 0, 1); gameLayout.Controls.Add(gameActions, 1, 1); gameLayout.Controls.Add(gamePathLabel, 0, 2); gameLayout.SetColumnSpan(gamePathLabel, 2); gameGroupBox.Controls.Add(gameLayout);

        modeGroupBox = new Panel { Dock = DockStyle.Top, AutoSize = false, Height = 300, Margin = new Padding(0, 0, 0, 20), BackColor = Color.Transparent };
        modeSectionCaptionLabel = CreateSectionCaption("Локалізація");
        modesFlowPanel = new Panel { Dock = DockStyle.Fill, AutoSize = false, Margin = new Padding(0), Padding = new Padding(0, 12, 0, 0), BackColor = Color.Transparent };
        modeGroupBox.Controls.Add(modesFlowPanel); modeGroupBox.Controls.Add(modeSectionCaptionLabel);

        operationStrip = new BdoSurfacePanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16, 12, 16, 12), Margin = new Padding(0), Visible = false, SurfaceColor = UiTheme.SurfaceElevated };
        operationMessageLabel = new Label { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = UiTheme.PrimaryText, Margin = new Padding(0, 0, 14, 0), Anchor = AnchorStyles.Left };
        progressBar = new BdoProgressBar { Dock = DockStyle.Fill, MinimumSize = new Size(160, 16), Height = 16, Margin = new Padding(0, 4, 8, 0), Anchor = AnchorStyles.Left | AnchorStyles.Right };
        progressLabel = new Label { Text = "0%", AutoSize = true, ForeColor = UiTheme.SecondaryText, Margin = new Padding(0, 5, 14, 0), Anchor = AnchorStyles.Left };
        cancelButton = new Button { Text = "Скасувати", AutoSize = true, Enabled = false, Visible = false, Margin = new Padding(0), Anchor = AnchorStyles.Right };
        var operationLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1, BackColor = Color.Transparent };
        operationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); operationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); operationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); operationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        operationLayout.Controls.Add(operationMessageLabel, 0, 0); operationLayout.Controls.Add(progressBar, 1, 0); operationLayout.Controls.Add(progressLabel, 2, 0); operationLayout.Controls.Add(cancelButton, 3, 0); operationStrip.Controls.Add(operationLayout);

        mainLayoutPanel.Controls.Add(headerPanel, 0, 0); mainLayoutPanel.Controls.Add(gameGroupBox, 0, 1); mainLayoutPanel.Controls.Add(modeGroupBox, 0, 2); mainLayoutPanel.Controls.Add(operationStrip, 0, 3);
        rootScrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Background, Padding = new Padding(0) };
        rootScrollPanel.Controls.Add(mainLayoutPanel); Controls.Add(rootScrollPanel); ResumeLayout(false);
    }

    private static Label CreateSectionCaption(string text) => new() { Text = text, AutoSize = true, Dock = DockStyle.Top, Font = new Font("Segoe UI", 14F, FontStyle.Bold), ForeColor = UiTheme.PrimaryText, Margin = new Padding(0) };
}
