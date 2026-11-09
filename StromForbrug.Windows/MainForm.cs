using System.Globalization;
using System.Text;
using Eloverblik;
using Stromligning;
using Velopack;
using Velopack.Sources;

namespace StromForbrug.Windows;

public sealed class MainForm : Form
{
    private static readonly CultureInfo Da = CultureInfo.GetCultureInfo("da-DK");
    private const string UserAgent = "StromForbrug/1.0";

    // Same palette as the web app: kWh in green, the price components like ElForecast.
    private static readonly Color KwhColor = Color.FromArgb(27, 175, 122);
    private static readonly Color[] CostColors =
    [
        Color.FromArgb(42, 120, 214), Color.FromArgb(235, 104, 52), Color.FromArgb(27, 175, 122),
        Color.FromArgb(237, 161, 0), Color.FromArgb(232, 123, 164), Color.FromArgb(0, 131, 0)
    ];

    private readonly StromligningClient _stromligning = new(userAgent: UserAgent);
    private readonly EloverblikClient _eloverblik = new(userAgent: UserAgent);
    private readonly ConsumptionService _service;
    private readonly AppSettings _settings = AppSettings.Load();
    private CancellationTokenSource? _inFlight;
    private ConsumptionResult? _fetched;
    private ConsumptionResult? _result;
    private bool _restoring;

