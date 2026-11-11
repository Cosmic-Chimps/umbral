namespace UmbralSample;

/// <summary>
/// Signed out, opening this page starts the sign-in; on success it shows the claims, on cancel it
/// goes back home. Signed in, it shows the claims of the <c>id_token</c>.
/// </summary>
public class PrivatePage : ContentPage
{
    private readonly MainPage home;
    private readonly VerticalStackLayout body = new() { Padding = 24, Spacing = 12 };
    private bool started;

    public PrivatePage(MainPage home)
    {
        this.home = home;
        Title = "Private";
        Content = new ScrollView { Content = body };
        body.Children.Add(new Label { Text = "Private", FontSize = 32, AutomationId = "title" });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // OnAppearing runs again when the browser sheet closes: sign in once per visit.
        if (started)
        {
            return;
        }
        started = true;

        var claims = await UmbralAuth.Shared.GetClaimsAsync();
        if (claims is null)
        {
            if (!UmbralAuth.IsConfigured)
            {
                await GoHomeAsync(UmbralAuth.SetClientIdMessage);
                return;
            }
            var outcome = await UmbralAuth.Shared.SignInAsync();
            claims = outcome.Succeeded ? await UmbralAuth.Shared.GetClaimsAsync() : null;
            if (claims is null)
            {
                // Cancelled or failed: back to Home, with the reason when there is one.
                await GoHomeAsync(outcome.Cancelled ? null : outcome.Message);
                return;
            }
            if (outcome.Message is not null)
            {
                home.ShowStatus(outcome.Message);
            }
        }
        Show(claims);
    }

    private async Task GoHomeAsync(string? message)
    {
        home.ShowStatus(message);
        await Navigation.PopToRootAsync();
    }

    private void Show(IReadOnlyList<ClaimRow> claims)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(120)),
                new ColumnDefinition(GridLength.Star),
            },
            RowSpacing = 8,
        };
        for (var row = 0; row < claims.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Add(new Label { Text = claims[row].Name, FontAttributes = FontAttributes.Bold }, 0, row);
            grid.Add(new Label { Text = claims[row].Value, AutomationId = $"claim-{claims[row].Name}" }, 1, row);
        }
        body.Children.Add(grid);
    }
}
