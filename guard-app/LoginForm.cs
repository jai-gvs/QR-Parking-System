namespace BcpGuardApp;

public class LoginForm : Form
{
    private readonly ApiClient _api;
    private readonly TextBox txtServer = new() { Text = "http://127.0.0.1:8000" };
    private readonly TextBox txtUser = new();
    private readonly TextBox txtPass = new() { UseSystemPasswordChar = true };
    private readonly Button btnLogin = new() { Text = "Login" };
    private readonly Label lblStatus = new();

    public LoginForm(ApiClient api)
    {
        _api = api;

        Text = "BCP Gate System - Login";
        ClientSize = new Size(400, 400);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10f);

        Controls.Add(new Label
        {
            Text = "BESTLINK COLLEGE OF THE PHILIPPINES",
            ForeColor = Theme.Accent, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            AutoSize = true, Location = new Point(30, 22)
        });
        Controls.Add(new Label
        {
            Text = "BCP Gate System",
            Font = new Font("Segoe UI", 20f, FontStyle.Bold),
            AutoSize = true, Location = new Point(26, 42)
        });
        Controls.Add(new Label
        {
            Text = "Guard station login",
            ForeColor = Theme.Muted, AutoSize = true, Location = new Point(30, 92)
        });

        AddField("Server address", txtServer, 130);
        AddField("Username", txtUser, 188);
        AddField("Password", txtPass, 246);

        Theme.StyleButton(btnLogin, Theme.Accent, Color.Black);
        btnLogin.Size = new Size(340, 44);
        btnLogin.Location = new Point(30, 308);
        btnLogin.Click += OnLogin;
        Controls.Add(btnLogin);

        lblStatus.Location = new Point(30, 360);
        lblStatus.Size = new Size(340, 32);
        lblStatus.ForeColor = Theme.Muted;
        Controls.Add(lblStatus);

        AcceptButton = btnLogin;
    }

    private void AddField(string caption, TextBox box, int y)
    {
        Controls.Add(new Label
        {
            Text = caption, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(30, y)
        });
        Theme.StyleField(box);
        box.Location = new Point(30, y + 22);
        box.Size = new Size(340, 28);
        Controls.Add(box);
    }

    private async void OnLogin(object? sender, EventArgs e)
    {
        if (txtUser.Text.Trim().Length == 0 || txtPass.Text.Length == 0)
        {
            SetStatus("Please enter username and password.", error: true);
            return;
        }

        btnLogin.Enabled = false;
        SetStatus("Logging in...", error: false);

        try
        {
            await _api.LoginAsync(txtServer.Text, txtUser.Text.Trim(), txtPass.Text);

            if (_api.Role != "attendant" && _api.Role != "admin")
                throw new ApiException("Only attendant or admin accounts can use this app.");

            DialogResult = DialogResult.OK;   // closes the form
        }
        catch (ApiException ex)
        {
            SetStatus(ex.Message, error: true);
            btnLogin.Enabled = true;
        }
    }

    private void SetStatus(string message, bool error)
    {
        lblStatus.ForeColor = error ? Theme.BadText : Theme.Accent;
        lblStatus.Text = message;
    }
}
