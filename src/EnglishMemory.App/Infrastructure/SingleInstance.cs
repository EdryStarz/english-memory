using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using EnglishMemory.Core;
namespace EnglishMemory.App.Infrastructure;

internal static class SingleInstance
{
    private static Mutex? mutex;
    public static readonly uint OpenMessage = RegisterWindowMessage("EnglishMemory.OpenWindow.v1");
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
    public static bool Own() { var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DataPaths.Root.ToUpperInvariant())))[..24]; mutex = new Mutex(true, "Local\\EnglishMemory." + key, out bool created); if (!created) PostMessage(new nint(0xffff), OpenMessage, 0, 0); return created; }
}
