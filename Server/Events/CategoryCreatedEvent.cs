using SharpPress.Models;

namespace SharpPress.Events
{
    public class CategoryCreatedEvent : BaseEvent
    {
        public Category Category { get; }
        public CategoryCreatedEvent(Category category) => Category = category;
    }
}
