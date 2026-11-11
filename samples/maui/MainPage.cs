namespace UmbralSample;

/// <summary>Home: the way to the other two screens, and Sign in or Sign out.</summary>
public class MainPage : ContentPage
{
    private readonly Button signIn = new() { Text = "Sign in", AutomationId = "sign-in" };
    private readonly Button signOut = new() { Text = "Sign out", AutomationId = "sign-out" };
    private readonly Label status = new() { TextColor = Colors.Red, AutomationId = "status", IsVisible = false };

    public MainPage()
    {
        Title = "Umbral sample";

        var publicButton = new Button { Text = "Public", AutomationId = "public" };
        publicButton.Clicked += async (_, _) =>
        {
            ShowStatus(null);
            await Navigation.PushAsync(new PublicPage());
        };

        var privateButton = new Button { Text = "Private", AutomationId = "private" };
        privateButton.Clicked += async (_, _) =>
        {
            ShowStatus(null);
            // Signed in or not, the private page decides: it starts the sign-in itself.
            await Navigation.PushAsync(new PrivatePage(this));
        };

        signIn.Clicked += async (_, _) => await SignInAsync();
        signOut.Clicked += async (_, _) => await SignOutAsync();

        Content = new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 12,
            Children =
            {
                new Label { Text = "Umbral sample", FontSize = 32, AutomationId = "title" },
                publicButton,
                privateButton,
                signIn,
                signOut,
                status,
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    /// <summary>Shows Sign in or Sign out, whichever fits what is stored.</summary>
    public async Task RefreshAsync()
    {
        var signedIn = await UmbralAuth.Shared.GetClaimsAsync() is not null;
        signIn.IsVisible = !signedIn;
        signOut.IsVisible = signedIn;
    }

    public void ShowStatus(string? message)
    {
        status.Text = message;
        status.IsVisible = !string.IsNullOrEmpty(message);
    }

    private async Task SignInAsync()
    {
        ShowStatus(null);
        var outcome = await UmbralAuth.Shared.SignInAsync();
        ShowStatus(outcome.Message);
        await RefreshAsync();
    }

    private async Task SignOutAsync()
    {
        ShowStatus(null);
        // Signed out as soon as this starts: the home screen shows Sign in even when the visit to
        // Umbral's end-session page fails.
        var message = await UmbralAuth.Shared.SignOutAsync();
        ShowStatus(message);
        await RefreshAsync();
    }
}
