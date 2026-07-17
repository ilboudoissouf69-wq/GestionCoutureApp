using System.Windows.Controls;

namespace GestionCoutureApp.Services
{
    public class NavigationService : INavigationService
    {
        private readonly Frame _mainFrame;

        public NavigationService(Frame mainFrame)
        {
            _mainFrame = mainFrame;
        }

        public void NaviguerVers(object viewModel)
        {
            string viewModelName = viewModel.GetType().Name;
            string viewName = viewModelName.Replace("ViewModel", "View");

            Type? viewType = Type.GetType($"GestionCoutureApp.Views.{viewName}");

            if (viewType != null)
            {
                var view = (UserControl?)Activator.CreateInstance(viewType);
                if (view != null)
                {
                    view.DataContext = viewModel;
                    _mainFrame.Navigate(view);
                }
            }
        }
    }
}
