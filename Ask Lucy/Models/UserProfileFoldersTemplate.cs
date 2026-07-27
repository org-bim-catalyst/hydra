namespace AskLucy.Models
{
    public class UserProfileFoldersTemplate
    {
        public UserProfileFoldersTemplate()
        {
            this.Collection = new HashSet<UserProfileFoldersTemplateItem>();
        }

        public required string Name { get; set; }
        public IEnumerable<UserProfileFoldersTemplateItem> Collection { get; set; }

        public class UserProfileFoldersTemplateItem
        {
            public required string Title { get; set; }
            public required string Description { get; set; }
        }
    }
}