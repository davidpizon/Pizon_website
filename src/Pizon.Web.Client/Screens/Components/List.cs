using Microsoft.AspNetCore.Components;
using Pizon.Web.Client.Screens.Rendering;

namespace Pizon.Web.Client.Screens.Components;

/// <summary>
/// Binds lines to a block slot of the enclosing <see cref="BbsScreen"/>'s art, a page at a time. While more
/// pages follow, the <see cref="More"/> slot shows <see cref="MoreText"/>, and any key no hotkey took turns the
/// page. <see cref="OnLastPage"/> runs once the last page is showing. It renders no HTML.
/// </summary>
public sealed class List : ComponentBase, IDisposable
{
    /// <summary>The default "more" prompt; Enter is clickable.</summary>
    public const string DefaultMoreText = "|08-- more -- |[Enter]|14[Enter]|08 next page|]|07";

    private BbsScreen? registered;
    private IReadOnlyList<string>? shown;

    /// <summary>Gets or sets the enclosing screen.</summary>
    [CascadingParameter]
    public BbsScreen? Screen { get; set; }

    /// <summary>Gets or sets the block slot the lines go in.</summary>
    [Parameter]
    [EditorRequired]
    public required FieldSlot Slot { get; set; }

    /// <summary>Gets or sets the lines (pipe-code text, one per row). A new list starts again at page one.</summary>
    [Parameter]
    public IReadOnlyList<string> Items { get; set; } = [];

    /// <summary>Gets or sets the slot for the "more" prompt, if the list pages.</summary>
    [Parameter]
    public FieldSlot? More { get; set; }

    /// <summary>Gets or sets the "more" prompt.</summary>
    [Parameter]
    public string MoreText { get; set; } = DefaultMoreText;

    /// <summary>Gets or sets what happens once the last page is showing.</summary>
    [Parameter]
    public EventCallback OnLastPage { get; set; }

    /// <summary>Gets or sets the 0-based page showing.</summary>
    public int Page { get; set; }

    /// <summary>Gets a value indicating whether pages follow this one.</summary>
    public bool HasMore => More is not null && (Page + 1) * Slot.Height < Items.Count;

    /// <summary>Gets the lines on the page showing.</summary>
    public IEnumerable<string> Lines => More is null ? Items : Items.Skip(Page * Slot.Height);

    /// <summary>Gets or sets a value indicating whether <see cref="OnLastPage"/> has run for these items.</summary>
    internal bool LastPageSeen { get; set; }

    /// <inheritdoc />
    public void Dispose() => registered?.RemoveList(this);

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        registered = Screen ?? throw new InvalidOperationException("A List must be inside a BbsScreen.");
        if (!ReferenceEquals(shown, Items))
        {
            shown = Items;
            Page = 0;
            LastPageSeen = false;
            registered.SetList(this);
        }
    }

    /// <inheritdoc />
    protected override bool ShouldRender() => false;
}
