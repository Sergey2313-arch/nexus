using Microsoft.UI.Xaml;

namespace NEXUS
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            MainWindow mainWindow = new();

            // Базовые модули и страницы.
            mainWindow.InitializeDiagnosticsUI();
            mainWindow.InitializeMainPagesUI();
            mainWindow.InitializeSecurityCompromiseUI();

            // Дополнительные функции интерфейса.
            mainWindow.InitializePageEnhancements();
            mainWindow.InitializeExtraTools();
            mainWindow.InitializeLogbookGrouping();
            mainWindow.InitializePlannerV2();
            mainWindow.InitializeSafeMaintenanceCleanup();
            mainWindow.InitializeLifecycleGuards();
            mainWindow.InitializeSecurityAuditUI();
            mainWindow.InitializeFullSystemReportUI();
            mainWindow.InitializeFullReportAssistantBridge();
            mainWindow.InitializeTelemetryHistory();

            _window = mainWindow;
            _window.Activate();
        }
    }
}
