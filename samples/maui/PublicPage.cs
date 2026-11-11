namespace UmbralSample;

/// <summary>Open to anyone, signed in or not.</summary>
public class PublicPage : ContentPage
{
    public PublicPage()
    {
        Title = "Public";
        Content = new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 12,
            Children =
            {
                new Label { Text = "Public", FontSize = 32, AutomationId = "title" },
                new Label { Text = "This page is open to anyone, signed in or not.", AutomationId = "public-text" },
            },
        };
    }
}
