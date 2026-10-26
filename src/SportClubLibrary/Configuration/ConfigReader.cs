using System;
using System.IO;
using System.Text.Json;

namespace SportClubLibrary.Configuration
{
    public static class ConfigReader
    {
        private const string ConfigFile = "appsettings.json";

        public static AppConfig Load()
        {
            try
            {
                string json = File.ReadAllText(ConfigFile);

                return JsonSerializer.Deserialize<AppConfig>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("Файл конфігурації не знайдено. Використовую тип InMemory.");
                return new AppConfig
                {
                    Storage = new StorageConfig
                    {
                        Type = "InMemory",
                        Path = ""
                    }
                };
            }
            catch (JsonException ex)
            {
                Console.WriteLine("Помилка JSON у конфігурації: " + ex.Message);
                Console.WriteLine("Використовую тип InMemory.");
                return new AppConfig
                {
                    Storage = new StorageConfig
                    {
                        Type = "InMemory",
                        Path = ""
                    }
                };
            }
        }
    }
}
