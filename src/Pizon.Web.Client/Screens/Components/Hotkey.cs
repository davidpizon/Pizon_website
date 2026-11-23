using Microsoft.AspNetCore.Components;
using Pizon.Doors.Sdk;

namespace Pizon.Web.Client.Screens.Components;

/// <summary>
/// A key the enclosing <see cref="BbsScreen"/> answers to (a click on a hotspot in the art sends the same
/// key). It runs <see cref="OnPress"/>, then goes to <see cref="Href"/> if it has one. Hotkeys are tried in the order they are declared, and only while
/// <see cref="When"/> holds. It renders no HTML; the screen's mirror lists it as a link.
/// </summary>
public sealed class Hotkey : ComponentBase, IDisposable
{
    private BbsScreen? registered;

    /// <summary>Gets or sets the enclosing screen.</summary>
    [CascadingParameter]
    public BbsScreen? Owner { get; set; }

    /// <summary>
    /// Gets or sets the key: one character (matched without regard to case) or a key name such as
    /// <see cref="DoorKeys.Escape"/>.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public required string Key { get; set; }

    /// <summary>Gets or sets the address to go to.</summary>
    [Parameter]
    public string? Href { get; set; }

    /// <summary>Gets or sets a value indicating whether the hotkey is active.</summary>
    [Parameter]
    public bool When { get; set; } = true;

    /// <summary>Gets or sets the label read out by the screen-reader mirror.</summary>
    [Parameter]
    public string? Label { get; set; }

    /// <summary>Gets or sets what happens when the key is pressed (before going anywhere).</summary>
    [Parameter]
    public EventCallback<string> OnPress { get; set; }

    /// <summary>
    /// Checks a key against this hotkey.
    /// </summary>
    /// <param name="key">The key pressed.</param>
    /// <returns><see langword="true"/> when the hotkey is active and the key is its key.</returns>
    public bool Matches(string? key) =>
        When && (Key.Length == 1 ? DoorKeys.Is(key, Key[0]) : string.Equals(key, Key, StringComparison.Ordinal));

    /// <inheritdoc />
    public void Dispose() => registered?.RemoveHotkey(this);

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        registered = Owner ?? throw new InvalidOperationException("A Hotkey must be inside a BbsScreen.");
        registered.AddHotkey(this);
    }

    /// <inheritdoc />
    protected override bool ShouldRender() => false;
}
