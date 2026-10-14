namespace SharpPress.Plugins
{
    public interface IPlugin
    {
        string Name { get; }
        string Version { get; }
        string Author { get; }
        string? Description { get; }
        Task OnLoadAsync(IPluginContext context);
        Task OnUpdateAsync(IPluginContext context);
        Task OnUnloadAsync();
    }
}
