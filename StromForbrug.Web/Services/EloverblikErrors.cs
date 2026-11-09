using Eloverblik;

namespace StromForbrug.Web.Services;

/// <summary>Danish texts for the Eloverblik failures that need more than the status line.</summary>
public static class EloverblikErrors
{
    /// <summary>
    /// The relay (api/eloverblik.php) could not be reached at all. The app talks to Eloverblik
    /// through it because browsers may not read Eloverblik's answers directly.
    /// </summary>
    public static string UnreadableText(EloverblikApiException ex) =>
        "Kunne ikke få forbindelse til Eloverblik via webstedets relæ (api/eloverblik.php). " +
        "Tjek internetforbindelsen, og prøv igen om lidt.";

    /// <summary>Eloverblik could not read the token (500 at the token exchange): it is incomplete.</summary>
    public const string TokenUnreadableText =
        "Eloverblik kunne ikke læse tokenet – det mangler sikkert et stykke. Tokenet er flere tusinde tegn langt, " +
        "og på telefon og iPad bliver det let kun kopieret delvist. Kopiér det igen fra eloverblik.dk " +
        "med kopiknappen, og indsæt det på ny.";

    /// <summary>The answer did not come from Eloverblik: the site is not running the PHP relay.</summary>
    public static string NotEloverblikText(EloverblikApiException ex) =>
        "Webstedet videresender ikke opslag til Eloverblik. StromForbrug kræver, at api/eloverblik.php kan køre " +
        "som PHP på webstedet – det kan det fx ikke på GitHub Pages.";
}
