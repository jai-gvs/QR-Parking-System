namespace BcpGuardApp;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Login -> guard station. "Logout" comes back to the login screen.
        while (true)
        {
            var api = new ApiClient();

            using (var login = new LoginForm(api))
            {
                if (login.ShowDialog() != DialogResult.OK) return;
            }

            using var main = new MainForm(api);
            main.ShowDialog();
            if (!main.LoggedOut) return;   // window closed with the X button -> exit
        }
    }
}
