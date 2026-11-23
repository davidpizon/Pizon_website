namespace Pizon.Web.Client.Screens.Pages;

/// <summary>
/// [R] Reply (<c>/board/reply</c>): asks which message, then takes the reply.
/// </summary>
public sealed partial class BoardReplyPage
{
    /// <inheritdoc />
    protected override bool Reply => true;
}
