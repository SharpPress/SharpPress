namespace SharpPress.Services
{
    public sealed class DashboardWidget
    {
        public string Owner { get; init; } = "";
        public string Title { get; init; } = "";
        public int Order { get; init; }
        public Func<Task<string>> Render { get; init; } = () => Task.FromResult("");
    }

    public interface IDashboardService
    {
        IReadOnlyList<DashboardWidget> Widgets { get; }
        IDisposable Register(DashboardWidget widget);
    }
}
