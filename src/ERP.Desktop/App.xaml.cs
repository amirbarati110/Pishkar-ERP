using ERP.Presentation.Help;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ERP.Desktop;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    public static AppServices Services { get; private set; } = null!;

    /// <summary>One cache of «راهنمای این صفحه» content (§4.1), shared by every page instead of each re-reading its own yaml file.</summary>
    public static WorkflowLoader Workflows { get; } = new();

    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();

        // A single unhandled exception anywhere used to take down the whole app with
        // no message and no trace (source-of-truth Appendix A #5). This does not fix
        // the underlying bug class, but it stops silent data loss: the user sees what
        // happened and a trace is written for support instead of a hard crash.
        UnhandledException += OnUnhandledException;
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // Startup used to await AppServices.InitializeAsync with no try/catch: a
        // missing database or client library (the normal state before a real
        // Installer exists — Appendix A #2/#4) crashed the process instantly with a
        // raw exception and no explanation. Fall back to a plain-language error
        // window instead, so the app fails visibly and recoverably.
        try
        {
            Services = await AppServices.InitializeAsync(CancellationToken.None);
            Window = new MainWindow();
        }
        catch (Exception exception)
        {
            Window = StartupErrorWindow.Create(exception);
        }

        Window.Activate();
    }

    private void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        TryLogCrash(e.Exception);
        // Let the app terminate normally afterward (e.Handled left false): the
        // process is not in a known-good state to keep running, but at least the
        // failure is now recorded instead of vanishing silently.
    }

    internal static void TryLogCrash(Exception exception)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PishkarERP",
                "logs");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(logPath, exception.ToString());
        }
        catch
        {
            // Logging must never throw during crash handling.
        }
    }
}
