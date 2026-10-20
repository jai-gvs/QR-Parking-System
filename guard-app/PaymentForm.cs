using System.Globalization;

namespace BcpGuardApp;

/// <summary>Pop-up where the guard enters the cash received for a parking entry.</summary>
public class PaymentForm : Form
{
    private readonly NumericUpDown num = new();
    private readonly Label lblChange = new();
    private readonly double _fee;

    public double Amount => (double)num.Value;

    public PaymentForm(string plate, string slot, double fee)
    {
        _fee = fee;

        Text = "Collect payment";
        ClientSize = new Size(380, 310);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10f);

        Controls.Add(new Label
        {
            Text = $"Vehicle: {plate}", Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            AutoSize = true, Location = new Point(20, 18)
        });
        Controls.Add(new Label
        {
            Text = $"Assigned slot: {slot}", ForeColor = Theme.Muted, AutoSize = true, Location = new Point(20, 58)
        });
        Controls.Add(new Label
        {
            Text = $"Parking fee: ₱{Money(fee)}", ForeColor = Theme.Accent,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold), AutoSize = true, Location = new Point(20, 88)
        });

        Controls.Add(new Label
        {
            Text = "Amount received (₱)", ForeColor = Theme.Muted, AutoSize = true, Location = new Point(20, 130)
        });
        num.DecimalPlaces = 2;
        num.Minimum = 0;
        num.Maximum = 100000;
        num.Increment = 5;
        num.Value = (decimal)fee;
        num.Font = new Font("Segoe UI", 14f);
        num.BackColor = Theme.Field;
        num.ForeColor = Theme.Text;
        num.Location = new Point(20, 154);
        num.Width = 220;
        num.ValueChanged += (_, _) => UpdateChange();
        Controls.Add(num);

        lblChange.Location = new Point(20, 204);
        lblChange.AutoSize = true;
        lblChange.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        Controls.Add(lblChange);
        UpdateChange();

        var btnOk = new Button { Text = "Confirm payment", Size = new Size(170, 44), Location = new Point(20, 248) };
        Theme.StyleButton(btnOk, Theme.Green, Color.White);
        btnOk.Click += (_, _) =>
        {
            if (num.Value < (decimal)_fee)
            {
                MessageBox.Show(this, $"Payment must be at least ₱{Money(_fee)}.", "Not enough",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        };
        Controls.Add(btnOk);

        var btnCancel = new Button
        {
            Text = "Cancel", Size = new Size(150, 44), Location = new Point(210, 248),
            DialogResult = DialogResult.Cancel
        };
        Theme.StyleButton(btnCancel, Theme.Panel, Theme.Text);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void UpdateChange()
    {
        var diff = (double)num.Value - _fee;
        if (diff >= 0)
        {
            lblChange.ForeColor = Theme.OkText;
            lblChange.Text = $"Change to give: ₱{Money(diff)}";
        }
        else
        {
            lblChange.ForeColor = Theme.BadText;
            lblChange.Text = $"Short by ₱{Money(-diff)}";
        }
    }

    public static string Money(double v) => v.ToString("N2", CultureInfo.InvariantCulture);
}
