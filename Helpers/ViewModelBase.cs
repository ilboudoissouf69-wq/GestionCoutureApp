using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GestionCoutureApp.Helpers
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T champ, T valeur, [CallerMemberName] string? propriete = null)
        {
            if (Equals(champ, valeur)) return false;
            champ = valeur;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propriete));
            return true;
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private string _messageErreur = string.Empty;
        public string MessageErreur
        {
            get => _messageErreur;
            set => SetProperty(ref _messageErreur, value);
        }
    }
}