    // ---- settings (edited in SettingsForm) ----
    private readonly Label _lblSetup = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(82, 81, 78)
    };
    private readonly Button _btnSettings = new() { Text = "⚙ Indstillinger...", Width = 140, Height = 30, Dock = DockStyle.Right };
    private readonly Button _btnUpdate = new() { Text = "Søg efter opdatering", Width = 160, Height = 30, Dock = DockStyle.Right };
    private const string RepoUrl = "https://github.com/kreaton/StromForbrug";
    private readonly UpdateManager _updates = new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    private string? _supplierName;

    // ---- period ----
    private readonly RadioButton _rbHour = new() { Text = "Time for time", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
    private readonly RadioButton _rbDay = new() { Text = "Dag for dag", AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
    private readonly DateTimePicker _dtFrom = NewDatePicker();
    private readonly DateTimePicker _dtTo = NewDatePicker();
    private readonly Button _btnWeek = new() { Text = "Seneste 7 dage", AutoSize = true };
    private readonly Button _btnMonth = new() { Text = "Seneste 30 dage", AutoSize = true };
    private readonly CheckBox _chkVat = new() { Text = "Kroner inkl. moms", AutoSize = true, Margin = new Padding(12, 7, 3, 3) };
    private readonly Button _btnFetch = new() { Text = "Hent forbrug", Width = 120, Height = 30 };
    private readonly Button _btnSave = new() { Text = "Gem som CSV...", Width = 120, Height = 30, Enabled = false };

    // ---- results ----
    private readonly Label _lblKwh = NewTileValue();
    private readonly Label _lblKwhSub = NewTileSub();
    private readonly Label _lblCost = NewTileValue();
    private readonly Label _lblCostSub = NewTileSub();
    private readonly Label _lblAvg = NewTileValue();
    private readonly Label _lblAvgSub = NewTileSub();
    private readonly Label _lblPeak = NewTileValue();
    private readonly Label _lblPeakSub = NewTileSub();
    private readonly Label _lblPeakCaption = NewTileCaption("Højeste time");

    private readonly BarChartControl _kwhChart = new() { Dock = DockStyle.Fill, Unit = "kWh", MutedLabel = "Estimeret", EmptyText = "Tryk \"Hent forbrug\"." };
    private readonly BarChartControl _costChart = new() { Dock = DockStyle.Fill, MutedLabel = "Mangler pris", EmptyText = "Ingen priser – vælg netselskab." };
    private readonly DataGridView _grid = NewGrid();
    private readonly ToolStripStatusLabel _status = new() { Text = "Klar.", Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    public MainForm()
    {
        var version = typeof(MainForm).Assembly.GetName().Version;
        Text = $"StromForbrug {version?.ToString(3)} – dit elforbrug i kWh og kroner";
        Width = 1280;
        Height = 900;
        MinimumSize = new Size(1040, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // Cosmetic only.
        }

        _service = new ConsumptionService(_eloverblik, _stromligning);
        _eloverblik.AccessTokenIssued += token => _settings.AccessToken = token;

        _kwhChart.Series = [("Forbrug", KwhColor)];
        _costChart.Series = Cost.Components.Select((c, i) => (c.Label, CostColors[i])).ToArray();
        _kwhChart.TooltipText = KwhTooltip;
        _costChart.TooltipText = CostTooltip;

        BuildLayout();
        RestoreSimpleSettings();

        _btnSettings.Click += async (_, _) => await OpenSettingsAsync(firstRun: false);
        _btnUpdate.Click += async (_, _) => await CheckForUpdateAsync();
        _rbHour.CheckedChanged += (_, _) => OnResolutionChanged();
        _btnWeek.Click += async (_, _) => { SetRange(7); await LoadAsync(); };
        _btnMonth.Click += async (_, _) => { SetRange(30); await LoadAsync(); };
        _dtFrom.CloseUp += async (_, _) => { ClampRange(fromChanged: true); await LoadAsync(); };
        _dtTo.CloseUp += async (_, _) => { ClampRange(fromChanged: false); await LoadAsync(); };
        _chkVat.CheckedChanged += (_, _) => Render();
        _btnFetch.Click += async (_, _) => { _fetched = null; await LoadAsync(); };
        _btnSave.Click += async (_, _) => await SaveCsvAsync();
        // Shown first, so the settings dialog (when there are no settings yet) opens on top of it.
        Shown += async (_, _) => await OnShownAsync();
        FormClosing += (_, _) => SaveSettings();
    }

    // ---------------- layout ----------------

    private void BuildLayout()
    {
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
        tabs.TabPages.Add(Page("Forbrug (kWh)", _kwhChart));
        tabs.TabPages.Add(Page("Pris (kr)", _costChart));
        tabs.TabPages.Add(Page("Tabel", _grid));
        var tabHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
        tabHost.Controls.Add(tabs);

        // Fill first: WinForms docks the last-added control first.
        Controls.Add(tabHost);
        Controls.Add(BuildTiles());
        Controls.Add(BuildPeriodBox());
        Controls.Add(BuildSetupBar());
        Controls.Add(statusStrip);

        static TabPage Page(string title, Control content)
        {
            var page = new TabPage(title) { Padding = new Padding(6), BackColor = Color.White };
            page.Controls.Add(content);
            return page;
        }
    }

    /// <summary>One line saying what is shown (metering point, companies) and the settings button.</summary>
    private Panel BuildSetupBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(14, 6, 14, 4) };
        bar.Controls.Add(_lblSetup);
        bar.Controls.Add(_btnUpdate);
        bar.Controls.Add(new Panel { Width = 8, Dock = DockStyle.Right });
        bar.Controls.Add(_btnSettings);
        return bar;
    }

    /// <summary>Looks for a newer GitHub release and installs it (installed copies only).</summary>
    private async Task CheckForUpdateAsync()
    {
        if (!_updates.IsInstalled)
        {
            MessageBox.Show(this, "Opdateringer virker kun i den installerede udgave (Setup.exe fra GitHub).",
                "Opdatering", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _btnUpdate.Enabled = false;
        try
        {
            var update = await _updates.CheckForUpdatesAsync();
            if (update is null)
            {
                MessageBox.Show(this, "Du har den nyeste udgave.", "Opdatering", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var answer = MessageBox.Show(this,
                $"Version {update.TargetFullRelease.Version} er klar. Hent og installér den nu? StromForbrug genstarter bagefter.",
                "Opdatering", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;

            await _updates.DownloadUpdatesAsync(update);
            _updates.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            ShowError("Opdateringen mislykkedes.", ex);
        }
        finally
        {
            _btnUpdate.Enabled = true;
        }
    }

    private GroupBox BuildPeriodBox()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(6, 0, 6, 0) };
        flow.Controls.Add(_rbHour);
        flow.Controls.Add(_rbDay);
        flow.Controls.Add(NewLabel("Fra:"));
        flow.Controls.Add(_dtFrom);
        flow.Controls.Add(NewLabel("Til:"));
        flow.Controls.Add(_dtTo);
        flow.Controls.Add(_btnWeek);
        flow.Controls.Add(_btnMonth);
        flow.Controls.Add(_chkVat);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, Width = 260, WrapContents = false };
        buttons.Controls.Add(_btnFetch);
        buttons.Controls.Add(_btnSave);

        var box = new GroupBox { Text = "Periode (højst 30 dage)", Dock = DockStyle.Top, Height = 64, Padding = new Padding(6, 2, 6, 2) };
        box.Controls.Add(flow);
        box.Controls.Add(buttons);
        return box;
    }

    private Control BuildTiles()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, RowCount = 1, Padding = new Padding(8, 6, 8, 2) };
        for (var i = 0; i < 4; i++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Panel[] tiles =
        [
            Tile(NewTileCaption("Forbrug i alt"), _lblKwh, _lblKwhSub),
            Tile(NewTileCaption("Pris i alt"), _lblCost, _lblCostSub),
            Tile(NewTileCaption("Gennemsnitspris"), _lblAvg, _lblAvgSub),
            Tile(_lblPeakCaption, _lblPeak, _lblPeakSub)
        ];
        for (var i = 0; i < tiles.Length; i++)
            table.Controls.Add(tiles[i], i, 0);

        // Size the lines from their fonts rather than fixed pixels: with Windows display scaling
        // the text grows, and fixed heights let the tiles' lower part slide under the tabs.
        var tile = tiles[0];
        var inner = tile.Controls.Cast<Control>().Sum(c => c.Height);
        var tileHeight = inner + tile.Padding.Vertical + 2 /* border */;
        table.Height = tileHeight + tile.Margin.Vertical + table.Padding.Vertical;
        return table;

        static Panel Tile(Label caption, Label value, Label sub)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(10, 4, 6, 4), Margin = new Padding(4) };
            foreach (var label in new[] { caption, value, sub })
                label.Height = TextRenderer.MeasureText("Åg", label.Font).Height + 2;
            // Dock=Top stacks in reverse order of addition.
            panel.Controls.Add(sub);
            panel.Controls.Add(value);
            panel.Controls.Add(caption);
            return panel;
        }
    }

    private static Label NewTileCaption(string text) => new() { Text = text, Dock = DockStyle.Top, Height = 18, ForeColor = Color.FromArgb(82, 81, 78), AutoSize = false };
    private static Label NewTileValue() => new() { Text = "–", Dock = DockStyle.Top, Height = 32, Font = new Font("Segoe UI", 16F, FontStyle.Bold), AutoSize = false };
    private static Label NewTileSub() => new() { Text = "", Dock = DockStyle.Top, Height = 18, ForeColor = Color.Gray, AutoSize = false };

    private static Label NewLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 8, 3, 3)
    };

    private static DateTimePicker NewDatePicker() => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "ddd dd-MM-yyyy",
        Width = 130,
        Margin = new Padding(3, 4, 8, 3)
    };

    private static DataGridView NewGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None,
        EnableHeadersVisualStyles = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowTemplate = { Height = 22 },
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        ColumnHeadersHeight = 28,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(238, 240, 243),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleRight
        }
    };

    // ---------------- settings ----------------

    private void RestoreSimpleSettings()
    {
        _eloverblik.AccessToken = _settings.AccessToken;
        _chkVat.Checked = _settings.IncludeVat;

        _restoring = true;
        (_settings.Resolution == Resolution.Day ? _rbDay : _rbHour).Checked = true;
        _restoring = false;
        SetRange(_settings.Resolution == Resolution.Day ? 30 : 7);
        UpdateSetupText();
    }

    private void SaveSettings()
    {
        _settings.AccessToken = _eloverblik.AccessToken;
        _settings.IncludeVat = _chkVat.Checked;
        _settings.Resolution = Resolution;
        _settings.Save();
    }

    private bool HasSettings =>
        !string.IsNullOrWhiteSpace(_settings.RefreshToken) && !string.IsNullOrWhiteSpace(_settings.MeteringPointId);

    private Resolution Resolution => _rbDay.Checked ? Resolution.Day : Resolution.Hour;

    private PriceQuery? CurrentPriceQuery =>
        string.IsNullOrWhiteSpace(_settings.SupplierId)
            ? null
            : new PriceQuery(_settings.SupplierId, _settings.CustomerGroupId, _settings.ProductId);

    /// <summary>First start: open the settings by themselves; otherwise fetch straight away.</summary>
    private async Task OnShownAsync()
    {
        if (!HasSettings)
        {
            await OpenSettingsAsync(firstRun: true);
            return;
        }

        await LoadAsync();
        await LoadSupplierNameAsync();
    }

    private async Task OpenSettingsAsync(bool firstRun)
    {
        var before = (_settings.RefreshToken, _settings.MeteringPointId, CurrentPriceQuery);

        using (var dialog = new SettingsForm(_settings, _eloverblik, _stromligning, firstRun))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                if (!HasSettings)
                    _status.Text = "Indtast token og målepunkt fra Eloverblik under Indstillinger for at se dit forbrug.";
                return;
            }
        }

        _chkVat.Checked = _settings.IncludeVat;
        if (before != (_settings.RefreshToken, _settings.MeteringPointId, CurrentPriceQuery))
            _fetched = null;
        _supplierName = null;
        UpdateSetupText();
        await LoadAsync();
        await LoadSupplierNameAsync();
    }

    /// <summary>The grid company's name for the line at the top; only cosmetic, so failures are ignored.</summary>
    private async Task LoadSupplierNameAsync()
    {
        if (CurrentPriceQuery is not { } q || _supplierName is not null)
            return;
        try
        {
            _supplierName = (await _stromligning.GetSuppliersAsync(CancellationToken.None)).FirstOrDefault(s => s.Id == q.SupplierId)?.Name;
            UpdateSetupText();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or StromligningApiException)
        {
        }
    }

    private void UpdateSetupText()
    {
        if (!HasSettings)
        {
            _lblSetup.Text = "Ingen indstillinger endnu – tryk \"Indstillinger...\" og indtast dit Eloverblik-token og målepunkt.";
            return;
        }

        var prices = CurrentPriceQuery is null
            ? "uden priser"
            : $"{_supplierName ?? _settings.SupplierId}{(_result?.PriceArea is { } a ? $" ({a})" : "")} · {_settings.CompanyName ?? "kun spotpris uden tillæg"}";
        _lblSetup.Text = $"Målepunkt {_settings.MeteringPointId}  ·  {prices}";
    }

    // ---------------- period ----------------

    private void OnResolutionChanged()
    {
        if (_restoring)
            return;
        SetRange(_rbDay.Checked ? 30 : 7);
        _ = LoadAsync();
    }

    /// <summary>The last <paramref name="days"/> days, ending yesterday (Eloverblik has nothing newer).</summary>
    private void SetRange(int days)
    {
        var to = DanishTime.Today.AddDays(-1);
        _dtTo.MaxDate = _dtFrom.MaxDate = DanishTime.Today.ToDateTime(TimeOnly.MinValue);
        _dtTo.Value = to.ToDateTime(TimeOnly.MinValue);
        _dtFrom.Value = to.AddDays(-(days - 1)).ToDateTime(TimeOnly.MinValue);
    }

    private void ClampRange(bool fromChanged)
    {
        var from = DateOnly.FromDateTime(_dtFrom.Value);
        var to = DateOnly.FromDateTime(_dtTo.Value);
        var max = ConsumptionService.MaxDays;
        if (fromChanged)
        {
            if (to < from) to = from;
            if (to.DayNumber - from.DayNumber >= max) to = from.AddDays(max - 1);
            if (to > DanishTime.Today) to = DanishTime.Today;
            _dtTo.Value = to.ToDateTime(TimeOnly.MinValue);
        }
        else
        {
            if (from > to) from = to;
            if (to.DayNumber - from.DayNumber >= max) from = to.AddDays(-(max - 1));
            _dtFrom.Value = from.ToDateTime(TimeOnly.MinValue);
        }
    }

    // ---------------- fetching ----------------

    private async Task LoadAsync()
    {
        if (!HasSettings)
        {
            _status.Text = "Indtast token og målepunkt fra Eloverblik under Indstillinger.";
            return;
        }

        var meteringPoint = _settings.MeteringPointId!;
        SaveSettings();
        _eloverblik.RefreshToken = _settings.RefreshToken;

        var from = DateOnly.FromDateTime(_dtFrom.Value);
        var to = DateOnly.FromDateTime(_dtTo.Value);
        var query = new ConsumptionQuery(meteringPoint, from, to, CurrentPriceQuery);

        // Switching view or narrowing the period reuses what was fetched.
        if (_fetched?.Covers(query) == true)
        {
            _result = _fetched.Slice(from, to);
            Render();
            UpdateSetupText();
            return;
        }

        _inFlight?.Cancel();
        _inFlight?.Dispose();
        _inFlight = new CancellationTokenSource();
        var ct = _inFlight.Token;

        SetBusy(true, "Henter forbrug fra Eloverblik... det kan tage lidt tid.");
        try
        {
            // A whole month ending at "Til" costs Eloverblik no more than a week, and makes
            // switching between hour and day instant.
            var fetchFrom = from < to.AddDays(-(ConsumptionService.MaxDays - 1)) ? from : to.AddDays(-(ConsumptionService.MaxDays - 1));
            var result = await _service.GetAsync(query with { From = fetchFrom }, ct);
            if (ct.IsCancellationRequested)
                return;

            _fetched = result;
            _result = result.Slice(from, to);
            Render();
            UpdateSetupText();
            if (result.PriceError is { } pe)
                _status.Text += "  Priserne kunne ikke hentes: " + pe.Message;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Superseded by a newer request.
        }
        catch (EloverblikApiException ex) when (ex.IsUnauthorized)
        {
            ShowError("Eloverblik afviste tokenet. Det kan være udløbet eller slettet – opret et nyt på eloverblik.dk.", ex);
        }
        catch (EloverblikApiException ex) when (ex.IsBusy)
        {
            ShowError("Eloverblik har travlt eller begrænser antallet af opslag lige nu. Prøv igen om et par minutter.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or EloverblikApiException or ArgumentException)
        {
            ShowError("Kunne ikke hente forbruget.", ex);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                SetBusy(false, null);
        }
    }

    // ---------------- rendering ----------------

    private double Pick(Money m) => _chkVat.Checked ? m.Total : m.ExVat;
    private string VatText => _chkVat.Checked ? "inkl. moms" : "ekskl. moms";
    private bool Daily => Resolution == Resolution.Day;

    private IReadOnlyList<ConsumptionRow> Rows => _result?.Rows(Resolution) ?? [];

    private void Render()
    {
        var result = _result;
        if (result is null)
            return;

        var rows = Rows;
        _kwhChart.Daily = _costChart.Daily = Daily;
        _costChart.Unit = "kr " + VatText;
        _kwhChart.Bars = rows.Select(r => new ChartBar(r.Start, [r.Kwh], r.MostlyEstimated)).ToList();
        _costChart.Bars = result.TotalCost is null
            ? []
            : rows.Select(r => new ChartBar(r.Start,
                r.Cost is { } c ? Cost.Components.Select(p => Pick(p.Get(c))).ToArray() : new double[Cost.Components.Length],
                r.HoursWithoutPrice > 0)).ToList();

        _lblKwh.Text = $"{result.TotalKwh.ToString("N1", Da)} kWh";
        _lblKwhSub.Text = $"{result.Daily.Count} dage · gns. {(result.TotalKwh / Math.Max(1, result.Hourly.Count) * 24).ToString("N1", Da)} kWh/døgn";

        if (result.TotalCost is { } total)
        {
            _lblCost.Text = $"{Pick(total.Total).ToString("N2", Da)} kr";
            _lblCostSub.Text = VatText + (result.HoursWithoutPrice > 0 ? $" · {result.HoursWithoutPrice} timer uden pris" : "");
            _lblAvg.Text = result.AveragePrice is { } avg ? $"{Pick(avg).ToString("N2", Da)} kr/kWh" : "–";
            _lblAvgSub.Text = "vægtet efter dit forbrug";
        }
        else
        {
            _lblCost.Text = _lblAvg.Text = "–";
            _lblCostSub.Text = CurrentPriceQuery is null ? "vælg netselskab for priser" : "ingen priser";
            _lblAvgSub.Text = "";
        }

        _lblPeakCaption.Text = Daily ? "Højeste dag" : "Højeste time";
        if (rows.Count > 0)
        {
            var peak = rows.MaxBy(r => r.Kwh)!;
            _lblPeak.Text = $"{peak.Kwh.ToString("N2", Da)} kWh";
            _lblPeakSub.Text = Daily ? peak.Start.ToString("dddd d. MMMM", Da) : peak.Start.ToString("ddd d. MMM 'kl.' HH':'mm", Da);
        }
        else
        {
            _lblPeak.Text = "–";
            _lblPeakSub.Text = "";
        }

        FillGrid(rows);

        _status.Text = rows.Count == 0
            ? "Eloverblik har ingen målinger for perioden endnu. Data kommer normalt dagen efter."
            : $"{result.Hourly.Count} timer{(result.EstimatedHours > 0 ? $" ({result.EstimatedHours} estimerede)" : "")}" +
              $"{(result.PriceArea is { } a ? $", prisområde {a}" : "")} – data til {result.DataUntil!.Value.ToString("ddd d. MMM HH':'mm", Da)}" +
              $" – opdateret {DateTime.Now.ToString("HH':'mm':'ss", Da)}.";
        _btnSave.Enabled = rows.Count > 0;
    }

    private void FillGrid(IReadOnlyList<ConsumptionRow> rows)
    {
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        _grid.Columns.Clear();
        AddColumn(Daily ? "Dag" : "Tidsrum", 160, DataGridViewContentAlignment.MiddleLeft);
        AddColumn("kWh", 80);
        AddColumn("kr/kWh", 80);
        foreach (var part in Cost.Components)
            AddColumn(part.Label, 90);
        AddColumn("I alt kr", 95);

        foreach (var row in rows)
        {
            var c = row.Cost;
            var values = new List<object>
            {
                Daily ? row.Start.ToString("ddd dd'/'MM", Da) : $"{row.Start.ToString("ddd dd'/'MM HH':'mm", Da)}–{row.End.ToString("HH':'mm", Da)}",
                row.Kwh.ToString("N3", Da),
                row.AveragePrice is { } p ? Pick(p).ToString("N3", Da) : "–"
            };
            values.AddRange(Cost.Components.Select(part => (object)(c is { } cc ? Pick(part.Get(cc)).ToString("N2", Da) : "–")));
            values.Add(c is { } t ? Pick(t.Total).ToString("N2", Da) : "–");

            var index = _grid.Rows.Add(values.ToArray());
            if (row.IsEstimated || row.HoursWithoutPrice > 0)
                _grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(110, 110, 120);
        }

        if (_result is { } r && rows.Count > 0)
        {
            var sum = r.TotalCost;
            var totals = new List<object> { "I alt", r.TotalKwh.ToString("N3", Da), r.AveragePrice is { } a ? Pick(a).ToString("N3", Da) : "–" };
            totals.AddRange(Cost.Components.Select(part => (object)(sum is { } s ? Pick(part.Get(s)).ToString("N2", Da) : "–")));
            totals.Add(sum is { } st ? Pick(st.Total).ToString("N2", Da) : "–");
            var i = _grid.Rows.Add(totals.ToArray());
            _grid.Rows[i].DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
            _grid.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
        }
        _grid.ResumeLayout();

        void AddColumn(string header, int width, DataGridViewContentAlignment align = DataGridViewContentAlignment.MiddleRight)
        {
            var column = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = width,
                MinimumWidth = Math.Min(width, 60),
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = align }
            };
            column.HeaderCell.Style.Alignment = align;
            _grid.Columns.Add(column);
        }
    }

    private string Head(ConsumptionRow row) =>
        Daily
            ? row.Start.ToString("dddd d. MMMM", Da) + $"  ({row.Hours} timer)"
            : row.Start.ToString("dddd d. MMMM", Da) + $"  {row.Start.ToString("HH':'mm", Da)}–{row.End.ToString("HH':'mm", Da)}";

    private string KwhTooltip(int index)
    {
        var rows = Rows;
        if (index >= rows.Count)
            return "";
        var row = rows[index];
        var sb = new StringBuilder();
        sb.AppendLine(Head(row));
        sb.AppendLine($"Forbrug: {row.Kwh.ToString("N3", Da)} kWh");
        if (row.AveragePrice is { } p)
            sb.AppendLine($"Gns. pris: {Pick(p).ToString("N3", Da)} kr/kWh");
        if (row.Cost is { } c)
            sb.AppendLine($"Pris: {Pick(c.Total).ToString("N2", Da)} kr {VatText}");
        if (row.IsEstimated)
            sb.AppendLine(Daily ? $"{row.EstimatedHours} af {row.Hours} timer er estimerede." : "Estimeret måling.");
        return sb.ToString().TrimEnd();
    }

    private string CostTooltip(int index)
    {
        var rows = Rows;
        if (index >= rows.Count)
            return "";
        var row = rows[index];
        var sb = new StringBuilder();
        sb.AppendLine(Head(row));
        if (row.Cost is not { } c)
            return sb.Append("Ingen pris for dette tidsrum.").ToString();

        for (var p = Cost.Components.Length - 1; p >= 0; p--)
            sb.AppendLine($"{Cost.Components[p].Label,-14}{Pick(Cost.Components[p].Get(c)).ToString("N2", Da),10} kr");
        sb.AppendLine($"I alt ekskl. moms: {c.Total.ExVat.ToString("N2", Da)} kr");
        sb.AppendLine($"Moms: {c.Total.Vat.ToString("N2", Da)} kr");
        sb.AppendLine($"I alt inkl. moms: {c.Total.Total.ToString("N2", Da)} kr");
        sb.Append($"{row.Kwh.ToString("N3", Da)} kWh à {(row.AveragePrice is { } ap ? Pick(ap).ToString("N3", Da) : "–")} kr/kWh");
        if (row.HoursWithoutPrice > 0)
            sb.Append($" · {row.HoursWithoutPrice} timer mangler pris");
        return sb.ToString();
    }

    // ---------------- export ----------------

    private async Task SaveCsvAsync()
    {
        if (_result is null || Rows.Count == 0)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = "Gem forbrug",
            Filter = "CSV-fil (*.csv)|*.csv",
            FileName = $"stromforbrug-{_result.Query.From:yyyy-MM-dd}-{_result.Query.To:yyyy-MM-dd}-{(Daily ? "dag" : "time")}",
            InitialDirectory = _settings.ExportFolder ?? "",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            // da-DK so the CSV opens straight into columns in a Danish Excel.
            await ConsumptionExporter.SaveCsvAsync(Rows, dialog.FileName, Da);
            _settings.ExportFolder = Path.GetDirectoryName(dialog.FileName);
            _status.Text = $"Gemt: {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Kunne ikke gemme filen.", ex);
        }
    }

    // ---------------- helpers ----------------

    private void SetBusy(bool busy, string? message)
    {
        UseWaitCursor = busy;
        _btnFetch.Enabled = !busy;
        _btnSettings.Enabled = !busy;
        _btnSave.Enabled = !busy && Rows.Count > 0;
        if (message is not null)
            _status.Text = message;
    }

    private void ShowError(string context, Exception ex)
    {
        _status.Text = context;
        MessageBox.Show(this, $"{context}{Environment.NewLine}{Environment.NewLine}{ex.Message}",
            "Fejl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inFlight?.Cancel();
            _inFlight?.Dispose();
            _stromligning.Dispose();
            _eloverblik.Dispose();
        }
        base.Dispose(disposing);
    }
}
