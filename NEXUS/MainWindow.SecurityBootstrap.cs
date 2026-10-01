using Microsoft.UI.Xaml;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        public void InitializeSecurityCompromiseUI()
        {
            if (_compromiseCard != null)
                return;

            if (_securityDepartmentBody == null)
                return;

            _compromiseCard = BuildCompromiseCard();
            _securityDepartmentBody.Children.Add(_compromiseCard);
        }
    }
}
