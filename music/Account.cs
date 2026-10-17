using System.Security.Principal;

namespace OmawinMusic;

// Named pipes are one namespace for the whole PC, not per Windows account: two accounts'
// players would answer each other. Every pipe name carries this account's SID.
static class Account
{
    static readonly string Sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;

    public static string Pipe(string name) => $"{name}-{Sid}";
}
