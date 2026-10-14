namespace SharpPress.Events
{
    public class CategoryDeletedEvent : BaseEvent
    {
        public int CategoryId { get; }
        public string Name { get; }
        public CategoryDeletedEvent(int categoryId, string name) { CategoryId = categoryId; Name = name; }
    }
}
