using JellyJam.Mobile.ViewModels;

namespace JellyJam.Mobile.Views;

public partial class RadioPage : ContentPage
{
    private readonly RadioViewModel viewModel;

    public RadioPage(RadioViewModel viewModel, AndroidSpectrumSource spectrum, LogTail logTail)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
        LogBackdrop.Connect(logTail);
        Spectrum.Connect(spectrum);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.InitializeAsync();
        Spectrum.SetForeground(true);
    }

    protected override void OnDisappearing()
    {
        Spectrum.SetForeground(false);
        base.OnDisappearing();
    }

    public void SetForeground(bool foreground) => Spectrum.SetForeground(foreground);
}