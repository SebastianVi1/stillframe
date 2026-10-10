using System.Windows.Threading;
using Stillframe.Services;

namespace Stillframe;

public partial class App : Application
{
    private Mutex? instance;
    private AppController? controller;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AccessibilityTheme.Initialize();
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        var profileIndex = Array.IndexOf(e.Args, "--profile");
        var profile = profileIndex >= 0 && profileIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[profileIndex + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stillframe");
        Log.Folder = profile;
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(profile.ToUpperInvariant())))[..16];
        instance = new Mutex(true, "Local\\Stillframe-" + digest, out var created);
        if (!created)
        {
            // A second launch asks the existing instance to open Settings.
            try { Directory.CreateDirectory(profile); File.WriteAllText(Path.Combine(profile, "open-settings.request"), "open"); } catch (IOException) { }
            Shutdown(); return;
        }
        DispatcherUnhandledException += OnUnhandled;
        try
        {
            Log.Write("Starting Stillframe 0.1; OS=" + Environment.OSVersion.Version);
            controller = new AppController(this, new PreferencesStore(profile));
            await controller.InitializeAsync(e.Args);
            var verifyIndex = Array.IndexOf(e.Args, "--verify");
            if (verifyIndex >= 0 && verifyIndex + 1 < e.Args.Length)
                await Verification.RunAsync(controller, Path.GetFullPath(e.Args[verifyIndex + 1]));
        }
        catch (Exception exception)
        {
            Log.Write("Startup failed: " + exception);
            MessageBox.Show("Stillframe could not start. Details are saved in " + Path.Combine(profile, "app.log"), "Stillframe", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Write("Unhandled UI error: " + e.Exception);
        // Unexpected errors must not leave an invisible, partly running application.
        controller?.Dispose();
        e.Handled = true;
        Shutdown(1);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        controller?.Dispose(); instance?.Dispose(); base.OnExit(e);
    }
}
