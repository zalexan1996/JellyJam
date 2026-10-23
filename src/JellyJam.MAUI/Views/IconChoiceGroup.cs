namespace JellyJam.Mobile.Views;

public sealed class IconChoiceGroup : Grid
{
    public static readonly BindableProperty OptionsProperty = BindableProperty.Create(
        nameof(Options), typeof(string), typeof(IconChoiceGroup), "", propertyChanged: (b, _, _) => ((IconChoiceGroup)b).Rebuild());
    public static readonly BindableProperty IconsProperty = BindableProperty.Create(
        nameof(Icons), typeof(string), typeof(IconChoiceGroup), "", propertyChanged: (b, _, _) => ((IconChoiceGroup)b).Rebuild());
    public static readonly BindableProperty SelectedProperty = BindableProperty.Create(
        nameof(Selected), typeof(string), typeof(IconChoiceGroup), "", BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((IconChoiceGroup)b).Highlight());

    private static readonly Color Active = Color.FromArgb("#173033");
    private static readonly Color Signal = Color.FromArgb("#71F7F3");
    private static readonly Color Surface = Color.FromArgb("#101A1F");
    private static readonly Color Idle = Color.FromArgb("#30484E");

    private readonly List<(string Name, ImageButton Button)> buttons = [];

    public IconChoiceGroup() => ColumnSpacing = 6;

    public string Options { get => (string)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    public string Icons { get => (string)GetValue(IconsProperty); set => SetValue(IconsProperty, value); }
    public string Selected { get => (string)GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }

    private void Rebuild()
    {
        var names = Options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var icons = Icons.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0 || names.Length != icons.Length) return;
        Children.Clear();
        ColumnDefinitions.Clear();
        buttons.Clear();
        for (var index = 0; index < names.Length; index++)
        {
            var name = names[index];
            var button = new ImageButton
            {
                Source = ImageSource.FromFile(icons[index] + ".png"), Padding = 12, HeightRequest = 48, WidthRequest = -1,
                HorizontalOptions = LayoutOptions.Fill
            };
            SemanticProperties.SetDescription(button, name);
            ToolTipProperties.SetText(button, name);
            button.Clicked += (_, _) => Selected = name;
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            SetColumn((BindableObject)button, index);
            Children.Add(button);
            buttons.Add((name, button));
        }
        Highlight();
    }

    private void Highlight()
    {
        foreach (var (name, button) in buttons)
        {
            var on = name == Selected;
            button.BackgroundColor = on ? Active : Surface;
            button.BorderColor = on ? Signal : Idle;
        }
    }
}
