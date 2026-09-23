using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Temizlikci.Domain.Projects;
using Temizlikci.Presentation.Developer;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.App.Views.Developer;

/// <summary>How Git state reads: "would deleting this project lose work?", always with a glyph, never color alone.</summary>
internal static class GitViews
{
    private const string WarningGlyph = "\uE7BA";
    private const string PushedGlyph = "\uE73E";

    /// <summary>A compact badge for a project row; a small spinner while Git is read; nothing for non-Git projects.</summary>
    public static FrameworkElement? Badge(DeveloperProject project, GitStatusModel git)
    {
        if (git.State(project) is { } state) return Label(state.HasLocalOnlyWork ? L10n.GitLocalWorkBadge : L10n.GitAllPushedBadge, state.HasLocalOnlyWork, "BadgeTextStyle");
        if (!git.IsLoading(project)) return null;
        double size = Ui.Double("CaptionFontSize");
        return new ProgressRing { IsActive = true, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>The inspector's Git card: a one-line answer, then the counts behind it.</summary>
    public static Border Card(DeveloperProject project, GitStatusModel git)
    {
        var content = new StackPanel { Spacing = Ui.Double("SpacingSmall") };
        content.Children.Add(Ui.Text(L10n.GitTitle, "BodyStrongTextStyle"));
        if (git.State(project) is { } state)
        {
            var summary = Label(state.HasLocalOnlyWork ? L10n.GitLocalWork : L10n.GitAllPushed, state.HasLocalOnlyWork, "BodyTextStyle");
            content.Children.Add(summary);
            content.Children.Add(Details(state));
        }
        else if (git.IsLoading(project))
        {
            var reading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Ui.Double("SpacingXSmall") };
            reading.Children.Add(new ProgressRing { IsActive = true, Width = Ui.Double("IconSize"), Height = Ui.Double("IconSize") });
            reading.Children.Add(Ui.Wrapped(L10n.GitReading, "SecondaryTextStyle"));
            content.Children.Add(reading);
        }
        else
        {
            content.Children.Add(Ui.Wrapped(L10n.GitUnreadable, "CaptionTextStyle"));
        }
        return Ui.Framed(content);
    }

    private static StackPanel Details(GitState state)
    {
        var details = new StackPanel { Spacing = Ui.Double("SpacingXSmall") };
        details.Children.Add(Row(L10n.GitBranch, state.Branch ?? L10n.GitDetached));
        if (!state.HasRemote) details.Children.Add(Ui.Wrapped(L10n.GitNoRemote, "BodyTextStyle"));
        void Count(string label, int value)
        {
            if (value > 0) details.Children.Add(Row(label, Format.Count(value)));
        }
        Count(L10n.GitUnpushedCommits, state.UnpushedCommits);
        Count(L10n.GitBehind, state.Behind);
        Count(L10n.GitStaged, state.Staged);
        Count(L10n.GitUnstaged, state.Unstaged);
        Count(L10n.GitUntracked, state.Untracked);
        Count(L10n.GitConflicted, state.Conflicted);
        Count(L10n.GitStashes, state.Stashes);
        if (state.UnpushedBranches.Count > 0)
        {
            var title = Ui.Text(L10n.GitBranchesOnlyHere, "SecondaryTextStyle");
            title.Margin = new Thickness(0, Ui.Double("SpacingXXSmall"), 0, 0);
            details.Children.Add(title);
            foreach (var branch in state.UnpushedBranches)
            {
                var row = Row(branch.Name, Format.Count(branch.Commits));
                foreach (var text in row.Children.OfType<TextBlock>()) text.Style = (Style)Application.Current.Resources["CaptionTextStyle"];
                details.Children.Add(row);
            }
        }
        return details;
    }

    /// <summary>A label that may wrap in the narrow inspector, and a number that never does.</summary>
    private static Grid Row(string label, string value)
    {
        var row = new Grid { ColumnSpacing = Ui.Double("SpacingSmall") };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labelText = Ui.Wrapped(label, "BodyTextStyle");
        row.Children.Add(labelText);
        var valueText = Ui.Text(value, "BodyTextStyle");
        valueText.IsTextSelectionEnabled = true;
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        return row;
    }

    /// <summary>Glyph and words; a grid rather than a horizontal stack, so long words wrap in the narrow inspector.</summary>
    private static Grid Label(string text, bool attention, string style)
    {
        var ink = Ui.Brush(attention ? "AttentionInkBrush" : "TextFillColorSecondaryBrush");
        var label = new Grid { ColumnSpacing = Ui.Double("SpacingXSmall"), VerticalAlignment = VerticalAlignment.Center };
        label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        label.Children.Add(new FontIcon { Glyph = attention ? WarningGlyph : PushedGlyph, FontSize = Ui.Double("CaptionFontSize"), Foreground = ink, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, Ui.Double("SpacingXXSmall"), 0, 0) });
        var words = Ui.Wrapped(text, style);
        words.Foreground = ink;
        Grid.SetColumn(words, 1);
        label.Children.Add(words);
        AutomationProperties.SetName(label, text);
        return label;
    }
}
