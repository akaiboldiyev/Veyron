using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cyclop.Windows;

internal static class ShelfThumbnailCache
{
    private const int CacheLimit=120;
    private static readonly object Gate=new();
    private static readonly Dictionary<string,(Task<BitmapSource?> Task,LinkedListNode<string> Node)> Cache=new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> Recent=new();

    public static Task<BitmapSource?> LoadAsync(string path)
    {
        lock(Gate)
        {
            if(Cache.TryGetValue(path,out var cached))
            {
                Recent.Remove(cached.Node);Recent.AddFirst(cached.Node);return cached.Task;
            }
            var node=Recent.AddFirst(path);var task=LoadCoreAsync(path);Cache[path]=(task,node);
            if(Cache.Count>CacheLimit&&Recent.Last is { } oldest){Recent.RemoveLast();Cache.Remove(oldest.Value);}
            return task;
        }
    }

    private static Task<BitmapSource?> LoadCoreAsync(string path)=>Task.Run<BitmapSource?>(()=>
    {
        if(!File.Exists(path)&&!Directory.Exists(path))return null;
        try
        {
            var iid=new Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B");
            var hr=SHCreateItemFromParsingName(path,0,ref iid,out var factory);
            if(hr<0)Marshal.ThrowExceptionForHR(hr);
            try
            {
                hr=factory.GetImage(new SizeI(192,112),ThumbnailOnly|ResizeToFit,out var bitmap);
                if(hr<0)Marshal.ThrowExceptionForHR(hr);
                try
                {
                    var image=System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bitmap,0,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze();
                    return image;
                }
                finally{DeleteObject(bitmap);}
            }
            finally{Marshal.ReleaseComObject(factory);}
        }
        catch
        {
            if(!IsImage(path))return null;
            try
            {
                using var stream=File.OpenRead(path);
                var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=192;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
            }
            catch{return null;}
        }
    });

    private static bool IsImage(string path)=>new[]{".png",".jpg",".jpeg",".tif",".tiff",".bmp",".gif",".webp",".heic",".heif"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    private const uint ResizeToFit=0,ThumbnailOnly=8;
    [StructLayout(LayoutKind.Sequential)]private struct SizeI{public int Width,Height;public SizeI(int width,int height){Width=width;Height=height;}}
    [ComImport,Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory{[PreserveSig]int GetImage(SizeI size,uint flags,out nint bitmap);}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]
    private static extern int SHCreateItemFromParsingName(string path,nint bindContext,ref Guid interfaceId,out IShellItemImageFactory factory);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern bool DeleteObject(nint handle);
}

internal sealed class ShelfPreviewImage:Image
{
    public static readonly DependencyProperty FilePathProperty=DependencyProperty.Register(nameof(FilePath),typeof(string),typeof(ShelfPreviewImage),new PropertyMetadata(null,OnFilePathChanged));
    public string? FilePath{get=>(string?)GetValue(FilePathProperty);set=>SetValue(FilePathProperty,value);}
    private static async void OnFilePathChanged(DependencyObject sender,DependencyPropertyChangedEventArgs args)
    {
        if(sender is not ShelfPreviewImage image)return;
        var path=args.NewValue as string;image.Source=null;
        if(string.IsNullOrWhiteSpace(path))return;
        var preview=await ShelfThumbnailCache.LoadAsync(path);
        if(image.Dispatcher.HasShutdownStarted||image.FilePath!=path)return;
        image.Source=preview;
    }
}
