namespace AskLucy.Models
{
    public class ChatMessages
    {
        public string id { get; set; }
        public Author author { get; set; }
        public float create_time { get; set; }
        public Content content { get; set; }
        public string status { get; set; }
        public bool end_turn { get; set; }
        public float weight { get; set; }
        public Metadata metadata { get; set; }
        public string recipient { get; set; }
    }

    public class Author
    {
        public string role { get; set; }
        public Metadata metadata { get; set; }
    }

    public class Metadata
    { 
        public string model_slug { get; set; }
        public Finish_Details finish_details { get; set; }
        public string timestamp_ { get; set; }
    }

    public class Finish_Details
    {
        public string type { get; set; }
        public string stop { get; set; }
    }

    public class Content
    {
        public string content_type { get; set; }
        public string[] parts { get; set; }
    }
}