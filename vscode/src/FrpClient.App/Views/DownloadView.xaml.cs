using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace FrpClient.App.Views;

public partial class DownloadView : UserControl
{
    private const string AllArchitectureOption = "全部";

    private readonly List<FrpReleaseInfo> _allReleases = [];
    private readonly ObservableCollection<FrpReleaseInfo> _filteredReleases = [];
    private readonly ObservableCollection<string> _architectureOptions = [];
    private bool _isBusy;
    private bool _isServiceRunning;
    private double? _downloadProgressPercent;

    public DownloadView()
    {
        InitializeComponent();
        ReleaseDataGrid.ItemsSource = _filteredReleases;
        ArchitectureFilterComboBox.ItemsSource = _architectureOptions;
        RefreshArchitectureOptions();
        UpdateEmptyState();
    }

    public event EventHandler? RefreshRequested;

    public event EventHandler<FrpReleaseDownloadRequestedEventArgs>? DownloadRequested;

    public void SetCurrentVersion(string version)
    {
        CurrentFrpcVersionTextBlock.Text = string.IsNullOrWhiteSpace(version) ? "-" : $"frpc {version}";
    }

    public void SetStatus(string status)
    {
        ReleaseStatusTextBlock.Text = status;
    }

    public void SetDownloadProgress(double? percent, string text)
    {
        _downloadProgressPercent = percent;
        DownloadProgressGrid.Visibility = Visibility.Visible;
        DownloadProgressTextBlock.Text = text;
        DownloadProgressFillBorder.Opacity = percent is null ? 0.72 : 1;
        UpdateDownloadProgressFill();
    }

    public void ResetDownloadProgress()
    {
        _downloadProgressPercent = 0;
        DownloadProgressFillBorder.Width = 0;
        DownloadProgressFillBorder.Opacity = 1;
        DownloadProgressTextBlock.Text = "";
        DownloadProgressGrid.Visibility = Visibility.Collapsed;
    }

    public void SetReleases(IEnumerable<FrpReleaseInfo> releases)
    {
        _allReleases.Clear();
        _allReleases.AddRange(releases);
        RefreshArchitectureOptions();
        ApplyFilter();
    }

    public void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        UpdateEnabledState();
    }

    public void SetServiceRunning(bool isServiceRunning)
    {
        _isServiceRunning = isServiceRunning;
        UpdateEnabledState();
    }

    private void RefreshReleasesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DownloadReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FrpReleaseInfo release)
        {
            DownloadRequested?.Invoke(this, new FrpReleaseDownloadRequestedEventArgs(release));
        }
    }

    private void SearchReleaseTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ArchitectureFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void DownloadProgressTrackBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDownloadProgressFill();
    }

    private void ApplyFilter()
    {
        var keyword = SearchReleaseTextBox.Text.Trim();
        var selectedArchitecture = ArchitectureFilterComboBox.SelectedItem?.ToString() ?? AllArchitectureOption;
        var filtered = _allReleases.AsEnumerable();

        if (!selectedArchitecture.Equals(AllArchitectureOption, StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(release =>
                release.PlatformArchitecture.Equals(selectedArchitecture, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filtered = filtered.Where(release =>
                    release.Version.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    release.TagName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    release.ReleaseName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    release.AssetName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    release.Platform.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    release.Architecture.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    release.PlatformArchitecture.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        _filteredReleases.Clear();

        foreach (var release in filtered)
        {
            _filteredReleases.Add(release);
        }

        ReleaseStatusTextBlock.Text = _allReleases.Count == 0
            ? ReleaseStatusTextBlock.Text
            : $"显示 {_filteredReleases.Count} / {_allReleases.Count} 个包";

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        ReleaseEmptyTextBlock.Visibility = _filteredReleases.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshArchitectureOptions()
    {
        var currentSelection = ArchitectureFilterComboBox.SelectedItem?.ToString();
        var options = _allReleases
            .Select(release => release.PlatformArchitecture)
            .Where(option => !string.IsNullOrWhiteSpace(option) && option != "-")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => option.StartsWith("windows_", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(option => option, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _architectureOptions.Clear();
        _architectureOptions.Add(AllArchitectureOption);

        foreach (var option in options)
        {
            _architectureOptions.Add(option);
        }

        ArchitectureFilterComboBox.SelectedItem =
            !string.IsNullOrWhiteSpace(currentSelection) && _architectureOptions.Contains(currentSelection)
                ? currentSelection
                : AllArchitectureOption;
    }

    private void UpdateEnabledState()
    {
        RefreshReleasesButton.IsEnabled = !_isBusy;
        SearchReleaseTextBox.IsEnabled = !_isBusy;
        ArchitectureFilterComboBox.IsEnabled = !_isBusy;
        ReleaseDataGrid.IsEnabled = !_isBusy && !_isServiceRunning;
    }

    private void UpdateDownloadProgressFill()
    {
        var trackWidth = DownloadProgressTrackBorder.ActualWidth;

        if (trackWidth <= 0)
        {
            return;
        }

        if (_downloadProgressPercent is null)
        {
            DownloadProgressFillBorder.Width = Math.Max(72, trackWidth * 0.32);
            return;
        }

        var percent = Math.Clamp(_downloadProgressPercent.Value, 0, 100);
        DownloadProgressFillBorder.Width = trackWidth * percent / 100;
    }
}
