/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.ComponentModel;
using Chummer.Desktop.ViewModels;

namespace Chummer.Desktop.Views.Sections
{
    public partial class ContactsSectionView : UserControl
    {
        private CharacterViewModel _owner;
        public ContactsSectionView()
        {
            AvaloniaXamlLoader.Load(this);
            DataContextChanged += (_, _) => Bind();
            AttachedToVisualTree += (_, _) => Bind();
            DetachedFromVisualTree += (_, _) => Unbind();
        }
        private void Bind()
        {
            Unbind(); _owner = DataContext as CharacterViewModel;
            if (_owner != null) _owner.PropertyChanged += Changed;
            ShowProfile();
        }
        private void Unbind()
        {
            if (_owner != null) _owner.PropertyChanged -= Changed;
            _owner = null;
            this.FindControl<ContentControl>("ContactProfileHost").Content = null;
        }
        private void Changed(object sender, PropertyChangedEventArgs e)
        { if (e.PropertyName == nameof(CharacterViewModel.SelectedContactProfile)) ShowProfile(); }
        private void ShowProfile()
        {
            var owner = _owner;
            this.FindControl<ContentControl>("ContactProfileHost").Content = owner?.SelectedContactProfile == null ? null
                : new ContactProfileView(owner.SelectedContactProfile, () => owner.SelectedContactProfile = null);
        }
    }
}
