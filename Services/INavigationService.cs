namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Contrat pour la navigation entre les écrans.
    /// </summary>
    public interface INavigationService
    {
        void NaviguerVers(object viewModel);
    }
}