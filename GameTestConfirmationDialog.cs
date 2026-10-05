using System.Drawing;
using System.Windows.Forms;
using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient;

internal sealed class GameTestConfirmationDialog : Form
{
    private readonly Button _backButton;
    private readonly Button _installButton;

    private GameTestConfirmationDialog(GameTestConfirmationPresentation presentation)
    {
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9.5F);
        Text = "Інформація про реліз";
        AccessibleName = Text;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.PrimaryText;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScroll = true;
        Padding = new Padding(UiTheme.Scale(this, 24));

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 0,
            Width = UiTheme.Scale(this, 460),
            BackColor = UiTheme.Background
        };

        var accent = new Panel
        {
            Height = UiTheme.Scale(this, 3),
            Dock = DockStyle.Top,
            BackColor = UiTheme.Accent,
            Margin = new Padding(0, 0, 0, UiTheme.Scale(this, 16))
        };
        content.Controls.Add(accent);

        var heading = CreateLabel(presentation.Heading, 15F, FontStyle.Bold, UiTheme.PrimaryText);
        heading.AccessibleName = "Статус перевірки релізу";
        heading.Margin = new Padding(0, 0, 0, UiTheme.Scale(this, 12));
        content.Controls.Add(heading);

        foreach (var detail in presentation.ServerDetails)
        {
            var detailLabel = CreateLabel(detail, 9.5F, FontStyle.Regular, UiTheme.AccentSecondaryText);
            detailLabel.Margin = new Padding(0, 0, 0, UiTheme.Scale(this, 8));
            content.Controls.Add(detailLabel);
        }

        var supportingText = CreateLabel(presentation.SupportingText, 9.5F, FontStyle.Regular, UiTheme.SecondaryText);
        supportingText.Margin = new Padding(0, UiTheme.Scale(this, 6), 0, UiTheme.Scale(this, 18));
        content.Controls.Add(supportingText);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Dock = DockStyle.Top,
            BackColor = UiTheme.Background,
            Margin = new Padding(0)
        };

        _installButton = new Button
        {
            Text = "Встановити",
            AutoSize = true,
            MinimumSize = new Size(UiTheme.Scale(this, 112), UiTheme.Scale(this, 38)),
            Margin = new Padding(UiTheme.Scale(this, 10), 0, 0, 0),
            DialogResult = DialogResult.None
        };
        UiTheme.StylePrimaryButton(_installButton);
        _installButton.Click += (_, _) => DialogResult = DialogResult.Yes;

        _backButton = new Button
        {
            Text = "Назад",
            AutoSize = true,
            MinimumSize = new Size(UiTheme.Scale(this, 100), UiTheme.Scale(this, 38)),
            Margin = new Padding(0),
            DialogResult = DialogResult.Cancel
        };
        UiTheme.StyleSecondaryButton(_backButton);
        buttons.Controls.Add(_installButton);
        buttons.Controls.Add(_backButton);
        content.Controls.Add(buttons);

        Controls.Add(content);

        CancelButton = _backButton;
        Shown += (_, _) => _backButton.Focus();
        Load += (_, _) => ClampToWorkingArea();
    }

    internal static bool ShowConfirmation(IWin32Window owner, GameTestInfo? gameTest)
    {
        var presentation = GameTestInstallPolicy.BuildPresentation(gameTest);
        using var dialog = new GameTestConfirmationDialog(presentation);
        return dialog.ShowDialog(owner) == DialogResult.Yes;
    }

    private Label CreateLabel(string text, float fontSize, FontStyle fontStyle, Color color) => new()
    {
        AutoSize = true,
        MaximumSize = new Size(UiTheme.Scale(this, 460), 0),
        Text = text,
        Font = new Font(Font.FontFamily, fontSize, fontStyle),
        ForeColor = color,
        BackColor = UiTheme.Background,
        AccessibleName = text,
        Margin = new Padding(0)
    };

    private void ClampToWorkingArea()
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        var maximumHeight = Math.Max(UiTheme.Scale(this, 320), workingArea.Height - UiTheme.Scale(this, 48));
        if (Height > maximumHeight)
        {
            Height = maximumHeight;
            AutoScroll = true;
        }
    }
}
