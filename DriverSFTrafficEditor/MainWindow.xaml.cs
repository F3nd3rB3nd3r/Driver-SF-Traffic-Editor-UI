using System.IO;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using DriverSFTrafficEditor.Core;
using DriverSFTrafficEditor.Models;
using Microsoft.Win32;

namespace DriverSFTrafficEditor;

public partial class MainWindow : Window
{
    private TrafficDocument? _document;
    private ICollectionView? _setView;
    private ICollectionView? _weightView;
    private ICollectionView? _profileView;
    private string _lastSaveName = "san_franciscoAI_edited.fchunk";
    private string? _lastSavedSha256;

    // Swap brush dictionaries in-place so all DynamicResource-bound text, grid,
    // dropdown and button colors update without losing the current document.
    private bool _darkTheme;

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        _darkTheme = !_darkTheme;
        var merged = Application.Current.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary
        {
            Source = new Uri(_darkTheme
                ? "pack://application:,,,/DriverSFTrafficEditor;component/Themes/Dark.xaml"
                : "pack://application:,,,/DriverSFTrafficEditor;component/Themes/Light.xaml", UriKind.Absolute)
        };
        if (merged.Count == 0) merged.Add(replacement);
        else merged[0] = replacement;
        ThemeButton.Content = _darkTheme ? "Light mode" : "Dark mode";
    }

    private bool HasUnsavedChanges()
    {
        if (_document?.HasChanges != true) return false;
        try
        {
            string now = Convert.ToHexString(SHA256.HashData(_document.PreparePatch().Data));
            return !string.Equals(now, _lastSavedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // An invalid pending edit must not be silently discarded.
            return true;
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        SetEditingEnabled(false);
    }

    private void SetEditingEnabled(bool ready)
    {
        SaveButton.IsEnabled = ready;
        ReportButton.IsEnabled = ready;
        ReplaceSpoolButton.IsEnabled = ready;
        TransferButton.IsEnabled = ready;
        AddVehicleButton.IsEnabled = ready;
        RemoveVehicleButton.IsEnabled = ready;
        SetWeightButton.IsEnabled = ready;
        ImportLuaButton.IsEnabled = ready;
        CapacityButton.IsEnabled = ready;
    }

    private bool ConfirmDiscard()
    {
        if (!HasUnsavedChanges()) return true;
        return MessageBox.Show("You have unsaved modifications. Discard them and open another file?",
            "Unsaved edits", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var picker = new OpenFileDialog
        {
            Title = "Open retail PC san_franciscoAI.fchunk",
            Filter = "Driver SF traffic resource (*.fchunk)|*.fchunk|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var doc = TrafficDocument.Load(picker.FileName);
            if (_document is not null) _document.Changed -= DocumentChanged;
            _document = doc;
            doc.Changed += DocumentChanged;
            _lastSavedSha256 = null;
            _lastSaveName = Path.GetFileNameWithoutExtension(picker.FileName) + "_edited.fchunk";
            FileLabel.Text = $"SOURCE  {doc.SourcePath}     |     {doc.OriginalSha256[..16]}… SHA-256";
            SetsList.ItemsSource = doc.Sets;
            _setView = CollectionViewSource.GetDefaultView(SetsList.ItemsSource);
            _setView.Filter = FilterSet;
            SetSearchBox.Clear();
            SetsList.SelectedItem = doc.Sets.FirstOrDefault(s => s.Name.Equals("Chapter 1", StringComparison.OrdinalIgnoreCase)) ?? doc.Sets[0];
            ProfilesList.ItemsSource = doc.Profiles;
            _profileView = CollectionViewSource.GetDefaultView(ProfilesList.ItemsSource);
            _profileView.Filter = FilterProfile;
            _profileView.SortDescriptions.Add(new SortDescription(nameof(WeightProfile.Number), ListSortDirection.Ascending));
            ProfileNumberSearchBox.Clear();
            _profileView.Refresh();
            ProfilesList.SelectedItem = doc.Profiles.FirstOrDefault(p => p.Number == 26) ?? doc.Profiles[0];
            UpdateProfileCount();
            VehicleResults.Text = "Enter a vehicle ID above to find its locations in this resource.";
            SetEditingEnabled(true);
            UpdateStatus($"Loaded and validated: {doc.Sets.Count} sets, {doc.Profiles.Count} editable weight tables.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot open this file:\n\n{ex.Message}", "Unsupported or corrupt resource",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DocumentChanged(object? sender, EventArgs e)
    {
        UpdateStatus();
        RefreshProfileTotal();
        RefreshProfileInfo();
    }

    private void RefreshProfileInfo()
    {
        if (ProfilesList.SelectedItem is WeightProfile p)
            ProfileInfo.Text = $"Loaded metadata {p.MetaHex} | loaded rows {p.DataHex} | {p.Rows.Count} ID/Float32 rows | {ProfilesList.SelectedItems.Count} table(s) selected";
    }

    private WeightProfile[] TargetProfiles() => _document is null ? Array.Empty<WeightProfile>() :
        AllTablesCheck.IsChecked == true ? _document.Profiles.ToArray() : ProfilesList.SelectedItems.Cast<WeightProfile>().ToArray();

    private void Capacity_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || !CommitEdits()) return;
        VehicleResults.Text = _document.CapacityReport();
        MainTabs.SelectedIndex = 2;
    }

    private void AddVehicles_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || !CommitEdits()) return;
        try
        {
            uint[] ids = VehicleIdInput.Parse(AddVehicleIds.Text);
            if (!float.TryParse(InitialWeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float weight) ||
                !float.IsFinite(weight) || weight < 0)
                throw new FormatException("Initial weight must be a finite nonnegative Float32 value; use a decimal point.");
            var targets = TargetProfiles();
            if (targets.Length == 0) throw new InvalidOperationException("Select at least one probability table.");
            int added = _document.AddVehicles(targets, ids, weight);
            UpdateStatus($"Added {added} row(s) across {targets.Length} table(s). Existing IDs and weights were kept.");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Cannot add vehicles", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ImportVehicleStats_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Import vehicle IDs from vehicleStats",
            Filter = "Lua vehicle stats (*.lua)|*.lua|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            uint[] ids = VehicleStatsInput.Parse(File.ReadAllText(picker.FileName));
            AddVehicleIds.Text = string.Join(", ", ids);
            UpdateStatus($"Imported {ids.Length} unique vehicle IDs from {Path.GetFileName(picker.FileName)}. Choose the tables and weight, then apply an action.");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Cannot import vehicle IDs", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void SetWeights_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || !CommitEdits()) return;
        try
        {
            uint[] ids = VehicleIdInput.Parse(AddVehicleIds.Text);
            if (!float.TryParse(InitialWeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float weight) ||
                !float.IsFinite(weight) || weight < 0)
                throw new FormatException("Weight must be a finite nonnegative Float32 value; use a decimal point.");
            var targets = TargetProfiles();
            if (targets.Length == 0) throw new InvalidOperationException("Select at least one probability table.");
            var result = _document.SetVehicleWeights(targets, ids, weight, CreateMissingCheck.IsChecked == true);
            int maximum = _document.CapacityRequirements().Select(r => r.UnionIds).DefaultIfEmpty().Max();
            UpdateStatus($"Set weight {weight:G9} across {targets.Length} table(s): {result.Updated} updated, {result.Added} added, {result.MissingSkipped} missing skipped. Max routed roster: {maximum} IDs.");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Cannot set weights", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void RemoveVehicles_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || !CommitEdits()) return;
        try
        {
            uint[] ids = string.IsNullOrWhiteSpace(AddVehicleIds.Text)
                ? WeightGrid.SelectedItems.Cast<WeightRow>().Select(r => r.VehicleId).Distinct().ToArray()
                : VehicleIdInput.Parse(AddVehicleIds.Text);
            if (ids.Length == 0) throw new InvalidOperationException("Enter IDs above, or select rows in the grid with the ID box empty.");
            var targets = TargetProfiles();
            if (targets.Length == 0) throw new InvalidOperationException("Select at least one probability table.");
            int removed = _document.RemoveVehicles(targets, ids);
            UpdateStatus($"Removed {removed} row(s) across {targets.Length} table(s).");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Cannot remove vehicles", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private bool FilterSet(object item)
    {
        if (item is not TrafficSet set) return false;
        string text = SetSearchBox?.Text?.Trim() ?? "";
        return text.Length == 0 || set.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            set.HashHex.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            set.Index.ToString(CultureInfo.InvariantCulture).Equals(text, StringComparison.OrdinalIgnoreCase);
    }

    private void SetSearch_Changed(object sender, TextChangedEventArgs e) => _setView?.Refresh();

    private void SetsList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SetsList.SelectedItem is not TrafficSet set)
        {
            SetTitle.Text = "Select a traffic setting";
            SetInfo.Text = "";
            GroupPicker.ItemsSource = null;
            SpoolGrid.ItemsSource = null;
            return;
        }
        SetTitle.Text = set.Name;
        SetInfo.Text = $"Record {set.Index}   •   CRC32 {set.HashHex}   •   metadata {set.MetaHex}   •   {set.IdCount} native spool IDs";
        GroupPicker.ItemsSource = set.Groups;
        GroupPicker.SelectedIndex = 0;
    }

    private void GroupPicker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (GroupPicker.SelectedItem is not SpoolGroup group)
        {
            SpoolGrid.ItemsSource = null;
            GroupOffset.Text = "";
            return;
        }
        SpoolGrid.ItemsSource = group.Rows;
        GroupOffset.Text = group.Count > 0 ? $"First ID: {group.OffsetHex}   •   {group.Count} slots" : "Empty group (no vehicle IDs)";
    }

    private bool FilterProfile(object item)
    {
        if (item is not WeightProfile profile) return false;
        string query = ProfileNumberSearchBox?.Text?.Trim() ?? "";
        return query.Length == 0 ||
            profile.Definition.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateProfileCount()
    {
        if (ProfileCountLabel is null || _profileView is null) return;
        int count = _profileView.Cast<object>().Count();
        ProfileCountLabel.Text = $"Showing {count} of {_document?.Profiles.Count ?? 0} tables";
    }

    private void ProfileFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_profileView is null) return;
        _profileView.Refresh();
        UpdateProfileCount();
    }

    private void ClearProfileFilter_Click(object sender, RoutedEventArgs e)
    {
        ProfileNumberSearchBox.Clear();
    }

    private void ProfilesList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not WeightProfile profile)
        {
            ProfileTitle.Text = "Choose a probability profile";
            ProfileInfo.Text = "";
            WeightGrid.ItemsSource = null;
            TotalLabel.Text = "";
            return;
        }
        ProfileTitle.Text = $"Table {profile.Number} — {profile.Definition.Name}";
        RefreshProfileInfo();
        WeightGrid.ItemsSource = profile.Rows;
        _weightView = CollectionViewSource.GetDefaultView(WeightGrid.ItemsSource);
        _weightView.Filter = FilterWeight;
        _weightView.Refresh();
        RefreshProfileTotal();
    }

    private void RefreshProfileTotal()
    {
        if (TotalLabel is null) return;
        if (ProfilesList.SelectedItem is WeightProfile p)
            TotalLabel.Text = $"Total weight: {p.CurrentTotal.ToString("0.####", CultureInfo.CurrentCulture)} | {p.Rows.Count(r => r.Weight > 0)} positive IDs";
        else TotalLabel.Text = "";
    }

    private bool FilterWeight(object obj)
    {
        if (obj is not WeightRow row) return false;
        string filter = WeightSearchBox?.Text?.Trim() ?? "";
        if (filter.Length == 0) return true;
        return row.VehicleId.ToString(CultureInfo.InvariantCulture).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            row.IdHex.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void WeightSearch_Changed(object sender, TextChangedEventArgs e) => _weightView?.Refresh();

    private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        // WPF commits a TwoWay cell binding after this event. Update the status after the commit.
        Dispatcher.BeginInvoke(new Action(() => { UpdateStatus(); RefreshProfileTotal(); }), DispatcherPriority.Background);
    }

    private static bool TryVehicleId(string? input, out uint value)
    {
        string text = input?.Trim() ?? "";
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private bool CommitEdits()
    {
        // Refuse a save while an inline DataGrid editor has not accepted its value.
        bool ok = SpoolGrid.CommitEdit(DataGridEditingUnit.Cell, true) &&
                  SpoolGrid.CommitEdit(DataGridEditingUnit.Row, true) &&
                  WeightGrid.CommitEdit(DataGridEditingUnit.Cell, true) &&
                  WeightGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (!ok)
            MessageBox.Show("Finish or correct the invalid grid edit before continuing.", "Edit not committed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        return ok;
    }

    private void ReplaceSpool_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitEdits()) return;
        if (GroupPicker.SelectedItem is not SpoolGroup g || SetsList.SelectedItem is not TrafficSet s)
            return;
        if (!TryVehicleId(OldSpoolId.Text, out uint oldId) ||
            !TryVehicleId(NewSpoolId.Text, out uint newId) || newId > ushort.MaxValue)
        {
            MessageBox.Show("Enter valid decimal IDs (or 0x-prefixed hex), between 0 and 65535.");
            return;
        }
        SpoolRow? source = g.Rows.FirstOrDefault(r => r.VehicleId == oldId);
        if (source is null)
        {
            MessageBox.Show($"ID {oldId} was not found in {s.Name}, group {g.Index}.");
            return;
        }
        if (g.Rows.Any(r => r.VehicleId == newId && !ReferenceEquals(r, source)))
        {
            MessageBox.Show($"ID {newId} is already in this group. Duplicate group IDs are not supported.");
            return;
        }
        if (oldId == newId) return;
        source.VehicleId = checked((int)newId);
        SpoolGrid.ScrollIntoView(source);
        UpdateStatus($"Replaced {oldId} → {newId} in {s.Name} / group {g.Index}. Check associated weights before saving.");
    }

    private void TransferWeight_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitEdits()) return;
        if (ProfilesList.SelectedItem is not WeightProfile profile) return;
        if (!TryVehicleId(WeightFromId.Text, out uint oldId) || !TryVehicleId(WeightToId.Text, out uint newId))
        {
            MessageBox.Show("Enter valid decimal IDs or 0x-prefixed hex IDs.");
            return;
        }
        if (oldId == newId) return;
        var src = profile.Rows.FirstOrDefault(r => r.VehicleId == oldId);
        var dst = profile.Rows.FirstOrDefault(r => r.VehicleId == newId);
        if (src is null || dst is null)
        {
            MessageBox.Show("Both IDs must be present in this table. Use Add or remove vehicle rows to insert a missing ID first.");
            return;
        }
        if (src.Weight == 0)
        {
            MessageBox.Show($"ID {oldId} already has weight 0 in table {profile.Number}.");
            return;
        }
        if (dst.Weight + src.Weight > float.MaxValue)
        {
            MessageBox.Show("This transfer would overflow Float32.");
            return;
        }
        double updated = dst.Weight + src.Weight;
        src.Weight = 0;
        dst.Weight = updated;
        WeightGrid.ScrollIntoView(dst);
        UpdateStatus($"Table {profile.Number}: moved all weight from ID {oldId} to ID {newId}. The total remains the same (apart from Float32 rounding).");
    }

    private static bool ProtectedActorsEdited(TrafficDocument doc) =>
        doc.Sets.SelectMany(s => s.Groups).SelectMany(g => g.Rows)
           .Any(r => r.IsModified && new[] { 144, 176, 287, 298 }.Contains(r.OriginalId));

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (_document is not { } doc || !CommitEdits()) return;
        if (!doc.HasChanges)
        {
            MessageBox.Show("There are no modified entries to save.", "No changes");
            return;
        }
        if (ProtectedActorsEdited(doc))
        {
            var answer = MessageBox.Show("You changed one or more mission-important source IDs (144, 176, 287, 298). This may break missions. Save anyway?",
                "Mission vehicle warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }
        var picker = new SaveFileDialog
        {
            Title = "Save a NEW patched PC traffic resource",
            Filter = "Driver SF traffic resource (*.fchunk)|*.fchunk",
            AddExtension = true,
            DefaultExt = ".fchunk",
            FileName = _lastSaveName,
            OverwritePrompt = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            doc.SaveAs(picker.FileName);
            _lastSavedSha256 = Convert.ToHexString(SHA256.HashData(doc.PreparePatch().Data));
            _lastSaveName = Path.GetFileName(picker.FileName);
            UpdateStatus($"Saved patched resource: {picker.FileName} (with a .changes.txt report). The loaded original was untouched.");
            MessageBox.Show($"Patched resource written:\n{picker.FileName}\n\nChange report:\n{picker.FileName}.changes.txt\n\nInstall the patched file only after backing up your game's original resource.",
                "Saved patched copy", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The patched copy could not be saved:\n\n{ex.Message}", "Save failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (_document is not { } doc || !CommitEdits()) return;
        try
        {
            string report = doc.PreparePatch().Report;
            var picker = new SaveFileDialog
            {
                Title = "Export change list with hex offsets",
                Filter = "Plain text report (*.txt)|*.txt", AddExtension = true,
                DefaultExt = ".txt", FileName = "DriverSF_traffic_changes.txt"
            };
            if (picker.ShowDialog(this) != true) return;
            File.WriteAllText(picker.FileName, report, new UTF8Encoding(false));
            UpdateStatus($"Exported change report to {picker.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void VehicleSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_document is not { } doc) return;
        if (!TryVehicleId(VehicleSearchBox.Text, out uint id))
        {
            MessageBox.Show("Enter a numeric vehicle ID (e.g. 216 or 0xD8).");
            return;
        }
        var b = new StringBuilder();
        b.AppendLine($"Vehicle ID {id} / 0x{id:X4}");
        b.AppendLine("All offsets are hexadecimal FILE offsets in the loaded resource.");
        b.AppendLine(new string('─', 74));
        b.AppendLine($"SPOOLING REFERENCES (all {doc.Sets.Count} settings)");
        int spoolHits = 0;
        foreach (var set in doc.Sets)
        foreach (var group in set.Groups)
        foreach (var row in group.Rows)
        {
            if (row.VehicleId != id) continue;
            spoolHits++;
            b.AppendLine($"  {set.Name,-34} group {group.Index} / slot {row.Slot,-2}  ID at {row.OffsetHex}");
        }
        if (spoolHits == 0) b.AppendLine("  None in the edited spooling lists.");
        b.AppendLine();
        b.AppendLine($"NONZERO PROBABILITY WEIGHTS (all {doc.Profiles.Count} tables)");
        int weightHits = 0, tableRows = 0;
        foreach (var p in doc.Profiles)
        foreach (var row in p.Rows)
        {
            if (row.VehicleId != id) continue;
            tableRows++;
            if (row.Weight <= 0) continue;
            weightHits++;
            b.AppendLine($"  Table {p.Number,-3}  weight {row.Weight.ToString("0.########", CultureInfo.InvariantCulture),-10}  weight at {row.OffsetHex}  {p.Definition.Name}");
        }
        if (weightHits == 0) b.AppendLine("  No positive weights in the loaded tables.");
        b.AppendLine();
        b.AppendLine($"Found: {spoolHits} spool slot(s), {weightHits} positive weights, {tableRows} total table rows.");
        b.AppendLine($"All {doc.Profiles.Count} weight tables were searched.");
        VehicleResults.Text = b.ToString();
    }

    private void UpdateStatus(string? message = null)
    {
        if (StatusLabel is null) return;
        if (_document is null)
        {
            StatusLabel.Text = message ?? "Ready. Open a PC .fchunk to edit.";
            return;
        }
        string tally = $"Pending edits: {_document.SpoolEdits} spooling IDs  /  {_document.WeightEdits} probability row changes";
        StatusLabel.Text = message is null ? tally : $"{message}   |   {tally}";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (HasUnsavedChanges())
        {
            var result = MessageBox.Show("You still have changes relative to the loaded original. Exit without saving another copy?",
                "Confirm exit", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) e.Cancel = true;
        }
        base.OnClosing(e);
    }
}
