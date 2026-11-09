using System.Globalization;
using Eloverblik;
using Stromligning;

namespace StromForbrug.Windows;

/// <summary>
/// The settings dialog: Eloverblik token and metering point, and the companies at
/// stromligning.dk. Opens by itself at start when there are no settings yet. OK writes the
/// choices into <see cref="AppSettings"/> and saves them.
/// </summary>
public sealed class SettingsForm : Form
{
    private static readonly CultureInfo Da = CultureInfo.GetCultureInfo("da-DK");
    private static readonly Supplier NoSupplier = new() { Name = "(intet – vis kun kWh)" };
    private static readonly Company NoCompany = new() { Name = "(intet – kun spotpris uden tillæg)" };

    private readonly AppSettings _settings;
    private readonly EloverblikClient _eloverblik;
    private readonly StromligningClient _stromligning;

    private readonly TextBox _txtToken = new()
    {
        UseSystemPasswordChar = true,
        Dock = DockStyle.Fill,
        Margin = new Padding(3, 5, 3, 4),
        PlaceholderText = "Indsæt dit token fra eloverblik.dk"
    };
    private readonly CheckBox _chkShowToken = new() { Text = "Vis", AutoSize = true, Margin = new Padding(6, 7, 3, 3) };
    private readonly ComboBox _cmbMeteringPoint = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Margin = new Padding(3, 5, 3, 4) };
    private readonly Button _btnPoints = new() { Text = "Hent målepunkter", AutoSize = true, Margin = new Padding(6, 3, 3, 3) };
    private readonly ComboBox _cmbSupplier = NewCombo();
    private readonly ComboBox _cmbCustomerGroup = NewCombo();
    private readonly ComboBox _cmbCompany = NewCombo();
    private readonly ComboBox _cmbProduct = NewCombo();
    private readonly CheckBox _chkVat = new() { Text = "Vis kroner inkl. moms", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
    private readonly Label _lblStatus = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _btnOk = new() { Text = "Gem", Width = 100, Height = 30, DialogResult = DialogResult.None };
    private readonly Button _btnCancel = new() { Text = "Annuller", Width = 100, Height = 30, DialogResult = DialogResult.Cancel };

    public SettingsForm(AppSettings settings, EloverblikClient eloverblik, StromligningClient stromligning, bool firstRun)
    {
        _settings = settings;
        _eloverblik = eloverblik;
        _stromligning = stromligning;

        Text = "Indstillinger – StromForbrug";
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(720, firstRun ? 520 : 470);
        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        BuildLayout(firstRun);

        _txtToken.Text = settings.RefreshToken ?? "";
        _cmbMeteringPoint.Text = settings.MeteringPointId ?? "";
        _chkVat.Checked = settings.IncludeVat;

        _chkShowToken.CheckedChanged += (_, _) => _txtToken.UseSystemPasswordChar = !_chkShowToken.Checked;
        _btnPoints.Click += async (_, _) => await LoadMeteringPointsAsync();
        _cmbSupplier.SelectedIndexChanged += OnSupplierChanged;
        _cmbCompany.SelectedIndexChanged += OnCompanyChanged;
        _txtToken.TextChanged += (_, _) => UpdateOk();
        _cmbMeteringPoint.TextChanged += (_, _) => UpdateOk();
        _btnOk.Click += (_, _) => Save();
        Load += async (_, _) => await LoadCatalogAsync();
        UpdateOk();
    }

    // ---------------- layout ----------------

    private void BuildLayout(bool firstRun)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(14, 10, 14, 10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, firstRun ? 58 : 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 214));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        root.Controls.Add(new Label
        {
            Text = firstRun
                ? "Velkommen til StromForbrug. Se dit elforbrug time for time eller dag for dag – og hvad det har kostet " +
                  "med tariffer, afgifter og din elaftales tillæg. Indtast først dit token og målepunkt fra Eloverblik."
                : "Token og valg gemmes for din Windows-bruger; tokenet krypteres og sendes kun til Eloverblik.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = firstRun ? 54 : 26,
            ForeColor = Color.FromArgb(60, 60, 60)
        }, 0, 0);

        // ---- Eloverblik ----
        var elo = Grid(3);
        elo.ColumnStyles[2] = new ColumnStyle(SizeType.AutoSize);
        elo.Controls.Add(NewLabel("Token:"), 0, 0);
        elo.Controls.Add(_txtToken, 1, 0);
        elo.Controls.Add(_chkShowToken, 2, 0);
        elo.Controls.Add(NewLabel("Målepunkt:"), 0, 1);
        elo.Controls.Add(_cmbMeteringPoint, 1, 1);
        elo.Controls.Add(_btnPoints, 2, 1);
        var help = new LinkLabel
        {
            Text = "Log ind på eloverblik.dk med MitID, vælg Datadeling og Opret token. Et token gælder i op til et år.",
            AutoSize = true,
            Margin = new Padding(3, 2, 3, 3),
            LinkArea = new LinkArea(11, 14)
        };
        help.LinkClicked += (_, _) => OpenUrl("https://eloverblik.dk");
        elo.Controls.Add(help, 1, 2);
        elo.SetColumnSpan(help, 2);
        root.Controls.Add(Group("Eloverblik – dit forbrug", elo, 132), 0, 1);

        // ---- Strømligning ----
        var prices = Grid(2);
        prices.Controls.Add(NewLabel("Netselskab:"), 0, 0);
        prices.Controls.Add(_cmbSupplier, 1, 0);
        prices.Controls.Add(NewLabel("Kundegruppe:"), 0, 1);
        prices.Controls.Add(_cmbCustomerGroup, 1, 1);
        prices.Controls.Add(NewLabel("Elhandelsselskab:"), 0, 2);
        prices.Controls.Add(_cmbCompany, 1, 2);
        prices.Controls.Add(NewLabel("Produkt:"), 0, 3);
        prices.Controls.Add(_cmbProduct, 1, 3);
        prices.Controls.Add(_chkVat, 1, 4);
        root.Controls.Add(Group("Strømligning – priser i kroner", prices, 200), 0, 2);

        root.Controls.Add(_lblStatus, 0, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
        buttons.Controls.Add(_btnCancel);
        buttons.Controls.Add(_btnOk);
        root.Controls.Add(buttons, 0, 4);

        Controls.Add(root);

        static TableLayoutPanel Grid(int columns)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, Padding = new Padding(6, 2, 6, 2) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            if (columns > 2)
                t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        static GroupBox Group(string title, Control content, int height)
        {
            var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Height = height, Padding = new Padding(6), Margin = new Padding(0, 6, 0, 0) };
            box.Controls.Add(content);
            return box;
        }
    }

    private static Label NewLabel(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };

    private static ComboBox NewCombo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 4) };

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No browser registered; nothing useful to do.
        }
    }

    // ---------------- values ----------------

    internal static string? CleanToken(string text)
    {
        var token = text.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token[7..].Trim();
        return token.Length == 0 ? null : token;
    }

    /// <summary>The digits of the combo's text, so both a picked item ("5713… – address") and a typed id work.</summary>
    private string? MeteringPointId
    {
        get
        {
            if (_cmbMeteringPoint.SelectedItem is MeteringPoint p && _cmbMeteringPoint.Text == p.ToString())
                return p.MeteringPointId;
            var digits = new string(_cmbMeteringPoint.Text.TakeWhile(c => c != '–').Where(char.IsDigit).ToArray());
            return digits.Length == 0 ? null : digits;
        }
    }

    private void UpdateOk() =>
        _btnOk.Enabled = CleanToken(_txtToken.Text) is not null && MeteringPointId is not null;

    private void Save()
    {
        if (!_btnOk.Enabled)
            return;

        _settings.RefreshToken = CleanToken(_txtToken.Text);
        _settings.MeteringPointId = MeteringPointId;
        _settings.IncludeVat = _chkVat.Checked;

        // Without the catalog (stromligning.dk unreachable) the earlier price choices are kept.
        if (_cmbSupplier.Items.Count > 0)
        {
            var supplier = _cmbSupplier.SelectedItem as Supplier;
            _settings.SupplierId = supplier is null || supplier == NoSupplier ? null : supplier.Id;
            _settings.CustomerGroupId = (_cmbCustomerGroup.SelectedItem as CustomerGroup)?.Id;
            _settings.CompanyName = _cmbCompany.SelectedItem is Company c && c != NoCompany ? c.Name : null;
            _settings.ProductId = (_cmbProduct.SelectedItem as Product)?.Id;
        }

        _settings.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    // ---------------- loading ----------------

    private async Task LoadCatalogAsync()
    {
        _lblStatus.Text = "Henter selskaber fra stromligning.dk...";
        try
        {
            var suppliersTask = _stromligning.GetSuppliersAsync(CancellationToken.None);
            var companiesTask = _stromligning.GetCompaniesAsync(CancellationToken.None);
            await Task.WhenAll(suppliersTask, companiesTask);
            if (IsDisposed)
                return;
            var cmp = StringComparer.Create(Da, true);

            _cmbSupplier.Items.Add(NoSupplier);
            foreach (var s in suppliersTask.Result.OrderBy(s => s.Name, cmp))
                _cmbSupplier.Items.Add(s);
            _cmbCompany.Items.Add(NoCompany);
            foreach (var c in companiesTask.Result.Where(c => c.SpotProducts.Any()).OrderBy(c => c.Name, cmp))
                _cmbCompany.Items.Add(c);

            _cmbSupplier.SelectedIndex = 0;
            _cmbCompany.SelectedIndex = 0;
            SelectWhere<Supplier>(_cmbSupplier, s => s.Id == _settings.SupplierId);
            SelectWhere<CustomerGroup>(_cmbCustomerGroup, g => g.Id == _settings.CustomerGroupId);
            SelectWhere<Company>(_cmbCompany, c => c.Name == _settings.CompanyName);
            SelectWhere<Product>(_cmbProduct, p => p.Id == _settings.ProductId);
            _lblStatus.Text = "";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or StromligningApiException)
        {
            if (!IsDisposed)
                _lblStatus.Text = "Kunne ikke hente selskaberne fra stromligning.dk: " + ex.Message;
        }
    }

    private static void SelectWhere<T>(ComboBox combo, Func<T, bool> match)
    {
        var item = combo.Items.OfType<T>().FirstOrDefault(match);
        if (item is not null)
            combo.SelectedItem = item;
    }

    private void OnSupplierChanged(object? sender, EventArgs e)
    {
        _cmbCustomerGroup.Items.Clear();
        var hasSupplier = _cmbSupplier.SelectedItem is Supplier s && s != NoSupplier;
        if (hasSupplier)
        {
            var supplier = (Supplier)_cmbSupplier.SelectedItem!;
            foreach (var group in supplier.CustomerGroups)
                _cmbCustomerGroup.Items.Add(group);
            if (_cmbCustomerGroup.Items.Count > 0)
            {
                var i = supplier.CustomerGroups.FindIndex(g => g.Default);
                _cmbCustomerGroup.SelectedIndex = i >= 0 ? i : 0;
            }
        }
        _cmbCustomerGroup.Enabled = _cmbCompany.Enabled = _cmbProduct.Enabled = hasSupplier;
    }

    private void OnCompanyChanged(object? sender, EventArgs e)
    {
        _cmbProduct.Items.Clear();
        if (_cmbCompany.SelectedItem is Company company)
        {
            foreach (var product in company.SpotProducts)
                _cmbProduct.Items.Add(product);
            if (_cmbProduct.Items.Count > 0)
                _cmbProduct.SelectedIndex = 0;
        }
    }

    private async Task LoadMeteringPointsAsync()
    {
        var token = CleanToken(_txtToken.Text);
        if (token is null)
        {
            _lblStatus.Text = "Indsæt først dit token fra eloverblik.dk.";
            return;
        }

        _btnPoints.Enabled = false;
        UseWaitCursor = true;
        _lblStatus.Text = "Henter målepunkter fra Eloverblik...";
        try
        {
            _eloverblik.RefreshToken = token;
            var points = await _eloverblik.GetMeteringPointsAsync(CancellationToken.None);
            var current = MeteringPointId;
            _cmbMeteringPoint.Items.Clear();
            foreach (var p in points.OrderByDescending(p => p.IsConsumption).ThenBy(p => p.MeteringPointId))
                _cmbMeteringPoint.Items.Add(p);

            var pick = points.FirstOrDefault(p => p.MeteringPointId == current) ?? points.FirstOrDefault(p => p.IsConsumption);
            if (pick is not null)
                _cmbMeteringPoint.SelectedItem = pick;
            _lblStatus.Text = points.Count == 0
                ? "Tokenet giver ikke adgang til nogen målepunkter."
                : $"{points.Count} målepunkt(er) hentet.";
        }
        catch (EloverblikApiException ex) when (ex.IsUnauthorized)
        {
            _lblStatus.Text = "Eloverblik afviste tokenet. Tjek at det er kopieret helt, og at det ikke er udløbet.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or EloverblikApiException)
        {
            _lblStatus.Text = "Kunne ikke hente målepunkter: " + ex.Message;
        }
        finally
        {
            _btnPoints.Enabled = true;
            UseWaitCursor = false;
            UpdateOk();
        }
    }
}
