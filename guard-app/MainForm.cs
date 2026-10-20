using System.Globalization;
using System.Text.Json;

namespace BcpGuardApp;

/// <summary>Guard station: scan QR, record entry/exit, watch the slots.</summary>
public class MainForm : Form
{
    private readonly ApiClient _api;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };

    private readonly TextBox txtQr = new();
    private readonly Button btnEntry = new() { Text = "VEHICLE ENTRY" };
    private readonly Button btnExit = new() { Text = "VEHICLE EXIT" };
    private readonly Button btnRefresh = new() { Text = "Refresh" };
    private readonly Button btnLogout = new() { Text = "Logout" };
    private readonly Label lblStatus = new();
    private readonly ListBox lstActivity = new();
    private readonly Label lblSummary = new();
    private readonly FlowLayoutPanel slotsFlow = new();

    private bool _refreshing;
    private bool _ending;

    /// <summary>True when the guard clicked Logout (or the session expired) - go back to the login screen.</summary>
    public bool LoggedOut { get; private set; }

    public MainForm(ApiClient api)
    {
        _api = api;
        BuildUi();

        Load += async (_, _) => await RefreshSlotsAsync();
        _timer.Tick += async (_, _) => await RefreshSlotsAsync(silent: true);
        _timer.Start();
        FormClosed += (_, _) => _timer.Stop();
    }

    // =====================================================================
    // UI
    // =====================================================================
    private void BuildUi()
    {
        Text = "BCP Gate System - Guard Station";
        ClientSize = new Size(1040, 660);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10f);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        // ---------- header ----------
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = Theme.Panel, Padding = new Padding(16, 0, 10, 0)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var who = string.IsNullOrWhiteSpace(_api.Name) ? _api.Username : _api.Name;
        header.Controls.Add(new Label
        {
            Text = $"BCP Gate System   |   {who} ({_api.Role})",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold)
        }, 0, 0);

        Theme.StyleButton(btnRefresh, Theme.Panel, Theme.Text);
        btnRefresh.FlatAppearance.BorderSize = 1;
        btnRefresh.FlatAppearance.BorderColor = Theme.Muted;
        btnRefresh.Size = new Size(96, 34);
        btnRefresh.Anchor = AnchorStyles.None;
        btnRefresh.Margin = new Padding(6);
        btnRefresh.Click += async (_, _) => await RefreshSlotsAsync();
        header.Controls.Add(btnRefresh, 1, 0);

        Theme.StyleButton(btnLogout, Theme.Red, Color.White);
        btnLogout.Size = new Size(96, 34);
        btnLogout.Anchor = AnchorStyles.None;
        btnLogout.Margin = new Padding(6);
        btnLogout.Click += (_, _) => { LoggedOut = true; Close(); };
        header.Controls.Add(btnLogout, 2, 0);

        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);

        // ---------- left: scan + actions ----------
        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(16, 16, 8, 8)
        };

        left.Controls.Add(new Label
        {
            Text = "Scan or type QR code / plate number",
            ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(3, 0, 3, 6)
        });

        Theme.StyleField(txtQr);
        txtQr.Font = new Font("Segoe UI", 14f);
        txtQr.Width = 316;
        txtQr.Margin = new Padding(3, 0, 3, 14);
        // Scanners usually type the code and press Enter - swallow it so it doesn't beep
        txtQr.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) e.SuppressKeyPress = true;
        };
        left.Controls.Add(txtQr);

        Theme.StyleButton(btnEntry, Theme.Green, Color.White);
        btnEntry.Size = new Size(316, 52);
        btnEntry.Margin = new Padding(3, 0, 3, 10);
        btnEntry.Click += OnEntry;
        left.Controls.Add(btnEntry);

        Theme.StyleButton(btnExit, Theme.Accent, Color.Black);
        btnExit.Size = new Size(316, 52);
        btnExit.Margin = new Padding(3, 0, 3, 12);
        btnExit.Click += OnExit;
        left.Controls.Add(btnExit);

        lblStatus.Size = new Size(316, 84);
        lblStatus.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        lblStatus.ForeColor = Theme.Muted;
        lblStatus.Text = "Ready. Scan a QR code, then press Entry or Exit.";
        left.Controls.Add(lblStatus);

        left.Controls.Add(new Label
        {
            Text = "Activity (this session)", ForeColor = Theme.Muted,
            AutoSize = true, Margin = new Padding(3, 4, 3, 4)
        });

        lstActivity.Size = new Size(316, 190);
        lstActivity.BackColor = Theme.Field;
        lstActivity.ForeColor = Theme.Text;
        lstActivity.BorderStyle = BorderStyle.None;
        lstActivity.IntegralHeight = false;
        left.Controls.Add(lstActivity);

        root.Controls.Add(left, 0, 1);

        // ---------- right: slot grid ----------
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 12, 12, 8) };

        slotsFlow.Dock = DockStyle.Fill;
        slotsFlow.FlowDirection = FlowDirection.TopDown;
        slotsFlow.WrapContents = false;
        slotsFlow.AutoScroll = true;

        lblSummary.Dock = DockStyle.Top;
        lblSummary.Height = 40;
        lblSummary.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        lblSummary.TextAlign = ContentAlignment.MiddleLeft;
        lblSummary.Text = "Loading slots...";

        // Fill control first, then the Top one (WinForms docking order)
        right.Controls.Add(slotsFlow);
        right.Controls.Add(lblSummary);
        root.Controls.Add(right, 1, 1);
    }

    // =====================================================================
    // Slots
    // =====================================================================
    private async Task RefreshSlotsAsync(bool silent = false)
    {
        if (_refreshing || _ending) return;
        _refreshing = true;
        try
        {
            var slots = await _api.GetSlotsAsync();
            RenderSlots(slots);
        }
        catch (ApiException ex)
        {
            if (!silent) ShowStatus(ex.Message, error: true);
            else lblSummary.Text = "Cannot reach the server - retrying...";
            HandleAuthError(ex);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RenderSlots(JsonElement slots)
    {
        if (slots.ValueKind != JsonValueKind.Array) return;

        slotsFlow.SuspendLayout();
        foreach (var c in slotsFlow.Controls.Cast<Control>().ToList()) c.Dispose();

        int vacant = 0, occupied = 0;
        var rowWidth = Math.Max(320, slotsFlow.ClientSize.Width - 30);

        foreach (var zone in slots.EnumerateArray().GroupBy(s => J.Str(s, "zone")).OrderBy(g => g.Key))
        {
            slotsFlow.Controls.Add(new Label
            {
                Text = $"Zone {zone.Key}", AutoSize = true, ForeColor = Theme.Muted,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Margin = new Padding(3, 8, 3, 4)
            });

            var row = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true, MaximumSize = new Size(rowWidth, 0), Margin = new Padding(0)
            };

            foreach (var slot in zone)
            {
                var isVacant = J.Str(slot, "status") == "vacant";
                if (isVacant) vacant++; else occupied++;

                row.Controls.Add(new Label
                {
                    Text = $"{J.Str(slot, "id")}\n{(isVacant ? "Vacant" : J.Str(slot, "plate"))}",
                    Size = new Size(96, 62), Margin = new Padding(4),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = isVacant ? Theme.Green : Theme.Red,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold)
                });
            }

            slotsFlow.Controls.Add(row);
        }

        slotsFlow.ResumeLayout();
        lblSummary.Text = $"Available: {vacant}     Occupied: {occupied}";
    }

    // =====================================================================
    // Entry
    // =====================================================================
    private async void OnEntry(object? sender, EventArgs e)
    {
        var qr = txtQr.Text.Trim();
        if (qr.Length == 0) { NeedQr(); return; }

        SetBusy(true);
        try
        {
            var prep = await _api.PrepareEntryAsync(qr);
            var plate = J.Str(J.Obj(prep, "vehicle"), "plate");
            var slot = J.Str(J.Obj(prep, "slot"), "id");
            var fee = J.Num(prep, "amount");

            using var dlg = new PaymentForm(plate, slot, fee);
            if (dlg.ShowDialog(this) != DialogResult.OK)
            {
                ShowStatus("Entry cancelled.", error: false, neutral: true);
                return;
            }

            var result = await _api.PayAndEnterAsync(qr, dlg.Amount);
            var session = J.Obj(result, "session");
            var usedSlot = J.Str(session, "slot");
            var change = dlg.Amount - fee;

            var msg = $"ENTRY OK: {plate} parked in slot {usedSlot}. " +
                      $"Paid ₱{PaymentForm.Money(dlg.Amount)}" +
                      (change > 0 ? $", change ₱{PaymentForm.Money(change)}." : ".");
            ShowStatus(msg, error: false);
            AddActivity($"ENTRY  {plate} -> {usedSlot}");

            txtQr.Clear();
            await RefreshSlotsAsync();
        }
        catch (ApiException ex)
        {
            ShowStatus(ex.Message, error: true);
            HandleAuthError(ex);
        }
        finally
        {
            SetBusy(false);
            if (!_ending) txtQr.Focus();
        }
    }

    // =====================================================================
    // Exit
    // =====================================================================
    private async void OnExit(object? sender, EventArgs e)
    {
        var qr = txtQr.Text.Trim();
        if (qr.Length == 0) { NeedQr(); return; }

        SetBusy(true);
        try
        {
            var prep = await _api.PrepareExitAsync(qr);
            var plate = J.Str(J.Obj(prep, "vehicle"), "plate");
            var session = J.Obj(prep, "session");

            var text = $"Vehicle: {plate}\n" +
                       $"Slot: {J.Str(session, "slot")}\n" +
                       $"Entered: {FormatTime(J.Str(session, "in"))}\n" +
                       $"Payment: PAID\n\nAllow this vehicle to exit?";

            var answer = MessageBox.Show(this, text, "Confirm exit",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                ShowStatus("Exit cancelled.", error: false, neutral: true);
                return;
            }

            var result = await _api.ExitAsync(qr);
            var released = J.Str(result, "slotReleased");

            ShowStatus($"EXIT OK: {plate} has left. Slot {released} is now free.", error: false);
            AddActivity($"EXIT   {plate} <- {released}");

            txtQr.Clear();
            await RefreshSlotsAsync();
        }
        catch (ApiException ex)
        {
            ShowStatus(ex.Message, error: true);
            HandleAuthError(ex);
        }
        finally
        {
            SetBusy(false);
            if (!_ending) txtQr.Focus();
        }
    }

    // =====================================================================
    // Helpers
    // =====================================================================
    private void NeedQr()
    {
        ShowStatus("Scan or type a QR code first.", error: true);
        txtQr.Focus();
    }

    private void SetBusy(bool busy)
    {
        btnEntry.Enabled = !busy;
        btnExit.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void ShowStatus(string message, bool error, bool neutral = false)
    {
        lblStatus.ForeColor = neutral ? Theme.Muted : error ? Theme.BadText : Theme.OkText;
        lblStatus.Text = message;
    }

    private void AddActivity(string text)
    {
        lstActivity.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}   {text}");
        while (lstActivity.Items.Count > 50) lstActivity.Items.RemoveAt(lstActivity.Items.Count - 1);
    }

    private static string FormatTime(string iso) =>
        DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.ToLocalTime().ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture)
            : iso;

    /// <summary>If the login token expired (1 hour), send the guard back to the login screen.</summary>
    private void HandleAuthError(ApiException ex)
    {
        var expired = ex.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                      (ex.StatusCode == System.Net.HttpStatusCode.Forbidden && ex.Message == "Not authenticated");
        if (!expired || _ending) return;

        _ending = true;
        _timer.Stop();
        MessageBox.Show(this, "Your session has expired. Please log in again.", "Session expired",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoggedOut = true;
        Close();
    }
}
