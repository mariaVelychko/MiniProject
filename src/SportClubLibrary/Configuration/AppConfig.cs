namespace SportClubLibrary.Configuration
{
    public class StorageConfig
    {
        public string Type { get; set; }
        public string Path { get; set; }
    }

    public class AppConfig
    {
        public StorageConfig Storage { get; set; }
    }
}
