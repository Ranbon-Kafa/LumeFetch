using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace LumeFetch.Android;

[Application]
public sealed class Application(nint javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<MobileApp>(javaReference, transfer)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont().LogToTrace();
}
