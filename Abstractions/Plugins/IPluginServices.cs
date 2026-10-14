namespace SharpPress.Plugins
{
    public interface IPluginServices
    {
        void Replace<T>(T implementation) where T : class;
        void Decorate<T>(Func<T, T> decorator) where T : class;
    }
}
