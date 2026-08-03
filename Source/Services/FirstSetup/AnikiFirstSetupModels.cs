using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace AnikiHelper.Services.FirstSetup
{
    public abstract class AnikiFirstSetupBindableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetValue<T>(
            ref T field,
            T value,
            [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class AnikiFirstSetupChoice : AnikiFirstSetupBindableBase
    {
        private bool isSelected;

        public string Key { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public string PreviewPath { get; set; }

        public bool IsRecommended { get; set; }

        public ICommand SelectCommand { get; set; }

        public bool IsSelected
        {
            get => isSelected;
            set => SetValue(ref isSelected, value);
        }
    }
}
