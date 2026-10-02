using Microsoft.UI.Xaml;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private bool _lifecycleGuardsInitialized;

        public void InitializeLifecycleGuards()
        {
            if (_lifecycleGuardsInitialized)
                return;

            _lifecycleGuardsInitialized = true;
            Closed += MainWindow_ExtendedClosed;
        }

        private void MainWindow_ExtendedClosed(object sender, WindowEventArgs args)
        {
            try
            {
                _pageEnhancementTimer?.Stop();
            }
            catch { }

            try
            {
                _compromiseScanCts?.Cancel();
                _compromiseScanCts?.Dispose();
                _compromiseScanCts = null;
            }
            catch { }
        }
    }
}
