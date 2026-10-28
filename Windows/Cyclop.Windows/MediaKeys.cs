namespace Cyclop.Windows;

internal static class MediaKeys
{
    private const uint KeyUp=0x0002;
    private const byte VkMediaNext=0xB0, VkMediaPrevious=0xB1, VkMediaPlayPause=0xB3;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey,byte scanCode,uint flags,nuint extraInfo);
    public static void Previous()=>Send(VkMediaPrevious);
    public static void Next()=>Send(VkMediaNext);
    public static void PlayPause()=>Send(VkMediaPlayPause);
    private static void Send(byte key){keybd_event(key,0,0,0);keybd_event(key,0,KeyUp,0);}
}
