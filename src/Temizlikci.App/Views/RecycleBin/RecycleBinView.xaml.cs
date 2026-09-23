using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Temizlikci.Domain.Tree;
using Temizlikci.Presentation.Actions;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.App.Views.RecycleBin;

/// <summary>What the app moved to the Recycle Bin this session, with Put Back — or an empty state explaining the view.</summary>
public sealed partial class RecycleBinView : UserControl
{
    private readonly MainViewModel main;

    public RecycleBinView(MainViewModel main)
    {
        this.main = main;
        InitializeComponent();
        main.Ledger.PropertyChanged += OnLedgerChanged;
        // Items emptied from the Recycle Bin since the list was last shown drop out when it appears.
        Loaded += async (_, _) => await main.Ledger.ReconcileAsync();
        Unloaded += (_, _) => main.Ledger.PropertyChanged -= OnLedgerChanged;
        Update();
    }

    private void OnLedgerChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        var records = main.Ledger.Records;
        bool empty = records.Count == 0;
        Records.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        TotalText.Text = L10n.RecycleTotal(Format.Bytes(main.Ledger.TotalSize));
        Rows.ItemsSource = records.Select(record => new RecycleRow(record)).ToList();
    }

    private async void OnPutBack(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RecycleRow row) return;
        if (main.PutBack(row.Record) is not { } error || XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = error.Message,
            Content = error.Suggestion,
            CloseButtonText = L10n.AlertOk,
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private void OnOpenRecycleBin(object sender, RoutedEventArgs e) => main.Ledger.OpenRecycleBin();
}

/// <summary>One recycled item as the list shows it.</summary>
public sealed class RecycleRow(RecycleRecord record)
{
    public RecycleRecord Record { get; } = record;
    public string Name => Record.Name;
    public string OriginalPath => Record.OriginalPath;
    public string FromText => L10n.RecycleFrom(Path.GetDirectoryName(Record.OriginalPath) ?? Record.OriginalPath);
    public string SizeText => Format.Bytes(Record.Node.AllocatedSize);
    /// <summary>What screen readers say for the row.</summary>
    public override string ToString() => $"{Name}, {SizeText}";

    public string Glyph => Record.Node.Kind == NodeKind.Directory ? "\uE8B7" : "\uE8A5";
}
