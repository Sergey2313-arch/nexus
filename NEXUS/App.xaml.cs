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

            // Сначала создаём старые модули, затем общий роутер страниц
            // переносит Security и Diagnostics в отдельные разделы.
            mainWindow.InitializeDiagnosticsUI();
            mainWindow.InitializeCompromiseUI();
            mainWindow.InitializeMainPagesUI();

            _window = mainWindow;
            _window.Activate();
        }
    }
}
