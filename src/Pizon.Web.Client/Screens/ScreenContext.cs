using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Doors;
using Pizon.Web.Client.Services;
using Pizon.Web.Client.Terminal;

namespace Pizon.Web.Client.Screens;

/// <summary>
/// Everything screens share: the terminal, the API, content, presence, navigation, storage and the clock.
/// </summary>
/// <param name="Terminal">The terminal.</param>
/// <param name="Api">The server API.</param>
/// <param name="Content">The editable content files.</param>
/// <param name="Presence">Callers-online presence.</param>
/// <param name="Navigator">Full-page navigation.</param>
/// <param name="Storage">Browser storage.</param>
/// <param name="Doors">The door host.</param>
/// <param name="Time">The clock.</param>
public sealed record ScreenContext(
    ITerminal Terminal,
    IBbsApi Api,
    ContentStore Content,
    IPresence Presence,
    IExternalNavigator Navigator,
    IBrowserStorage Storage,
    DoorHost Doors,
    TimeProvider Time);
