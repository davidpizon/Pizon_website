namespace Pizon.Doors.Sdk;

/// <summary>
/// The caller playing a door. Doors only ever see the handle and security level.
/// </summary>
/// <param name="Handle">The caller's BBS handle, or <c>GUEST</c>.</param>
/// <param name="IsAuthenticated">Whether the caller signed in (guests cannot save scores).</param>
/// <param name="SecurityLevel">0 for guests, 10 for signed-in callers, 255 for the SysOp.</param>
public sealed record DoorCaller(string Handle, bool IsAuthenticated, int SecurityLevel)
{
    /// <summary>Gets a guest caller.</summary>
    public static DoorCaller Guest { get; } = new("GUEST", false, 0);
}
