namespace Binexus.Platform.Runtime;

public sealed class CloudRuntimeDescriptor : IRuntimeDescriptor
{
    public RuntimeMode Mode => RuntimeMode.Cloud;
}
