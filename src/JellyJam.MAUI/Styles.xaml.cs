namespace JellyJam.Mobile;

public partial class Styles : ResourceDictionary
{
    public Styles() => InitializeComponent();

    public ResourceDictionary Theme(string name) => (ResourceDictionary)this[name];
}
