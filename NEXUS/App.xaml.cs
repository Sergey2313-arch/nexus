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
            mainWindow.InitializeDiagnosticsUI();

            _window = mainWindow;
            _window.Activate();
        }
    }
}
