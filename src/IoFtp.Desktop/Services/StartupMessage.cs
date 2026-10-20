namespace IoFtp.Desktop.Services;

internal sealed class StartupMessage
{
    private static readonly string[] Messages =
    [
        "Warming up the flux capacitor...",
        "Negotiating with the packet hamsters...",
        "Downloading more bandwidth...",
        "Polishing the progress bars...",
        "Giving the bytes a head start...",
        "Turning coffee into file transfers...",
        "Untangling the network cables...",
        "Teaching packets to take the fast lane...",
        "Looking for a parking slot for your files...",
        "Your files have cleared their schedule.",
        "Less waiting. More transferring.",
        "All ports lead to FluxFTP.",
        "Checking that the Internet is still plugged in...",
        "Preparing a soft landing for incoming files...",
        "Putting the RAM disk on the starting grid...",
        "Ready when your bytes are."
    ];
    public string Text { get; } = Messages[Random.Shared.Next(Messages.Length)];
    public bool TransferStarted { get; private set; }
    public void StartTransfer() => TransferStarted = true;
    public string Display(string normalStatus) => TransferStarted ? normalStatus : Text;
}
